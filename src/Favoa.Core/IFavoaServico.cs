using Favoa.Core.Captura;
using Favoa.Core.Edicao;
using Favoa.Core.Financas;
using Favoa.Core.Rotina;
using Favoa.Core.Painel;

namespace Favoa.Core;

/// <param name="Criado">O que foi criado ou alterado, para permitir desfazer. Nulo quando não há o que desfazer.</param>
public sealed record ResultadoRegistro(bool Sucesso, string Mensagem, ReferenciaRegistro? Criado = null);

/// <summary>
/// Porta única entre a interface e os dados. A tela nunca fala com o banco diretamente.
/// </summary>
public interface IFavoaServico
{
    Task<ContextoCaptura> ObterContextoAsync(DateOnly hoje, CancellationToken ct = default);
    Task<ResultadoRegistro> RegistrarAsync(Interpretacao interpretacao, CancellationToken ct = default);
    Task<PainelDoDia> ObterPainelAsync(DateOnly hoje, CancellationToken ct = default);
    Task AlternarTarefaAsync(Guid tarefaId, DateOnly hoje, CancellationToken ct = default);
    /// <summary>Marca ou desmarca o hábito em um dia (hoje ou um dia passado).</summary>
    Task AlternarHabitoAsync(Guid habitoId, DateOnly dia, CancellationToken ct = default);

    /// <summary>Reverte um registro recém-criado pela captura.</summary>
    Task<ResultadoRegistro> DesfazerAsync(ReferenciaRegistro referencia, CancellationToken ct = default);

    Task<RegistroEditavel?> ObterParaEdicaoAsync(ReferenciaRegistro referencia, CancellationToken ct = default);
    Task<ResultadoRegistro> SalvarEdicaoAsync(RegistroEditavel registro, CancellationToken ct = default);
    Task<ResultadoRegistro> ExcluirAsync(ReferenciaRegistro referencia, CancellationToken ct = default);

    /// <summary>Lançamentos de um mês agrupados por dia, com totais e ritmo do orçamento.</summary>
    Task<ExtratoMes> ObterExtratoAsync(FiltroExtrato filtro, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Pendentes e concluídas recentes, organizadas para a tela de Tarefas.</summary>
    Task<QuadroTarefas> ObterQuadroTarefasAsync(DateOnly hoje, CancellationToken ct = default);

    /// <summary>Muda só o prazo (nulo = sem prazo).</summary>
    Task<ResultadoRegistro> DefinirPrazoTarefaAsync(Guid tarefaId, DateOnly? prazo, CancellationToken ct = default);

    /// <summary>Hábitos ativos e arquivados com a grade dos últimos dias.</summary>
    Task<QuadroHabitos> ObterQuadroHabitosAsync(DateOnly hoje, CancellationToken ct = default);

    /// <summary>Arquiva (some do painel e da colmeia, mas guarda o histórico) ou reativa.</summary>
    Task<ResultadoRegistro> DefinirHabitoAtivoAsync(Guid habitoId, bool ativo, CancellationToken ct = default);

    /// <summary>Dias de <paramref name="inicio"/> a <paramref name="fim"/> com tarefas, contas previstas, gastos, treino, peso, lembretes e o progresso de cada dia.</summary>
    Task<CalendarioPeriodo> ObterCalendarioAsync(DateOnly inicio, DateOnly fim, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Em quantos dos últimos <paramref name="dias"/> dias a pessoa lançou despesa, água, treino e peso (para os atalhos da Home).</summary>
    Task<UsoRecente> ObterUsoRecenteAsync(DateOnly hoje, int dias = 14, CancellationToken ct = default);

    /// <summary>Resumo dos 7 dias que terminam em <paramref name="fim"/>, comparado com a semana anterior.</summary>
    Task<ResumoSemana> ObterResumoSemanaAsync(DateOnly fim, CancellationToken ct = default);

    /// <summary>Progresso da colmeia em cada dia (nulo antes do primeiro registro e depois de hoje).</summary>
    Task<IReadOnlyList<ProgressoDia?>> ObterProgressoAsync(DateOnly inicio, DateOnly fim, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Tarefa nova com prazo (o calendário cria direto no dia escolhido).</summary>
    Task<ResultadoRegistro> CriarTarefaAsync(string titulo, DateOnly? prazo, CancellationToken ct = default);

    /// <summary>Tela inicial (colmeia ou calendário) e visão do calendário, de cada pessoa.</summary>
    Task<PreferenciasInicio> ObterPreferenciasInicioAsync(CancellationToken ct = default);
    Task SalvarPreferenciasInicioAsync(PreferenciasInicio preferencias, CancellationToken ct = default);
}
