using Favoa.Core.Captura;

namespace Favoa.Core.Fitness;

public enum Atividade
{
    Nenhuma = 0,
    Forca,
    MuayThai,
    Futebol,
    Corrida,
}

/// <summary>Um pedaço do treino para o cálculo do gasto (venha ele do banco ou da tela).</summary>
public sealed record ItemGasto(Atividade Atividade, int? Minutos, decimal? DistanciaKm, int? Series, Intensidade? Intensidade);

/// <summary>Gasto estimado de um treino. <see cref="PorItem"/> acompanha a ordem dos itens informados (força fica de fora: vai em <see cref="KcalForca"/>).</summary>
public sealed record GastoSessao(int? Kcal, IReadOnlyList<int?> PorItem, int? KcalForca, int MinutosForca, bool ForcaEstimada, bool SemPeso)
{
    public static readonly GastoSessao Vazio = new(null, [], null, 0, false, false);
}

/// <summary>
/// Gasto calorico bruto pelo método dos METs: kcal = MET × peso (kg) × horas.
/// Valores do Compendium of Physical Activities (aproximados); é estimativa, com erro de 20% a 30%.
/// Bruto = inclui o gasto de repouso daquele tempo.
/// </summary>
public static class GastoCalorico
{
    /// <summary>Sem a duração da musculação, cada série conta isto (a série em si mais o descanso).</summary>
    public const int MinutosPorSerie = 3;

    public static Atividade Classificar(string nome, TipoExercicio tipo)
    {
        if (tipo == TipoExercicio.Forca) return Atividade.Forca;
        var n = Texto.Normalizar(nome);
        if (n.Contains("muay") || n.Contains("thai")) return Atividade.MuayThai;
        if (n.Contains("futebol") || n.Contains("pelada")) return Atividade.Futebol;
        if (n.Contains("corrida") || n.StartsWith("corri")) return Atividade.Corrida;
        return Atividade.Nenhuma;
    }

    /// <summary>Muay thai, futebol e a musculação perguntam a intensidade; a corrida usa a velocidade.</summary>
    public static bool PedeIntensidade(Atividade a) => a is Atividade.MuayThai or Atividade.Futebol or Atividade.Forca;

    public static double Met(Atividade a, Intensidade i) => (a, i) switch
    {
        (Atividade.MuayThai, Intensidade.Leve) => 5.5,
        (Atividade.MuayThai, Intensidade.Moderada) => 7.8,
        (Atividade.MuayThai, _) => 10.5,
        (Atividade.Futebol, Intensidade.Leve) => 5.0,
        (Atividade.Futebol, Intensidade.Moderada) => 7.0,
        (Atividade.Futebol, _) => 10.0,
        (Atividade.Forca, Intensidade.Leve) => 3.0,
        (Atividade.Forca, Intensidade.Moderada) => 3.5,
        (Atividade.Forca, _) => 6.0,
        _ => 0,
    };

    private static readonly (double Kmh, double Met)[] PontosCorrida =
        [(6.4, 6.0), (8.0, 8.3), (9.7, 9.8), (10.8, 10.5), (11.3, 11.0), (12.1, 11.5), (12.9, 11.8), (14.5, 12.8), (16.1, 14.5)];

    /// <summary>MET da corrida pela velocidade média (km/h), interpolando a tabela.</summary>
    public static double MetCorrida(double kmh)
    {
        if (kmh <= PontosCorrida[0].Kmh) return PontosCorrida[0].Met;
        for (var i = 1; i < PontosCorrida.Length; i++)
        {
            var (k1, m1) = PontosCorrida[i];
            if (kmh > k1) continue;
            var (k0, m0) = PontosCorrida[i - 1];
            return m0 + (m1 - m0) * (kmh - k0) / (k1 - k0);
        }
        return PontosCorrida[^1].Met;
    }

    public static int Kcal(double met, decimal pesoKg, double minutos) =>
        (int)Math.Round(met * (double)pesoKg * minutos / 60.0, MidpointRounding.AwayFromZero);

    public static string Rotulo(Intensidade i) => i switch { Intensidade.Leve => "leve", Intensidade.Intensa => "intensa", _ => "moderada" };

    /// <summary>Peso da pessoa no dia: o último registro até ali; se só há registros depois, o primeiro; sem registro, nulo.</summary>
    public static decimal? PesoEm(IReadOnlyList<(DateOnly Data, decimal Kg)> pesos, DateOnly dia)
    {
        if (pesos.Count == 0) return null;
        var ate = pesos.Where(p => p.Data <= dia).OrderBy(p => p.Data).LastOrDefault();
        return ate.Kg > 0 ? ate.Kg : pesos.OrderBy(p => p.Data).First().Kg;
    }

    public static GastoSessao Calcular(IReadOnlyList<ItemGasto> itens, int? minutosForca, Intensidade? intensidadeForca, decimal? pesoKg)
    {
        var semPeso = pesoKg is not > 0;
        var porItem = new List<int?>(itens.Count);
        var total = 0; var algum = false;

        foreach (var x in itens)
        {
            int? kcal = null;
            if (!semPeso && x.Atividade != Atividade.Forca) kcal = Linha(x, pesoKg!.Value);
            porItem.Add(kcal);
            if (kcal is { } k) { total += k; algum = true; }
        }

        var series = itens.Where(x => x.Atividade == Atividade.Forca).Sum(x => x.Series ?? 0);
        var temForca = itens.Any(x => x.Atividade == Atividade.Forca) || minutosForca > 0;
        var informado = minutosForca is > 0;
        var minForca = informado ? minutosForca!.Value : series * MinutosPorSerie;
        int? kcalForca = null;
        if (temForca && minForca > 0 && !semPeso)
        {
            kcalForca = Kcal(Met(Atividade.Forca, intensidadeForca ?? Intensidade.Moderada), pesoKg!.Value, minForca);
            total += kcalForca.Value; algum = true;
        }

        return new GastoSessao(algum ? total : null, porItem, kcalForca, temForca ? minForca : 0, temForca && !informado, semPeso);
    }

    private static int? Linha(ItemGasto x, decimal peso)
    {
        if (x.Minutos is not > 0) return null;
        switch (x.Atividade)
        {
            case Atividade.MuayThai or Atividade.Futebol:
                return Kcal(Met(x.Atividade, x.Intensidade ?? Intensidade.Moderada), peso, x.Minutos.Value);
            case Atividade.Corrida when x.DistanciaKm is > 0:
                return Kcal(MetCorrida((double)x.DistanciaKm.Value / (x.Minutos.Value / 60.0)), peso, x.Minutos.Value);
            default:
                return null;
        }
    }
}
