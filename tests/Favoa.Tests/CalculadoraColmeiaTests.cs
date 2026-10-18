using Favoa.Core.Painel;

namespace Favoa.Tests;

public class CalculadoraColmeiaTests
{
    private static CelulaColmeia Celula(IReadOnlyList<CelulaColmeia> c, string chave) => c.Single(x => x.Chave == chave);

    [Fact]
    public void Tem_sete_celulas_com_hoje_no_centro()
    {
        var celulas = CalculadoraColmeia.Calcular(new ResumoDoDia());

        Assert.Equal(7, celulas.Count);
        Assert.Equal(CelulaColmeia.Hoje, celulas[3].Chave);
    }

    [Fact]
    public void Financas_enche_com_o_que_sobra_do_orcamento()
    {
        var c = CalculadoraColmeia.Calcular(new ResumoDoDia
        {
            OrcamentoMensalCentavos = 300000,
            DespesasDoMesCentavos = 176000,
        });

        var fin = Celula(c, CelulaColmeia.Financas);
        Assert.Equal(41, fin.Percentual);
        Assert.Equal(EstadoCelula.Ativa, fin.Estado);
    }

    [Fact]
    public void Financas_estourada_fica_em_alerta_e_vazia()
    {
        var fin = Celula(CalculadoraColmeia.Calcular(new ResumoDoDia
        {
            OrcamentoMensalCentavos = 100000,
            DespesasDoMesCentavos = 120000,
        }), CelulaColmeia.Financas);

        Assert.Equal(0, fin.Percentual);
        Assert.Equal(EstadoCelula.Alerta, fin.Estado);
    }

    [Fact]
    public void Centro_e_a_media_das_areas_ativas()
    {
        var c = CalculadoraColmeia.Calcular(new ResumoDoDia
        {
            OrcamentoMensalCentavos = 100000, DespesasDoMesCentavos = 0, // 100%
            TarefasConcluidasHoje = 1, TarefasParaHoje = 1,             // 50%
            HabitosAtivos = 4, HabitosFeitosHoje = 4,                    // 100%
            AguaHojeMl = 1250, MetaAguaMl = 2500,                       // 50%
        });

        var centro = Celula(c, CelulaColmeia.Hoje);
        Assert.Equal(75, centro.Percentual);
        Assert.Equal("2/4", centro.Valor);
    }

    [Fact]
    public void Areas_futuras_aparecem_em_breve()
    {
        var c = CalculadoraColmeia.Calcular(new ResumoDoDia());

        Assert.Equal(EstadoCelula.EmBreve, Celula(c, CelulaColmeia.Projetos).Estado);
    }

    [Fact]
    public void Treino_entra_na_media_depois_do_primeiro_treino()
    {
        var sem = CalculadoraColmeia.Calcular(new ResumoDoDia());
        Assert.Equal(EstadoCelula.Alerta, Celula(sem, CelulaColmeia.Treino).Estado);
        Assert.EndsWith("/4", Celula(sem, CelulaColmeia.Hoje).Valor);

        var com = CalculadoraColmeia.Calcular(new ResumoDoDia { TemTreinos = true, TreinosUltimos7Dias = 2, MetaTreinosSemana = 4, TreinouHoje = true });
        var treino = Celula(com, CelulaColmeia.Treino);
        Assert.Equal("2 de 4", treino.Valor);
        Assert.Equal(50, treino.Percentual);
        Assert.EndsWith("/5", Celula(com, CelulaColmeia.Hoje).Valor);
    }

    [Fact]
    public void Alimentacao_mistura_agua_e_calorias_e_alerta_quando_passa()
    {
        var c = Celula(CalculadoraColmeia.Calcular(new ResumoDoDia
        {
            AguaHojeMl = 2500, MetaAguaMl = 2500, TemConsumoHoje = true, KcalHoje = 1000, MetaKcal = 2000,
        }), CelulaColmeia.Alimentacao);
        Assert.Equal(75, c.Percentual);
        Assert.Equal("1.000 kcal", c.Valor);

        var passou = Celula(CalculadoraColmeia.Calcular(new ResumoDoDia
        {
            TemConsumoHoje = true, KcalHoje = 2500, MetaKcal = 2000,
        }), CelulaColmeia.Alimentacao);
        Assert.Equal(EstadoCelula.Alerta, passou.Estado);
    }
}
