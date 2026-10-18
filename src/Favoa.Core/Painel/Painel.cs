namespace Favoa.Core.Painel;

/// <summary>Números brutos do dia, montados pela camada de dados.</summary>
public sealed record ResumoDoDia
{
    public long OrcamentoMensalCentavos { get; init; }
    public long DespesasDoMesCentavos { get; init; }
    public long ReceitasDoMesCentavos { get; init; }

    public int TarefasConcluidasHoje { get; init; }
    public int TarefasParaHoje { get; init; }
    public int TarefasAtrasadas { get; init; }
    /// <summary>Nenhuma tarefa existe ainda (nem feita, nem pendente): a célula não conta como cheia.</summary>
    public bool SemTarefasCadastradas { get; init; }

    public int HabitosAtivos { get; init; }
    public int HabitosFeitosHoje { get; init; }
    public int MelhorSequenciaDias { get; init; }

    public int AguaHojeMl { get; init; }
    public int MetaAguaMl { get; init; } = 2500;

    public int KcalHoje { get; init; }
    public int MetaKcal { get; init; }
    public bool TemConsumoHoje { get; init; }

    public bool TemTreinos { get; init; }
    public bool TreinouHoje { get; init; }
    public int TreinosUltimos7Dias { get; init; }
    public int MetaTreinosSemana { get; init; } = 3;

    /// <summary>Projetos em planejamento ou andamento e o progresso médio deles (fora da média do dia: projeto é de longo prazo).</summary>
    public int ProjetosAtivos { get; init; }
    public double ProgressoProjetos { get; init; }
    public string? ProjetoMaisProximo { get; init; }
    public int? DiasProjetoMaisProximo { get; init; }
}

public sealed record TarefaResumo(Guid Id, string Titulo, DateOnly? Prazo, bool Atrasada);
public sealed record HabitoResumo(Guid Id, string Nome);
public sealed record RegistroRecente(string Descricao, string Valor, DateTime Quando, Edicao.ReferenciaRegistro Referencia);

public sealed record PainelDoDia(
    DateOnly Hoje,
    ResumoDoDia Resumo,
    IReadOnlyList<CelulaColmeia> Celulas,
    IReadOnlyList<TarefaResumo> ProximasTarefas,
    IReadOnlyList<HabitoResumo> HabitosPendentes,
    IReadOnlyList<RegistroRecente> Recentes)
{
    public CelulaColmeia Centro => Celulas.First(c => c.Chave == CelulaColmeia.Hoje);
}
