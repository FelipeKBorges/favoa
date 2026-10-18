namespace Favoa.Core.Painel;

/// <summary>Como a colmeia estava num dia: o geral (centro) e quanto cada área encheu, de 0 a 1.</summary>
public sealed record ProgressoDia(
    DateOnly Data,
    double Geral,
    double Financas,
    double Tarefas,
    double Habitos,
    double Alimentacao,
    double? Treino)
{
    public int Percentual => (int)Math.Round(Geral * 100);

    public double? Area(string chave) => chave switch
    {
        CelulaColmeia.Hoje => Geral,
        CelulaColmeia.Financas => Financas,
        CelulaColmeia.Tarefas => Tarefas,
        CelulaColmeia.Habitos => Habitos,
        CelulaColmeia.Alimentacao => Alimentacao,
        CelulaColmeia.Treino => Treino,
        _ => null,
    };
}

public enum TipoItemDia
{
    Tarefa,
    ContaPrevista,
    Lancamento,
    Habito,
    Treino,
    Peso,
    Lembrete,
    Projeto,
}

/// <summary>Uma linha no dia do calendário. <see cref="Id"/> serve para concluir a tarefa ou marcar o hábito ali mesmo.</summary>
public sealed record ItemDia(TipoItemDia Tipo, string Texto, string? Detalhe = null, bool Feito = false, bool Alerta = false, Guid? Id = null);

public sealed record DiaCalendario(
    DateOnly Data,
    ProgressoDia? Progresso,
    long DespesasCentavos,
    long ReceitasCentavos,
    int AguaMl,
    int Kcal,
    IReadOnlyList<ItemDia> Itens);

public sealed record CalendarioPeriodo(DateOnly Hoje, IReadOnlyList<DiaCalendario> Dias);

public enum TelaInicial { Colmeia, Calendario }

/// <summary>Automática: mês no PC, semana no celular.</summary>
public enum VisaoCalendario { Automatica, Mes, Semana }

public sealed record PreferenciasInicio(TelaInicial Tela, VisaoCalendario Visao)
{
    public static readonly PreferenciasInicio Padrao = new(TelaInicial.Colmeia, VisaoCalendario.Automatica);
}

/// <summary>Os registros brutos de que o histórico precisa. Metas e orçamento são os de hoje (não guardamos o passado delas).</summary>
public sealed record DadosHistorico
{
    public long OrcamentoMensalCentavos { get; init; }
    public IReadOnlyList<(DateOnly Data, long Centavos)> Despesas { get; init; } = [];
    public IReadOnlyList<(DateOnly Criada, DateOnly? Prazo, DateOnly? ConcluidaEm)> Tarefas { get; init; } = [];
    public IReadOnlyList<(Guid Id, DateOnly Criado)> Habitos { get; init; } = [];
    public IReadOnlyList<(Guid HabitoId, DateOnly Data)> RegistrosHabito { get; init; } = [];
    public IReadOnlyDictionary<DateOnly, int> AguaPorDia { get; init; } = new Dictionary<DateOnly, int>();
    public IReadOnlyDictionary<DateOnly, int> KcalPorDia { get; init; } = new Dictionary<DateOnly, int>();
    public IReadOnlyList<DateOnly> Treinos { get; init; } = [];
    public int MetaAguaMl { get; init; } = 2500;
    public int MetaKcal { get; init; }
    /// <summary>Gasto bruto dos treinos de cada dia e quanto dele entra na meta (%).</summary>
    public IReadOnlyDictionary<DateOnly, int> GastoTreinoPorDia { get; init; } = new Dictionary<DateOnly, int>();
    public int CompensacaoTreinoPct { get; init; } = 100;
    public int MetaTreinosSemana { get; init; } = 3;

    /// <summary>Primeiro dia com qualquer registro. Antes dele o histórico fica vazio (nada a medir).</summary>
    public DateOnly? InicioDeUso { get; init; }
}

/// <summary>
/// Refaz a colmeia de cada dia passado a partir dos registros, com a mesma regra da colmeia de hoje
/// (<see cref="CalculadoraColmeia"/>). Nada é guardado: o histórico se corrige sozinho quando um registro é editado.
/// </summary>
public static class HistoricoColmeia
{
    public static IReadOnlyList<ProgressoDia?> Calcular(DadosHistorico dados, DateOnly inicio, DateOnly fim, DateOnly hoje)
    {
        var resultado = new List<ProgressoDia?>();
        var habitosFeitos = dados.RegistrosHabito.GroupBy(r => r.Data).ToDictionary(g => g.Key, g => g.Select(r => r.HabitoId).ToHashSet());
        var treinos = dados.Treinos.Distinct().Order().ToList();

        for (var d = inicio; d <= fim; d = d.AddDays(1))
        {
            if (d > hoje || dados.InicioDeUso is not { } comeco || d < comeco)
            {
                resultado.Add(null);
                continue;
            }
            resultado.Add(Dia(dados, d, habitosFeitos, treinos));
        }
        return resultado;
    }

