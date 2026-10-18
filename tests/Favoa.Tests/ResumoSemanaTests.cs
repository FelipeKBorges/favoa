using Favoa.Core.Painel;

namespace Favoa.Tests;

public class ResumoSemanaTests
{
    [Fact]
    public void Compara_gastos_treinos_e_habitos_das_duas_semanas()
    {
        var fim = new DateOnly(2026, 10, 14);
        var id = Guid.NewGuid();
        var d = new DadosHistorico
        {
            Despesas = [(fim, 10000), (fim.AddDays(-6), 5000), (fim.AddDays(-7), 9000)],
            Treinos = [fim, fim.AddDays(-3), fim.AddDays(-8)],
            Habitos = [(id, fim.AddDays(-30))],
            RegistrosHabito = [(id, fim), (id, fim.AddDays(-1))],
            KcalPorDia = new Dictionary<DateOnly, int> { [fim] = 2000, [fim.AddDays(-1)] = 1800, [fim.AddDays(-2)] = 0 },
            MetaKcal = 2100,
        };
        var r = ResumoSemana.Calcular(d, fim);
        Assert.Equal(15000, r.GastoCentavos);
        Assert.Equal(9000, r.GastoAnteriorCentavos);
        Assert.Equal(2, r.Treinos);
        Assert.Equal(1, r.TreinosAnteriores);
        Assert.Equal(2, r.HabitosFeitos);
        Assert.Equal(7, r.HabitosPossiveis);
        Assert.Equal(2, r.DiasComDieta);
        Assert.Equal(1900, r.MediaKcal);
    }
}
