using System.Text.Json;
using Favoa.Core.Fitness;
using Favoa.Core.Pessoas;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

public sealed class AlimentosOnlineTests : IAsyncLifetime
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

    private const string Resposta = """
    { "count": 4, "products": [
      { "code": "7891000100103", "product_name": "Leite condensado", "brands": "Moça,Nestlé",
        "nutriments": { "energy-kcal_100g": 325, "proteins_100g": 7.7, "carbohydrates_100g": 55, "fat_100g": 7.7, "fiber_100g": 0 },
        "serving_quantity": 20 },
      { "code": "7890000000001", "product_name_pt": "Biscoito integral", "product_name": "Whole biscuit",
        "nutriments": { "energy_100g": 1883, "proteins_100g": "8", "carbohydrates_100g": 66, "fat_100g": 16 } },
      { "code": "7890000000002", "product_name": "Sem macros", "nutriments": { "energy-kcal_100g": 100 } },
      { "code": "7891000100103", "product_name": "Repetido", "nutriments": { "energy-kcal_100g": 1, "proteins_100g": 1, "carbohydrates_100g": 1, "fat_100g": 1 } }
    ] }
    """;

    [Fact]
    public void Le_so_produtos_com_kcal_e_macros_sem_repetir()
    {
        using var doc = JsonDocument.Parse(Resposta);
        var produtos = OpenFoodFacts.Interpretar(doc.RootElement);
        Assert.Equal(2, produtos.Count);

        var leite = produtos[0];
        Assert.Equal("Leite condensado", leite.Nome);
        Assert.Equal("Moça", leite.Marca);
        Assert.Equal(20, leite.GramasPorcao);

        var biscoito = produtos[1];
        Assert.Equal("Biscoito integral", biscoito.Nome); // nome em português primeiro
        Assert.Equal(450, biscoito.Kcal, 0);               // 1883 kJ → kcal
        Assert.Equal(8, biscoito.Proteina);                // número vindo como texto
        Assert.Null(biscoito.Marca);
    }

    [Fact]
    public async Task Produto_escolhido_vira_alimento_da_base_uma_vez_so()
    {
        using var doc = JsonDocument.Parse(Resposta);
        var leite = OpenFoodFacts.Interpretar(doc.RootElement)[0];

        var a = await _fitness.SalvarProdutoOnlineAsync(leite);
        var b = await _fitness.SalvarProdutoOnlineAsync(leite);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal("Open Food Facts", a.Fonte);
        Assert.Equal("Moça", a.Marca);
        Assert.Equal(20, a.GramasPorcao);

        var achado = Assert.Single(await _fitness.BuscarAlimentosAsync("leite condensado moca"), x => x.Id == a.Id);
        Assert.Equal(325, achado.Kcal);
    }

    [Fact]
    public async Task Base_traz_taco_e_ibge_com_a_fonte_na_busca()
    {
        await using var db = await _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>().CreateDbContextAsync();
        Assert.True(await db.Alimentos.CountAsync(a => a.CodigoTaco != null) > 500);
        Assert.True(await db.Alimentos.CountAsync(a => a.Fonte == FontesAlimento.Ibge) > 1000);

        var frango = await _fitness.BuscarAlimentosAsync("frango frito", 20);
        Assert.Contains(frango, a => a.Fonte == "IBGE");
        var arroz = await _fitness.BuscarAlimentosAsync("arroz tipo 1 cozido", 5);
        Assert.Equal("TACO", arroz[0].Fonte); // TACO primeiro quando os dois têm

        // Rodar a preparação de novo não duplica
        await _sp.PrepararBancoComCasaAsync();
        Assert.Equal(await db.Alimentos.CountAsync(a => a.Fonte == FontesAlimento.Ibge),
            Favoa.Data.InfraestruturaExtensions.LerIbge().Count());
    }
}
