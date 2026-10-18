using Favoa.Core.Financas;

namespace Favoa.Tests;

public sealed class RitmoOrcamentoTests
{
    [Fact]
    public void Sem_orcamento_nao_ha_ritmo() =>
        Assert.Null(RitmoOrcamento.Calcular(new(2026, 10, 10), 2026, 10, 0, 5000));

    [Fact]
    public void Meio_do_mes_no_ritmo_e_valor_por_dia_conta_hoje()
    {
        // Outubro tem 31 dias; no dia 10 o esperado é 10/31 de 3100,00 = 1000,00.
        var r = RitmoOrcamento.Calcular(new(2026, 10, 10), 2026, 10, 310000, 100000)!;

        Assert.Equal(100000, r.EsperadoAteHojeCentavos);
        Assert.Equal(SituacaoOrcamento.NoRitmo, r.Situacao);
        Assert.Equal(22, r.DiasRestantes);            // 10 a 31, incluindo hoje
        Assert.Equal(210000 / 22, r.PorDiaCentavos);
    }

    [Theory]
    [InlineData(50000, SituacaoOrcamento.Folga)]
    [InlineData(120000, SituacaoOrcamento.Acelerado)]
    [InlineData(320000, SituacaoOrcamento.Estourado)]
    public void Situacao_segue_o_gasto(long gasto, SituacaoOrcamento esperada) =>
        Assert.Equal(esperada, RitmoOrcamento.Calcular(new(2026, 10, 10), 2026, 10, 310000, gasto)!.Situacao);

    [Fact]
    public void Mes_passado_nao_tem_valor_por_dia()
    {
        var r = RitmoOrcamento.Calcular(new(2026, 10, 10), 2026, 9, 300000, 250000)!;

        Assert.Equal(0, r.DiasRestantes);
        Assert.Null(r.PorDiaCentavos);
        Assert.Equal(300000, r.EsperadoAteHojeCentavos);
        Assert.Equal(50000, r.RestanteCentavos);
    }

    [Fact]
    public void Estourado_nao_tem_valor_por_dia() =>
        Assert.Null(RitmoOrcamento.Calcular(new(2026, 10, 10), 2026, 10, 100000, 150000)!.PorDiaCentavos);
}
