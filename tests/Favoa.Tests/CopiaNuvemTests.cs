using System.Diagnostics;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Favoa.Tests;

public sealed class CopiaNuvemTests : IAsyncLifetime
{
    private readonly string _raiz = Directory.CreateTempSubdirectory("favoa-nuvem-").FullName;
    private string PastaDados => Path.Combine(_raiz, "dados");
    private string Nuvem => Path.Combine(_raiz, "OneDrive", "Fávoa");
    private string Projeto => Path.Combine(_raiz, "projeto");
    private ServiceProvider _sp = null!;
    private CopiaNuvem _copia = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(PastaDados);
        var opcoes = new OpcoesFavoa { PastaDados = PastaDados };
        _sp = new ServiceCollection()
            .AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite($"Data Source={opcoes.CaminhoBanco}"))
            .BuildServiceProvider();
        await _sp.PrepararBancoComCasaAsync();
        var dados = new DadosFavoa(_sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>(), opcoes);
        _copia = new CopiaNuvem(opcoes, dados, NullLogger<CopiaNuvem>.Instance);

        Directory.CreateDirectory(Projeto);
        Git("init", "-q");
        File.WriteAllText(Path.Combine(Projeto, "leia.txt"), "fávoa");
        Git("add", "-A");
        Git("-c", "user.email=t@t", "-c", "user.name=t", "commit", "-q", "-m", "primeiro");
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_raiz, true); } catch (IOException) { }
    }

    private void Git(params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = Projeto, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    [Fact]
    public async Task Liga_e_copia_banco_dados_csv_backup_e_codigo()
    {
        var r = await _copia.ConfigurarAsync(Nuvem, Projeto);
        Assert.True(r.Sucesso, r.Mensagem);

        Assert.True(File.Exists(Path.Combine(Nuvem, "Dados", "favoa.db")));
        Assert.Contains("\"app\": \"favoa\"", File.ReadAllText(Path.Combine(Nuvem, "Dados", "favoa-dados.json")));
        Assert.True(File.Exists(Path.Combine(Nuvem, "Dados", "csv", "favoa-lancamentos.csv")));
        Assert.Single(Directory.GetFiles(Path.Combine(Nuvem, "Backups"), "favoa-*.db"));
        Assert.True(new FileInfo(Path.Combine(Nuvem, "Codigo", "favoa.bundle")).Length > 0);
        Assert.Empty(Directory.GetFiles(Nuvem, "*.tmp", SearchOption.AllDirectories));

        var estado = await _copia.ObterAsync();
        Assert.True(estado.Ativa);
        Assert.Null(estado.UltimoErro);
        Assert.NotNull(estado.UltimaCopia);

        // Sem mudanças: não copia de novo.
        Assert.Equal("Nada mudou desde a última cópia.", (await _copia.CopiarAsync(forcar: false)).Mensagem);
    }

    [Fact]
    public async Task Copia_de_novo_quando_o_banco_ou_o_codigo_mudam()
    {
        await _copia.ConfigurarAsync(Nuvem, Projeto);

        await using (var db = await _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>().CreateDbContextAsync())
        {
            db.Tarefas.Add(new Favoa.Core.Rotina.Tarefa { Titulo = "Nova" });
            await db.SaveChangesAsync();
        }
        File.SetLastWriteTimeUtc(Path.Combine(PastaDados, "favoa.db"), DateTime.UtcNow.AddMinutes(1));
        File.WriteAllText(Path.Combine(Projeto, "leia.txt"), "mudou");
        Git("-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qam", "segundo");

        var r = await _copia.CopiarAsync(forcar: false);
        Assert.Contains("dados", r.Mensagem);
        Assert.Contains("código", r.Mensagem);
        Assert.Contains("Nova", File.ReadAllText(Path.Combine(Nuvem, "Dados", "favoa-dados.json")));
    }

    [Fact]
    public async Task Valida_pastas_e_desliga()
    {
        Assert.False((await _copia.ConfigurarAsync("relativa", null)).Sucesso);
        Assert.False((await _copia.ConfigurarAsync(Path.Combine(PastaDados, "sub"), null)).Sucesso);
        Assert.False((await _copia.ConfigurarAsync(Nuvem, Path.Combine(_raiz, "sem-git"))).Sucesso);

        Assert.True((await _copia.ConfigurarAsync(Nuvem, null)).Sucesso);
        Assert.False(Directory.Exists(Path.Combine(Nuvem, "Codigo"))); // sem projeto, sem código
        Assert.True((await _copia.ConfigurarAsync("", null)).Sucesso);
        Assert.False((await _copia.ObterAsync()).Ativa);
        Assert.False((await _copia.CopiarAsync(forcar: true)).Sucesso);
    }
}
