using Favoa.Core.Fitness;
using Favoa.Core.Pessoas;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Favoa.Tests;

public class ImcTests
{
    [Theory]
    [InlineData(50, 170, 17.3, "Abaixo do peso")]
    [InlineData(70, 175, 22.9, "Peso normal")]
    [InlineData(85, 175, 27.8, "Sobrepeso")]
    [InlineData(95, 175, 31.0, "Obesidade grau I")]
    [InlineData(110, 175, 35.9, "Obesidade grau II")]
    [InlineData(140, 175, 45.7, "Obesidade grau III")]
    public void Imc_classifica_pela_faixa_da_OMS(double kg, int cm, double esperado, string faixa)
    {
        var r = CalculoCorpo.Imc((decimal)kg, cm);
        Assert.NotNull(r);
        Assert.Equal((decimal)esperado, r!.Valor);
        Assert.Equal(faixa, r.Faixa);
    }

    [Fact]
    public void Sem_altura_ou_peso_nao_calcula()
    {
        Assert.Null(CalculoCorpo.Imc(null, 170));
        Assert.Null(CalculoCorpo.Imc(70, null));
        Assert.Null(CalculoCorpo.Imc(70, 50));
    }

    [Fact]
    public void Peso_saudavel_para_1m75()
    {
        var f = CalculoCorpo.PesoSaudavel(175);
        Assert.Equal(56.7m, f!.Value.Min);
        Assert.Equal(76.3m, f.Value.Max);
    }

    [Fact]
    public void Tipo_de_medida_ignora_acento_e_caixa()
    {
        Assert.Equal("Braço", TiposMedida.Canonico("braco"));
        Assert.Null(TiposMedida.Canonico("orelha"));
    }
}

public sealed class MedidasServicoTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = new(2026, 10, 17);
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private IDbContextFactory<FavoaDbContext> _fabrica = null!;
    private Guid _a, _b;

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao)).BuildServiceProvider();
        await _sp.PrepararBancoComCasaAsync();
        _fabrica = _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        var acesso = new AcessoServico(_fabrica);
        await acesso.SalvarPessoaAsync(await CasaDeTeste.FamiliaIdAsync(_fabrica), null, "Eva", "#B9A3E3");
        var pessoas = (await acesso.ObterEstadoAsync(null)).Pessoas;
        _a = pessoas.Single(p => p.Nome == "Davi").Id;
        _b = pessoas.Single(p => p.Nome == "Eva").Id;
    }

    public async Task DisposeAsync() { await _sp.DisposeAsync(); await _conexao.DisposeAsync(); }

    private FitnessServico Servico(Guid pessoa) => new(_fabrica, new PessoaFixa(pessoa));

    private sealed class PessoaFixa(Guid id) : IPessoaAtual { public Guid? Id { get; } = id; }

    [Fact]
    public async Task Altura_peso_e_medidas_sao_de_cada_pessoa()
    {
        var f = Servico(_a); var g = Servico(_b);
        Assert.True((await f.SalvarAlturaAsync(180)).Sucesso);
        Assert.True((await g.SalvarAlturaAsync(165)).Sucesso);
        await f.RegistrarPesoAsync(Hoje, 81m);
        await g.RegistrarPesoAsync(Hoje, 60m);
        await f.RegistrarMedidaAsync(Hoje, "cintura", 88.5m);
        await g.RegistrarMedidaAsync(Hoje, "cintura", 70m);

        var pf = await f.ObterPainelMedidasAsync(Hoje);
        var pg = await g.ObterPainelMedidasAsync(Hoje);
        Assert.Equal(180, pf.AlturaCm);
        Assert.Equal(165, pg.AlturaCm);
        Assert.Equal(25m, pf.Imc!.Valor);
        Assert.Equal(22.0m, pg.Imc!.Valor);
        Assert.Equal(88.5m, pf.Medidas.Single().Cm);
        Assert.Equal(70m, pg.Medidas.Single().Cm);
    }

    [Fact]
    public async Task Medida_do_mesmo_dia_substitui_e_calcula_variacao()
    {
        var s = Servico(_a);
        await s.RegistrarMedidaAsync(Hoje.AddDays(-30), "Quadril", 100m);
        await s.RegistrarMedidaAsync(Hoje, "Quadril", 99m);
        await s.RegistrarMedidaAsync(Hoje, "Quadril", 98m);
        var m = (await s.ObterPainelMedidasAsync(Hoje)).Medidas.Single();
        Assert.Equal(98m, m.Cm);
        Assert.Equal(-2m, m.Variacao);
        Assert.Equal(2, m.Pontos.Count);
    }

    [Fact]
    public async Task Valores_absurdos_sao_recusados()
    {
        var s = Servico(_a);
        Assert.False((await s.SalvarAlturaAsync(20)).Sucesso);
        Assert.False((await s.RegistrarMedidaAsync(Hoje, "cintura", 1m)).Sucesso);
        Assert.False((await s.RegistrarMedidaAsync(Hoje, "orelha", 30m)).Sucesso);
    }
}
