using Favoa.Core.Rotina;

namespace Favoa.Tests;

public sealed class QuadroHabitosTests
{
    private static readonly DateOnly Hoje = new(2026, 10, 10);

    private static IEnumerable<DateOnly> Dias(params int[] atras) => atras.Select(a => Hoje.AddDays(-a));

    [Fact]
    public void Sequencia_atual_conta_ate_ontem_se_hoje_ainda_nao_foi_feito()
    {
        var e = EstatisticaHabito.Calcular(Dias(1, 2, 3), Hoje, 28);
        Assert.Equal(3, e.SequenciaAtual);

        Assert.Equal(4, EstatisticaHabito.Calcular(Dias(0, 1, 2, 3), Hoje, 28).SequenciaAtual);
        Assert.Equal(0, EstatisticaHabito.Calcular(Dias(2, 3), Hoje, 28).SequenciaAtual);
    }

    [Fact]
    public void Melhor_sequencia_e_feitos_no_periodo()
    {
        var e = EstatisticaHabito.Calcular(Dias(0, 1, 10, 11, 12, 13, 14, 40), Hoje, 28);

        Assert.Equal(2, e.SequenciaAtual);
        Assert.Equal(5, e.MelhorSequencia);
        Assert.Equal(7, e.FeitosNoPeriodo); // o de 40 dias atrás fica fora
    }

    [Fact]
    public void Quadro_separa_ativos_e_arquivados_e_monta_os_dias()
    {
        var ler = new Habito { Nome = "Ler" };
        var correr = new Habito { Nome = "Correr", Ativo = false };
        var q = QuadroHabitos.Montar(
            [ler, correr],
            [new RegistroHabito { HabitoId = ler.Id, Data = Hoje }],
            Hoje);

        Assert.Equal(["Ler"], q.Ativos.Select(h => h.Nome));
        Assert.Equal(["Correr"], q.Arquivados.Select(h => h.Nome));
        Assert.Equal(7, q.Semana.Count);
        Assert.Equal(Hoje, q.Semana[^1]);
        Assert.Equal(Hoje.AddDays(-27), q.Historico[0]);
        Assert.Equal(1, q.FeitosHoje);
    }
}
