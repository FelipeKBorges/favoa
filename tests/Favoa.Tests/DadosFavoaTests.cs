using System.Text;
using System.Text.RegularExpressions;
using Favoa.Core.Captura;
using Favoa.Core.Dados;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

/// <summary>Exportar e importar: o que sai tem que voltar igual.</summary>
public sealed class DadosFavoaTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = new(2026, 10, 10);
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), $"favoa-dados-{Guid.NewGuid():N}");
    private readonly List<(SqliteConnection Conexao, ServiceProvider Sp)> _bancos = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var (c, sp) in _bancos) { await sp.DisposeAsync(); await c.DisposeAsync(); }
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    private readonly Dictionary<DadosFavoa, IDbContextFactory<FavoaDbContext>> _fabricas = [];
    private readonly Dictionary<DadosFavoa, Guid> _pessoas = [];

    private async Task<(FavoaServico Servico, DadosFavoa Dados)> NovoBanco()
    {
        var conexao = new SqliteConnection("Data Source=:memory:");
        await conexao.OpenAsync();
        var sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(conexao)).BuildServiceProvider();
        await sp.PrepararBancoComCasaAsync();
        _bancos.Add((conexao, sp));
        var fabrica = sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        // Como no app: quem registra é sempre uma pessoa (a primeira da casa).
        await using var db = await fabrica.CreateDbContextAsync();
        var primeira = await db.Pessoas.Select(p => p.Id).FirstAsync();
        var dados = new DadosFavoa(fabrica, new OpcoesFavoa { PastaDados = _pasta });
        _fabricas[dados] = fabrica;
        _pessoas[dados] = primeira;
        return (new FavoaServico(fabrica, new Favoa.Core.Pessoas.PessoaFixa(primeira)), dados);
    }

    private static async Task Registrar(FavoaServico s, string frase)
    {
        var r = await s.RegistrarAsync(InterpretadorFrase.Interpretar(frase, await s.ObterContextoAsync(Hoje)));
        Assert.True(r.Sucesso, r.Mensagem);
    }

    [Fact]
    public async Task Exportar_e_importar_em_outro_banco_preserva_tudo()
    {
        var (origemServico, origem) = await NovoBanco();
        foreach (var f in new[] { "orçamento 3000", "gastei 32,50 no mercado com o nubank", "recebi 5000 de salário",
                                  "tarefa pagar luz sexta", "novo hábito ler", "fiz ler", "bebi 500 ml",
                                  "supino 3x10 40kg", "corri 5km 30min", "comi 150g de arroz tipo 1 cozido no almoço", "peso 82,5" })
            await Registrar(origemServico, f);
        var arquivo = await origem.ExportarJsonAsync();

        var (destinoServico, destino) = await NovoBanco();
        await Registrar(destinoServico, "gastei 999 em lazer"); // será substituído
        // Restaurar numa instalação sem login: as pessoas do arquivo substituem as daqui (com login, a pessoa de quem entra fica; veja o teste abaixo).
        await using (var db = await _fabricas[destino].CreateDbContextAsync())
        {
            db.Usuarios.RemoveRange(db.Usuarios);
            await db.SaveChangesAsync();
        }

        var analise = await destino.AnalisarImportacaoAsync(arquivo.Conteudo);
        Assert.True(analise.Valido, analise.Mensagem);
        Assert.Equal(2, analise.NoArquivo!.Lancamentos);

        var r = await destino.ImportarJsonAsync(arquivo.Conteudo);
        Assert.True(r.Sucesso, r.Mensagem);
        Assert.True(File.Exists(r.BackupAntes));

        var painelOrigem = await origemServico.ObterPainelAsync(Hoje);
        Assert.True(painelOrigem.Resumo.TemTreinos);
        // As pessoas vêm do arquivo (com os mesmos ids): o destino passa a ver os dados como a pessoa de origem.
        var painelDestino = await new FavoaServico(_fabricas[destino], new Favoa.Core.Pessoas.PessoaFixa(_pessoas[origem])).ObterPainelAsync(Hoje);
        Assert.Equal(painelOrigem.Resumo, painelDestino.Resumo);

        // Exportar de novo dá o mesmo conteúdo (fora a data de exportação).
        var deNovo = await destino.ExportarJsonAsync();
        Assert.Equal(SemData(arquivo.Conteudo), SemData(deNovo.Conteudo));
    }

    [Fact]
    public async Task Importar_mantem_a_pessoa_de_quem_tem_login_e_poe_os_registros_na_familia()
    {
        var (origemServico, origem) = await NovoBanco();
        foreach (var f in new[] { "gastei 32,50 no mercado", "tarefa pagar luz sexta", "supino 3x10 40kg", "comi 150g de arroz tipo 1 cozido no almoço" })
            await Registrar(origemServico, f);
        var arquivo = await origem.ExportarJsonAsync();

        var (_, destino) = await NovoBanco();
        var fabrica = _fabricas[destino];
        var r = await destino.ImportarJsonAsync(arquivo.Conteudo);
        Assert.True(r.Sucesso, r.Mensagem);

        await using var db = await fabrica.CreateDbContextAsync();
        var usuario = await db.Usuarios.SingleAsync();
        var familia = await db.Familias.Select(f => f.Id).SingleAsync();
        Assert.Contains(await db.Pessoas.Select(p => p.Id).ToListAsync(), id => id == usuario.PessoaId); // quem pode entrar continua existindo
        Assert.All(await db.Lancamentos.ToListAsync(), l => Assert.Equal(familia, l.FamiliaId));
        Assert.All(await db.Tarefas.ToListAsync(), t => Assert.Equal(familia, t.FamiliaId));
        Assert.All(await db.Exercicios.ToListAsync(), e => Assert.Equal(familia, e.FamiliaId));
        Assert.All(await db.Contas.ToListAsync(), c => Assert.Equal(familia, c.FamiliaId));
        Assert.All(await db.Pessoas.ToListAsync(), p => Assert.Equal(familia, p.FamiliaId));
        // TACO e IBGE continuam sendo catálogo comum, sem família.
        Assert.True(await db.Alimentos.AnyAsync(a => a.CodigoTaco != null && a.FamiliaId == null));
    }

    [Fact]
    public async Task Importar_e_zerar_sao_recusados_quando_ha_mais_de_uma_familia()
    {
        var (servico, dados) = await NovoBanco();
        await Registrar(servico, "gastei 10 no mercado");
        var arquivo = await dados.ExportarJsonAsync();
        Assert.True(await dados.BancoDeUmaFamiliaAsync());

        var outra = await new AcessoServico(_fabricas[dados]).CriarFamiliaAsync("Outra casa", "Bia", "bia@exemplo.com", CasaDeTeste.Senha);
        Assert.True(outra.Sucesso, outra.Mensagem);
        Assert.False(await dados.BancoDeUmaFamiliaAsync());

        var importar = await dados.ImportarJsonAsync(arquivo.Conteudo);
        Assert.False(importar.Sucesso);
        Assert.Contains("mais de uma família", importar.Mensagem);
        var zerar = await dados.ComecarDoZeroAsync();
        Assert.False(zerar.Sucesso);

        await using var db = await _fabricas[dados].CreateDbContextAsync();
        Assert.Equal(1, await db.Lancamentos.CountAsync()); // nada foi apagado
    }

    private static string SemData(byte[] json) =>
        Regex.Replace(Encoding.UTF8.GetString(json), "\"exportadoEm\":\\s*\"[^\"]+\"", "");

    [Theory]
    [InlineData("isso não é json")]
    [InlineData("{\"app\":\"outro\",\"versao\":1}")]
    [InlineData("{\"app\":\"favoa\",\"versao\":99}")]
    [InlineData("{\"app\":\"favoa\",\"versao\":1}")]
    [InlineData("{\"app\":\"favoa\",\"versao\":3}")]
    public async Task Arquivo_invalido_e_recusado_sem_mexer_no_banco(string conteudo)
    {
        var (servico, dados) = await NovoBanco();
        await Registrar(servico, "gastei 10 no mercado");

        var r = await dados.ImportarJsonAsync(Encoding.UTF8.GetBytes(conteudo));

        Assert.False(r.Sucesso);
        Assert.Equal(1, (await dados.ObterInformacoesAsync()).Contagem.Lancamentos);
    }

    [Fact]
    public async Task Lancamento_com_conta_inexistente_e_recusado()
    {
        var (servico, dados) = await NovoBanco();
        await Registrar(servico, "gastei 10 no mercado");
        var json = Encoding.UTF8.GetString((await dados.ExportarJsonAsync()).Conteudo);
        var quebrado = Regex.Replace(json, "\"contaId\":\\s*\"[^\"]+\"", $"\"contaId\": \"{Guid.NewGuid()}\"");

        var analise = await dados.AnalisarImportacaoAsync(Encoding.UTF8.GetBytes(quebrado));

        Assert.False(analise.Valido);
        Assert.Contains("contas", analise.Mensagem);
    }

    [Fact]
    public async Task Csv_de_lancamentos_abre_no_excel_brasileiro()
    {
        var (servico, dados) = await NovoBanco();
        await Registrar(servico, "gastei 32,50 no mercado arroz; feijão com o nubank");

        var bytes = (await dados.ExportarCsvAsync(ModuloCsv.Lancamentos)).Conteudo;

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]); // BOM
        var linhas = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Data;Tipo;Valor;Categoria;Conta;Descrição;Lançado por", linhas[0]);
        Assert.StartsWith("10/10/2026;Despesa;-32,50;Mercado;", linhas[1]);
        Assert.EndsWith(";Nubank;;Davi", linhas[1]); // lançamento novo não tem descrição livre
    }

    [Fact]
    public async Task Backup_manual_aparece_na_lista()
    {
        var (_, dados) = await NovoBanco();

        var caminho = await dados.FazerBackupAgoraAsync();

        Assert.True(File.Exists(caminho));
        Assert.Contains((await dados.ObterInformacoesAsync()).Backups, b => caminho.EndsWith(b.Nome));
    }

    [Fact]
    public async Task Comecar_do_zero_apaga_registros_e_mantem_pessoas_configuracoes_e_cadastros()
    {
        var (servico, dados) = await NovoBanco();
        foreach (var f in new[] { "orçamento 3000", "uber 23", "novo hábito ler", "fiz ler", "bebi 500 ml", "tarefa pagar luz", "supino 3x10 40kg", "peso 82", "comi 150g de arroz tipo 1 cozido no almoço" })
            await Registrar(servico, f);
        var fabrica = _fabricas[dados];
        int contas, categorias;
        await using (var db = await fabrica.CreateDbContextAsync())
            (contas, categorias) = (await db.Contas.CountAsync(), await db.Categorias.CountAsync());

        var r = await dados.ComecarDoZeroAsync();
        Assert.True(r.Sucesso, r.Mensagem);
        Assert.True(File.Exists(r.BackupAntes));

        await using (var db = await fabrica.CreateDbContextAsync())
        {
            Assert.Equal(0, await db.Lancamentos.CountAsync());
            Assert.Equal(0, await db.Tarefas.CountAsync());
            Assert.Equal(0, await db.Habitos.CountAsync());
            Assert.Equal(0, await db.RegistrosHabito.CountAsync());
            Assert.Equal(0, await db.RegistrosAgua.CountAsync());
            Assert.Equal(0, await db.Sessoes.CountAsync());
            Assert.Equal(0, await db.Consumos.CountAsync());
            Assert.Equal(0, await db.Pesos.CountAsync());
            Assert.Equal(contas, await db.Contas.CountAsync());
            Assert.Equal(categorias, await db.Categorias.CountAsync());
            Assert.True(await db.Alimentos.AnyAsync());
            Assert.Equal(1, await db.Usuarios.CountAsync()); // quem pode entrar não muda
            Assert.Equal(1, await db.Pessoas.CountAsync());
            Assert.Equal("300000", (await db.Configuracoes.FindAsync(Favoa.Core.Rotina.Configuracao.OrcamentoMensalCentavos))!.Valor);
        }
    }
}
