using Favoa.Core.Fitness;
using Favoa.Core.Pessoas;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

/// <summary>Medidas caseiras (Tabela de Medidas Referidas, IBGE POF 2008-2009): "1 ovo", "1 fatia de pão de forma".</summary>
public sealed class PorcoesTests : IAsyncLifetime
{
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private FitnessServico _fitness = null!;

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao)).BuildServiceProvider();
        await _sp.PrepararBancoComCasaAsync();
        _fitness = new FitnessServico(_sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>(), new PessoaFixa(null));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private async Task<AlimentoResumo> Achar(string texto, string contem)
    {
        var r = await _fitness.BuscarAlimentosAsync(texto, 50);
        return r.First(a => a.Nome.Contains(contem, StringComparison.OrdinalIgnoreCase) && a.Porcoes.Count > 0);
    }

    [Fact]
    public async Task Base_do_ibge_vem_com_medidas_caseiras_e_so_entra_uma_vez()
    {
        await using var db = await _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>().CreateDbContextAsync();
        var total = await db.Porcoes.CountAsync();
        Assert.True(total > 6000);

        await InfraestruturaExtensions.CarregarMedidasAsync(db); // de novo: não duplica
        Assert.Equal(total, await db.Porcoes.CountAsync());
    }

    [Fact]
    public async Task Ovo_vem_em_unidade_ja_escolhida_com_o_peso_da_tabela()
    {
        var ovo = await Achar("ovo cozido", "cozido");
        var padrao = Assert.Single(ovo.Porcoes, p => p.Padrao);
        Assert.Equal("Unidade", padrao.Nome);
        Assert.Equal(45, padrao.Gramas); // cozido; o frito pesa 50 g
    }

    [Fact]
    public async Task Pao_frances_da_taco_vem_em_unidade_de_50_g()
    {
        var pao = await Achar("pao trigo frances", "francês");
        var padrao = Assert.Single(pao.Porcoes, p => p.Padrao);
        Assert.Equal(("Unidade", 50d), (padrao.Nome, padrao.Gramas));
        Assert.Equal("TACO", pao.Fonte);
    }

    [Fact]
    public async Task Pao_de_forma_vem_em_fatia()
    {
        var pao = await Achar("pao de forma industrializado", "qualquer marca");
        var padrao = Assert.Single(pao.Porcoes, p => p.Padrao);
        Assert.Equal("Fatia", padrao.Nome);
        Assert.Equal(25, padrao.Gramas);
    }

    [Fact]
    public async Task Arroz_fica_em_gramas_com_colher_e_concha_como_opcao()
    {
        var arroz = await Achar("arroz", "Arroz, tipo 1, cozido");
        Assert.DoesNotContain(arroz.Porcoes, p => p.Padrao);
        Assert.Contains(arroz.Porcoes, p => p.Nome == "Colher de sopa" && p.Gramas == 25);
    }

    [Fact]
    public async Task Porcao_do_cadastro_proprio_vem_primeiro_e_escolhida()
    {
        var r = await _fitness.SalvarAlimentoAsync(new AlimentoEdicao(null, "Barra caseira", 400, 8, 60, 12, "barra", 30));
        Assert.True(r.Sucesso);
        var barra = (await _fitness.BuscarAlimentosAsync("barra caseira", 5)).Single();
        var primeira = barra.Porcoes[0];
        Assert.Equal(("barra", 30d, true), (primeira.Nome, primeira.Gramas, primeira.Padrao));
    }

    [Fact]
    public async Task Medidas_somem_junto_com_o_alimento_que_nao_existe_mais()
    {
        await using var db = await _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>().CreateDbContextAsync();
        Assert.Empty(await db.Porcoes.Where(p => !db.Alimentos.Any(a => a.Id == p.AlimentoId)).ToListAsync());
    }

    private static readonly DateOnly Dia = new(2026, 10, 19);

    [Fact]
    public async Task Lancar_numa_medida_guarda_a_unidade_e_mostra_1_5_fatia()
    {
        var pao = await Achar("pao de forma industrializado", "qualquer marca");
        var r = await _fitness.AdicionarConsumoAsync(Dia, Refeicao.CafeDaManha, pao.Id, 38, "Fatia", 1.5);
        Assert.True(r.Sucesso, r.Mensagem);
        Assert.Contains("1,5 fatias = 38 g", r.Mensagem);

        var item = (await _fitness.ObterDiaDietaAsync(Dia)).Refeicoes.Single(x => x.Refeicao == Refeicao.CafeDaManha).Consumido.Single();
        Assert.Equal(38, item.Gramas);
        Assert.Equal("1,5 fatias", item.Medida);

        await _fitness.AdicionarConsumoAsync(Dia, Refeicao.Almoco, pao.Id, 100, "g", 100);
        Assert.Null((await _fitness.ObterDiaDietaAsync(Dia)).Refeicoes.Single(x => x.Refeicao == Refeicao.Almoco).Consumido.Single().Medida);
    }

    [Fact]
    public async Task Busca_abre_na_ultima_unidade_usada_no_alimento()
    {
        var arroz = await Achar("arroz", "Arroz, tipo 1, cozido");
        Assert.Null(arroz.UltimaUnidade);

        await _fitness.AdicionarConsumoAsync(Dia, Refeicao.Almoco, arroz.Id, 75, "Colher de sopa", 3);
        var depois = await Achar("arroz", "Arroz, tipo 1, cozido");
        Assert.Equal(("Colher de sopa", 3d), (depois.UltimaUnidade, depois.UltimaQuantidade));
    }

    [Fact]
    public async Task Ajustar_medida_do_catalogo_vale_so_para_a_casa_e_remover_volta_ao_ibge()
    {
        var ovo = await Achar("ovo cozido", "cozido");
        var catalogo = (await _fitness.ObterPorcoesAsync(ovo.Id)).Single(p => p.Nome == "Unidade");
        Assert.False(catalogo.DaCasa);

        // Catálogo não se apaga.
        Assert.False((await _fitness.RemoverPorcaoAsync(catalogo.Id)).Sucesso);

        Assert.True((await _fitness.SalvarPorcaoAsync(ovo.Id, "unidade", 60)).Sucesso);
        var ajustada = (await _fitness.ObterPorcoesAsync(ovo.Id)).Single(p => p.Nome == "Unidade");
        Assert.Equal((60d, true, true), (ajustada.Gramas, ajustada.DaCasa, ajustada.Ajustada));
        var padrao = (await Achar("ovo cozido", "cozido")).Porcoes.Single(p => p.Padrao);
        Assert.Equal(60, padrao.Gramas);
        Assert.False(padrao.Estimada && padrao.Gramas == 45);

        Assert.True((await _fitness.RemoverPorcaoAsync(ajustada.Id)).Sucesso);
        Assert.Equal(45, (await Achar("ovo cozido", "cozido")).Porcoes.Single(p => p.Padrao).Gramas);
    }

    [Fact]
    public async Task Medida_nova_da_casa_aparece_na_busca_e_valida_os_gramas()
    {
        var iogurte = await Achar("iogurte natural", "natural");
        Assert.True((await _fitness.SalvarPorcaoAsync(iogurte.Id, "Pote", 170)).Sucesso);
        Assert.Contains((await Achar("iogurte natural", "natural")).Porcoes, p => p is { Nome: "Pote", Gramas: 170, Estimada: false });

        Assert.False((await _fitness.SalvarPorcaoAsync(iogurte.Id, "", 10)).Sucesso);
        Assert.False((await _fitness.SalvarPorcaoAsync(iogurte.Id, "Balde", 0)).Sucesso);
        Assert.False((await _fitness.SalvarPorcaoAsync(iogurte.Id, "Balde", 99999)).Sucesso);
    }

    [Fact]
    public async Task Plano_e_copia_de_refeicao_mantem_a_unidade()
    {
        var pao = await Achar("pao de forma industrializado", "qualquer marca");
        await _fitness.AdicionarItemPlanoAsync(Refeicao.LancheTarde, pao.Id, 50, "Fatia", 2);
        var plano = (await _fitness.ObterDiaDietaAsync(Dia)).Refeicoes.Single(x => x.Refeicao == Refeicao.LancheTarde).Plano.Single();
        Assert.Equal("2 fatias", plano.Medida);

        await _fitness.SeguirPlanoAsync(Dia, Refeicao.LancheTarde);
        await _fitness.CopiarRefeicaoAsync(Dia, Dia.AddDays(1), Refeicao.LancheTarde);
        var copia = (await _fitness.ObterDiaDietaAsync(Dia.AddDays(1))).Refeicoes.Single(x => x.Refeicao == Refeicao.LancheTarde).Consumido.Single();
        Assert.Equal("2 fatias", copia.Medida);
    }

    [Theory]
    [InlineData("fatia", "fatias")]
    [InlineData("colher de sopa", "colheres de sopa")]
    [InlineData("unidade pequena", "unidades pequenas")]
    [InlineData("copo americano", "copos americanos")]
    [InlineData("porção", "porções")]
    [InlineData("colher de arroz/servir", "colheres de arroz/servir")]
    public void Plural_das_medidas(string singular, string plural) => Assert.Equal(plural, FitnessServico.PluralDaMedida(singular));
}