    public static ResumoDoDia Resumo(DadosHistorico dados, DateOnly d)
    {
        var habitosFeitos = dados.RegistrosHabito.GroupBy(r => r.Data).ToDictionary(g => g.Key, g => g.Select(r => r.HabitoId).ToHashSet());
        return Montar(dados, d, habitosFeitos, dados.Treinos.Distinct().Order().ToList());
    }

    private static ProgressoDia Dia(DadosHistorico dados, DateOnly d, Dictionary<DateOnly, HashSet<Guid>> habitosFeitos, List<DateOnly> treinos)
    {
        var resumo = Montar(dados, d, habitosFeitos, treinos);
        var celulas = CalculadoraColmeia.Calcular(resumo);
        double F(string chave) => celulas.First(c => c.Chave == chave).Preenchimento;
        return new ProgressoDia(d, F(CelulaColmeia.Hoje), F(CelulaColmeia.Financas), F(CelulaColmeia.Tarefas),
            F(CelulaColmeia.Habitos), F(CelulaColmeia.Alimentacao), resumo.TemTreinos ? F(CelulaColmeia.Treino) : null);
    }

    private static ResumoDoDia Montar(DadosHistorico dados, DateOnly d, Dictionary<DateOnly, HashSet<Guid>> habitosFeitos, List<DateOnly> treinos)
    {
        var inicioMes = new DateOnly(d.Year, d.Month, 1);
        var gasto = dados.Despesas.Where(x => x.Data >= inicioMes && x.Data <= d).Sum(x => x.Centavos);

        // Pendente no fim do dia d: existia e não estava concluída até d.
        bool PendenteEm((DateOnly Criada, DateOnly? Prazo, DateOnly? ConcluidaEm) t) =>
            t.Criada <= d && (t.ConcluidaEm is null || t.ConcluidaEm > d);

        var habitos = dados.Habitos.Where(h => h.Criado <= d).Select(h => h.Id).ToList();
        var feitos = habitosFeitos.TryGetValue(d, out var set) ? habitos.Count(set.Contains) : 0;

        var kcal = dados.KcalPorDia.TryGetValue(d, out var k) ? k : 0;
        var semana = treinos.Where(x => x > d.AddDays(-7) && x <= d).ToList();

        return new ResumoDoDia
        {
            OrcamentoMensalCentavos = dados.OrcamentoMensalCentavos,
            DespesasDoMesCentavos = gasto,
            TarefasConcluidasHoje = dados.Tarefas.Count(t => t.ConcluidaEm == d),
            TarefasParaHoje = dados.Tarefas.Count(t => PendenteEm(t) && t.Prazo == d),
            TarefasAtrasadas = dados.Tarefas.Count(t => PendenteEm(t) && t.Prazo < d),
            HabitosAtivos = habitos.Count,
            HabitosFeitosHoje = feitos,
            AguaHojeMl = dados.AguaPorDia.TryGetValue(d, out var a) ? a : 0,
            MetaAguaMl = dados.MetaAguaMl,
            KcalHoje = kcal,
            MetaKcal = dados.MetaKcal <= 0 ? 0 : dados.MetaKcal + (int)Math.Round((dados.GastoTreinoPorDia.TryGetValue(d, out var gt) ? gt : 0) * Math.Clamp(dados.CompensacaoTreinoPct, 0, 100) / 100.0, MidpointRounding.AwayFromZero),
            TemConsumoHoje = dados.KcalPorDia.ContainsKey(d),
            TemTreinos = treinos.Count > 0 && treinos[0] <= d,
            TreinouHoje = semana.Contains(d),
            TreinosUltimos7Dias = semana.Count,
            MetaTreinosSemana = dados.MetaTreinosSemana,
        };
    }

    /// <summary>Dias seguidos (até hoje, ou até ontem se hoje ainda não encheu) com a colmeia em pelo menos <paramref name="minimo"/>.</summary>
    public static int Sequencia(IReadOnlyList<ProgressoDia?> dias, DateOnly hoje, double minimo = 0.7)
    {
        var porData = dias.Where(p => p is not null).ToDictionary(p => p!.Data, p => p!.Geral);
        var d = porData.TryGetValue(hoje, out var h) && h >= minimo ? hoje : hoje.AddDays(-1);
        var n = 0;
        while (porData.TryGetValue(d, out var g) && g >= minimo) { n++; d = d.AddDays(-1); }
        return n;
    }
}
