using Favoa.Core.Fitness;
using Favoa.Core.Pessoas;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

/// <summary>Preparados comuns (estimativas) e busca que perdoa: pastel de frango, "pastel de lagosta", "pasteu".</summary>
public sealed class BaseAlimentosTests : IAsyncLifetime
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

    [Fact]
    public async Task Pastel_de_frango_existe_como_estimativa_com_unidade()
    {
        var r = await _fitness.BuscarAlimentosAsync("pastel de frango", 5);
        var pastel = Assert.Single(r, a => a.Nome.StartsWith("Pastel de frango, frito"));
        Assert.Equal("Estimativa", pastel.Fonte);
        Assert.False(pastel.Aproximado);
        Assert.Contains(pastel.Porcoes, p => p.Nome == "Unidade" && p.Gramas == 100 && p.Padrao);
    }

    [Fact]
    public async Task Sabor_que_nao_existe_mostra_parecidos_avisando()
    {
        var r = await _fitness.BuscarAlimentosAsync("pastel de lagosta", 8);
        Assert.NotEmpty(r);
        Assert.All(r, a => Assert.True(a.Aproximado));
        Assert.All(r, a => Assert.Contains("pastel", a.Nome, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Erro_de_digitacao_acha_o_alimento()
    {
        var r = await _fitness.BuscarAlimentosAsync("pastle", 8);
        Assert.NotEmpty(r);
        Assert.Contains(r, a => a.Nome.Contains("Pastel", StringComparison.OrdinalIgnoreCase));
        Assert.All(r, a => Assert.True(a.Aproximado));
    }

    [Fact]
    public async Task Palavra_sem_nenhuma_relacao_nao_inventa_resultado()
    {
        Assert.Empty(await _fitness.BuscarAlimentosAsync("zzqxwv", 8));
    }

    [Theory]
    [InlineData("pastel", "pastel", true)]
    [InlineData("pastle", "pastel", true)]
    [InlineData("pstel", "pastel", true)]
    [InlineData("pastel", "coxinha", false)]
    [InlineData("coxina", "coxinha", true)]
    public void Distancia_de_edicao(string a, string b, bool perto) => Assert.Equal(perto, FitnessServico.PertoDe(a, b));

    [Fact]
    public async Task Estimativas_sao_coerentes_unicas_e_sem_familia()
    {
        await using var db = await _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>().CreateDbContextAsync();
        var est = await db.Alimentos.AsNoTracking().Where(a => a.Fonte == FontesAlimento.Estimativa).ToListAsync();
        Assert.True(est.Count >= 100);
        Assert.Equal(est.Count, est.Select(a => a.CodigoExterno).Distinct().Count());
        Assert.All(est, a =>
        {
            Assert.Null(a.FamiliaId);
            Assert.True(a.GramasPorcao > 0 && !string.IsNullOrEmpty(a.NomePorcao));
            // Calorias batem com os macros (4/4/9) dentro de 25%: pega erro de digitação nos números.
            var calculado = 4 * a.Proteina + 4 * a.Carboidrato + 9 * a.Gordura;
            Assert.InRange(calculado / a.Kcal, 0.75, 1.25);
        });
    }

    [Fact]
    public async Task Preparar_o_banco_de_novo_nao_duplica()
    {
        await _sp.PrepararBancoComCasaAsync();
        await using var db = await _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>().CreateDbContextAsync();
        var est = await db.Alimentos.Where(a => a.Fonte == FontesAlimento.Estimativa).Select(a => a.CodigoExterno).ToListAsync();
        Assert.Equal(est.Count, est.Distinct().Count());
    }
}
