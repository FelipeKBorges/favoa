namespace Favoa.Core.Painel;

/// <summary>Números dos 7 dias que terminam em <see cref="Fim"/>, comparados com os 7 anteriores.</summary>
public sealed record ResumoSemana(
    DateOnly Inicio, DateOnly Fim,
    long GastoCentavos, long GastoAnteriorCentavos,
    int Treinos, int TreinosAnteriores, int GastoTreinoKcal,
    int HabitosFeitos, int HabitosPossiveis,
    int DiasComDieta, int MediaKcal, int MetaKcal,
    int TarefasConcluidas)
{
    public int HabitosPct => HabitosPossiveis == 0 ? 0 : (int)Math.Round(100.0 * HabitosFeitos / HabitosPossiveis);

    public static ResumoSemana Calcular(DadosHistorico d, DateOnly fim)
    {
        var inicio = fim.AddDays(-6);
        var antIni = inicio.AddDays(-7);
        var antFim = inicio.AddDays(-1);

        long Gasto(DateOnly a, DateOnly b) => d.Despesas.Where(x => x.Data >= a && x.Data <= b).Sum(x => x.Centavos);
        int Treinos(DateOnly a, DateOnly b) => d.Treinos.Count(x => x >= a && x <= b);

        var feitos = 0;
        var possiveis = 0;
        for (var dia = inicio; dia <= fim; dia = dia.AddDays(1))
        {
            var ativos = d.Habitos.Where(h => h.Criado <= dia).Select(h => h.Id).ToHashSet();
            possiveis += ativos.Count;
            feitos += d.RegistrosHabito.Where(r => r.Data == dia && ativos.Contains(r.HabitoId)).Select(r => r.HabitoId).Distinct().Count();
        }

        var kcal = d.KcalPorDia.Where(k => k.Key >= inicio && k.Key <= fim && k.Value > 0).Select(k => k.Value).ToList();
        var gastoTreino = d.GastoTreinoPorDia.Where(k => k.Key >= inicio && k.Key <= fim).Sum(k => k.Value);
        var tarefas = d.Tarefas.Count(t => t.ConcluidaEm >= inicio && t.ConcluidaEm <= fim);

        return new ResumoSemana(inicio, fim, Gasto(inicio, fim), Gasto(antIni, antFim),
            Treinos(inicio, fim), Treinos(antIni, antFim), gastoTreino,
            feitos, possiveis, kcal.Count, kcal.Count == 0 ? 0 : (int)kcal.Average(), d.MetaKcal, tarefas);
    }
}
