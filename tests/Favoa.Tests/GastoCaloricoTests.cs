using Favoa.Core.Captura;
using Favoa.Core.Fitness;

namespace Favoa.Tests;

public class GastoCaloricoTests
{
    private static readonly ContextoCaptura Ctx = new(new DateOnly(2026, 10, 10), ["Outros"], ["Carteira"], []);

    [Fact]
    public void Muay_thai_usa_met_pela_intensidade()
    {
        // 7,8 × 80 kg × 1 h
        Assert.Equal(624, GastoCalorico.Kcal(GastoCalorico.Met(Atividade.MuayThai, Intensidade.Moderada), 80m, 60));
        Assert.Equal(840, GastoCalorico.Kcal(GastoCalorico.Met(Atividade.MuayThai, Intensidade.Intensa), 80m, 60));
    }

    [Fact]
    public void Corrida_interpola_pela_velocidade()
    {
        Assert.Equal(8.3, GastoCalorico.MetCorrida(8.0), 3);
        Assert.InRange(GastoCalorico.MetCorrida(9.0), 8.3, 9.8);
        Assert.Equal(6.0, GastoCalorico.MetCorrida(4.0), 3);
        Assert.Equal(14.5, GastoCalorico.MetCorrida(30), 3);
    }

    [Fact]
    public void Classifica_pelo_nome()
    {
        Assert.Equal(Atividade.MuayThai, GastoCalorico.Classificar("Muay thai", TipoExercicio.Cardio));
        Assert.Equal(Atividade.Futebol, GastoCalorico.Classificar("Futebol", TipoExercicio.Cardio));
        Assert.Equal(Atividade.Corrida, GastoCalorico.Classificar("Corrida", TipoExercicio.Cardio));
        Assert.Equal(Atividade.Forca, GastoCalorico.Classificar("Supino", TipoExercicio.Forca));
        Assert.Equal(Atividade.Nenhuma, GastoCalorico.Classificar("Bike", TipoExercicio.Cardio));
    }

    [Fact]
    public void Peso_do_dia_e_o_ultimo_ate_a_data()
    {
        var pesos = new List<(DateOnly, decimal)> { (new DateOnly(2026, 10, 1), 80m), (new DateOnly(2026, 10, 8), 78m) };
        Assert.Equal(80m, GastoCalorico.PesoEm(pesos, new DateOnly(2026, 10, 5)));
        Assert.Equal(78m, GastoCalorico.PesoEm(pesos, new DateOnly(2026, 10, 9)));
        Assert.Equal(80m, GastoCalorico.PesoEm(pesos, new DateOnly(2026, 9, 1)));
        Assert.Null(GastoCalorico.PesoEm([], new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void Sem_peso_nao_ha_gasto()
    {
        var g = GastoCalorico.Calcular([new(Atividade.MuayThai, 60, null, null, Intensidade.Moderada)], null, null, null);
        Assert.Null(g.Kcal);
        Assert.True(g.SemPeso);
    }

    [Fact]
    public void Musculacao_sem_duracao_estima_pelas_series_e_com_duracao_usa_a_informada()
    {
        var itens = new List<ItemGasto> { new(Atividade.Forca, null, null, 10, null) };
        var estimada = GastoCalorico.Calcular(itens, null, Intensidade.Moderada, 80m);
        Assert.True(estimada.ForcaEstimada);
        Assert.Equal(30, estimada.MinutosForca);
        Assert.Equal(140, estimada.Kcal); // 3,5 × 80 × 0,5 h

        var informada = GastoCalorico.Calcular(itens, 60, Intensidade.Moderada, 80m);
        Assert.False(informada.ForcaEstimada);
        Assert.Equal(280, informada.Kcal);
    }

    [Fact]
    public void Soma_varias_atividades_do_treino()
    {
        var g = GastoCalorico.Calcular([
            new(Atividade.Futebol, 60, null, null, Intensidade.Moderada),
            new(Atividade.Corrida, 30, 5m, null, null)], null, null, 70m);
        Assert.Equal(g.PorItem.Sum(x => x ?? 0), g.Kcal);
        Assert.Equal(490, g.PorItem[0]); // 7,0 × 70 × 1 h
    }

    [Fact]
    public void Meta_do_dia_soma_o_gasto_pela_compensacao()
    {
        var m = new MetasFitness(2000, 150, 200, 60, 4, 50);
        Assert.Equal(2250, m.MetaKcalDoDia(500));
        Assert.Equal(2500, new MetasFitness(2000, 150, 200, 60, 4).MetaKcalDoDia(500));
        Assert.Equal(0, new MetasFitness(0, 0, 0, 0, 0).MetaKcalDoDia(500));
    }

    [Fact]
    public void Frase_de_muay_thai_traz_tempo_e_intensidade()
    {
        var r = InterpretadorFrase.Interpretar("muay thai 60 min intenso", Ctx);
        Assert.Equal(TipoCaptura.Cardio, r.Tipo);
        Assert.Equal("Muay thai", r.Titulo);
        Assert.Equal(60, r.Minutos);
        Assert.Equal(Intensidade.Intensa, r.Intensidade);
    }

    [Fact]
    public void Frase_de_futebol_sem_intensidade_assume_moderada_e_sem_tempo_avisa()
    {
        var r = InterpretadorFrase.Interpretar("joguei futebol 90 min", Ctx);
        Assert.Equal("Futebol", r.Titulo);
        Assert.Equal(Intensidade.Moderada, r.Intensidade);

        var s = InterpretadorFrase.Interpretar("futebol", Ctx);
        Assert.NotNull(s.Aviso);
    }
}
