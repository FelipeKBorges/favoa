using System.Security.Cryptography;
using Favoa.Core;
using Favoa.Core.Acesso;
using Favoa.Core.Diario;
using Favoa.Core.Pessoas;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

public sealed class DiarioTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.Now);
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private IDbContextFactory<FavoaDbContext> _fabrica = null!;
    private SessaoUsuario _ana = null!, _bia = null!, _ceu = null!;
    private readonly CifraDiario _cifra = new(RandomNumberGenerator.GetBytes(32));

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao)).BuildServiceProvider();
        await _sp.PrepararBancoAsync();
        _fabrica = _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        var acesso = new AcessoServico(_fabrica);
        _ana = (await acesso.CriarFamiliaAsync("Casa da Ana", "Ana", "ana@exemplo.com", CasaDeTeste.Senha)).Sessao!;
        _bia = (await acesso.CriarFamiliaAsync("Casa da Bia", "Bia", "bia@exemplo.com", CasaDeTeste.Senha)).Sessao!;
        // Outra pessoa na MESMA casa da Ana: o diário também é privado dentro da casa.
        await using var db = await _fabrica.CreateDbContextAsync();
        var pessoa = new Favoa.Core.Pessoas.Pessoa { Nome = "Ceu", FamiliaId = _ana.FamiliaId, Ordem = 5 };
        db.Pessoas.Add(pessoa);
        await db.SaveChangesAsync();
        _ceu = _ana with { PessoaId = pessoa.Id };
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private DiarioServico Servico(SessaoUsuario s) => new(_fabrica, new PessoaFixa(s.PessoaId, s.FamiliaId), _cifra);

    [Fact]
    public async Task Salva_le_atualiza_e_exclui_a_entrada_do_dia()
    {
        var d = Servico(_ana);
        Assert.Null(await d.ObterAsync(Hoje));

        var r = await d.SalvarAsync(Hoje, 4, 2, "  Dia bom, corri de manhã.  ", ["Trabalho", "#corrida", "trabalho", " vida  boa "]);
        Assert.True(r.Sucesso, r.Mensagem);

        var e = (await d.ObterAsync(Hoje))!;
        Assert.Equal("Dia bom, corri de manhã.", e.Texto);
        Assert.Equal(4, e.Humor);
        Assert.Equal(2, e.Energia);
        Assert.Equal(["trabalho", "corrida", "vida-boa"], e.Tags);

        Assert.True((await d.SalvarAsync(Hoje, 5, null, "Mudei de ideia.", [])).Sucesso);
        await using (var db = await _fabrica.CreateDbContextAsync())
            Assert.Single(await db.Diario.IgnoreQueryFilters().ToListAsync()); // uma por dia: atualiza, não duplica
        Assert.Equal("Mudei de ideia.", (await d.ObterAsync(Hoje))!.Texto);

        Assert.True((await d.ExcluirAsync(Hoje)).Sucesso);
        Assert.Null(await d.ObterAsync(Hoje));
        Assert.False((await d.ExcluirAsync(Hoje)).Sucesso);
    }

    [Fact]
    public async Task Recusa_entrada_vazia_valores_fora_da_faixa_e_dia_futuro()
    {
        var d = Servico(_ana);
        Assert.False((await d.SalvarAsync(Hoje, null, null, "   ", [])).Sucesso);
        Assert.False((await d.SalvarAsync(Hoje, 0, null, "x", [])).Sucesso);
        Assert.False((await d.SalvarAsync(Hoje, 6, null, "x", [])).Sucesso);
        Assert.False((await d.SalvarAsync(Hoje, null, 9, "x", [])).Sucesso);
        Assert.False((await d.SalvarAsync(Hoje.AddDays(5), 3, null, "futuro", [])).Sucesso);
        Assert.False((await d.SalvarAsync(Hoje, 3, null, new string('a', DiarioServico.MaximoLetras + 1), [])).Sucesso);
        Assert.True((await d.SalvarAsync(Hoje, 3, null, "", [])).Sucesso); // só o humor já vale
    }

    [Fact]
    public async Task No_banco_o_texto_e_as_tags_ficam_cifrados()
    {
        await Servico(_ana).SalvarAsync(Hoje, 3, 3, "meu segredo mais secreto", ["intimo"]);
        await using var db = await _fabrica.CreateDbContextAsync();
        var bruto = await db.Diario.IgnoreQueryFilters().SingleAsync();
        Assert.StartsWith("v1:", bruto.Texto);
        Assert.DoesNotContain("segredo", bruto.Texto);
        Assert.DoesNotContain("intimo", bruto.Tags);
        Assert.Equal(3, bruto.Humor); // humor e energia seguem abertos (calendário)
    }

    [Fact]
    public async Task Cada_pessoa_so_ve_o_proprio_diario_ate_dentro_da_mesma_casa()
    {
        await Servico(_ana).SalvarAsync(Hoje, 4, 4, "diário da Ana", ["ana"]);
        await Servico(_ceu).SalvarAsync(Hoje, 2, 2, "diário do Céu", ["ceu"]);
        await Servico(_bia).SalvarAsync(Hoje, 5, 5, "diário da Bia", ["bia"]);

        Assert.Equal("diário da Ana", (await Servico(_ana).ObterAsync(Hoje))!.Texto);
        Assert.Equal("diário do Céu", (await Servico(_ceu).ObterAsync(Hoje))!.Texto);
        Assert.Equal("diário da Bia", (await Servico(_bia).ObterAsync(Hoje))!.Texto);
        foreach (var s in new[] { _ana, _ceu, _bia })
        {
            var lista = await Servico(s).ListarAsync(null, null);
            Assert.Single(lista);
            Assert.Single(await Servico(s).MesAsync(Hoje.Year, Hoje.Month));
        }
        Assert.Equal(["ana"], await Servico(_ana).TagsFrequentesAsync());
        Assert.Empty(await Servico(_ana).ListarAsync("Bia", null));
        Assert.False((await Servico(_ana).ExcluirAsync(Hoje.AddDays(-9))).Sucesso);

        // Excluir o dia da Ana não mexe no do Céu (mesma casa, mesma data).
        Assert.True((await Servico(_ana).ExcluirAsync(Hoje)).Sucesso);
        Assert.NotNull(await Servico(_ceu).ObterAsync(Hoje));
    }

    [Fact]
    public async Task Linha_do_tempo_busca_no_texto_e_filtra_por_tag_mais_recentes_primeiro()
    {
        var d = Servico(_ana);
        await d.SalvarAsync(Hoje.AddDays(-2), 3, null, "Fui à praia com a família", ["familia", "lazer"]);
        await d.SalvarAsync(Hoje.AddDays(-1), 2, null, "Reunião difícil no trabalho", ["trabalho"]);
        await d.SalvarAsync(Hoje, 5, null, "Treino ótimo", ["treino", "lazer"]);

        var todas = await d.ListarAsync(null, null);
        Assert.Equal([Hoje, Hoje.AddDays(-1), Hoje.AddDays(-2)], todas.Select(x => x.Data));
        Assert.Equal("Treino ótimo", todas[0].Previa);

        Assert.Equal([Hoje.AddDays(-1)], (await d.ListarAsync("REUNIAO", null)).Select(x => x.Data)); // sem acento nem maiúscula
        Assert.Equal([Hoje, Hoje.AddDays(-2)], (await d.ListarAsync(null, "#Lazer")).Select(x => x.Data));
        Assert.Equal([Hoje.AddDays(-2)], (await d.ListarAsync("praia", "lazer")).Select(x => x.Data));
        Assert.Single(await d.ListarAsync(null, null, limite: 1));
        Assert.Equal("lazer", (await d.TagsFrequentesAsync()).First());
    }

    [Fact]
    public async Task Sem_a_chave_certa_a_entrada_nao_abre_mas_nao_derruba_a_lista()
    {
        await Servico(_ana).SalvarAsync(Hoje, 3, 3, "texto", ["a"]);
        var outraChave = new DiarioServico(_fabrica, new PessoaFixa(_ana.PessoaId, _ana.FamiliaId), new CifraDiario(RandomNumberGenerator.GetBytes(32)));

        var e = (await outraChave.ObterAsync(Hoje))!;
        Assert.False(e.Legivel);
        Assert.Equal("", e.Texto);
        var lista = await outraChave.ListarAsync(null, null);
        Assert.False(lista.Single().Legivel);
        Assert.Equal(3, lista.Single().Humor);
    }

    [Fact]
    public void Cifra_nao_repete_nonce_prende_o_texto_a_entrada_e_detecta_adulteracao()
    {
        var id = Guid.NewGuid();
        var a = _cifra.Cifrar("olá", id);
        Assert.NotEqual(a, _cifra.Cifrar("olá", id));
        Assert.Equal("olá", _cifra.Decifrar(a, id));
        Assert.Null(_cifra.Decifrar(a, Guid.NewGuid())); // conteúdo de outra entrada
        Assert.Null(_cifra.Decifrar(a[..^4] + "AAAA", id)); // adulterado
        Assert.Null(_cifra.Decifrar("texto aberto", id));
        Assert.Equal("", _cifra.Decifrar(_cifra.Cifrar("", id), id));
    }

    [Fact]
    public void Chave_do_arquivo_e_criada_uma_vez_e_reaproveitada()
    {
        var pasta = Directory.CreateTempSubdirectory("favoa-chave-").FullName;
        try
        {
            var opcoes = new OpcoesFavoa { PastaDados = pasta };
            var primeira = new CifraDiario(opcoes);
            var id = Guid.NewGuid();
            var cifrado = primeira.Cifrar("guardado", id);
            Assert.True(File.Exists(CifraDiario.ArquivoChave(opcoes)));
            Assert.Equal("guardado", new CifraDiario(opcoes).Decifrar(cifrado, id)); // outra instância lê o mesmo arquivo
            Assert.Equal(primeira.ExportarChave(), new CifraDiario(Convert.FromBase64String(primeira.ExportarChave())).ExportarChave());

            File.WriteAllText(CifraDiario.ArquivoChave(opcoes), "lixo");
            Assert.ThrowsAny<Exception>(() => new CifraDiario(opcoes).Cifrar("x", id)); // chave estragada não vira outra em silêncio
        }
        finally { Directory.Delete(pasta, true); }
    }
}
