namespace Favoa.Core.Rotina;

public sealed record LembreteCadastro(Guid Id, string Titulo, TipoLembrete Tipo, TimeOnly Hora, DiasSemana Dias, bool Ativo)
{
    /// <summary>Próximo aviso (hora local), nulo se desligado.</summary>
    public DateTime? Proximo { get; init; }
    /// <summary>Último envio (hora local) e o resultado ("Enviado para 1 aparelho").</summary>
    public DateTime? UltimoEnvio { get; init; }
    public string? UltimoResultado { get; init; }
}

public sealed record DadosLembrete(Guid? Id, string Titulo, TipoLembrete Tipo, TimeOnly Hora, DiasSemana Dias);

public sealed record AparelhoPush(Guid Id, string Aparelho, DateTime CriadoEm);

public sealed record QuadroLembretes(IReadOnlyList<LembreteCadastro> Lembretes, IReadOnlyList<AparelhoPush> Aparelhos, string ChavePublica)
{
    /// <summary>Avisos de tarefas com horário que ainda vão sair (próximos 14 dias), da pessoa ou da casa.</summary>
    public IReadOnlyList<AvisoTarefaPrevisto> AvisosTarefas { get; init; } = [];
}

/// <summary>Um aviso de tarefa agendado: sai em <paramref name="Quando"/> para <paramref name="Para"/>.</summary>
public sealed record AvisoTarefaPrevisto(Guid TarefaId, string Titulo, DateTime Quando, DateTime HoraTarefa, int AvisoMinutos, string Para);

/// <summary>Inscrição que o navegador devolve ao aceitar notificações.</summary>
public sealed record InscricaoPush(string Endpoint, string P256dh, string Auth, string? Aparelho);

public sealed record ResultadoEnvio(int Enviados, int Falhas, string Mensagem);

public interface ILembretesServico
{
    Task<QuadroLembretes> ObterAsync(CancellationToken ct = default);
    Task<ResultadoRegistro> SalvarAsync(DadosLembrete dados, CancellationToken ct = default);
    Task<ResultadoRegistro> DefinirAtivoAsync(Guid id, bool ativo, CancellationToken ct = default);
    Task<ResultadoRegistro> ExcluirAsync(Guid id, CancellationToken ct = default);

    Task<ResultadoRegistro> InscreverAparelhoAsync(InscricaoPush inscricao, CancellationToken ct = default);
    Task<ResultadoRegistro> RemoverAparelhoAsync(Guid id, CancellationToken ct = default);
    Task<ResultadoRegistro> RemoverAparelhoPorEndpointAsync(string endpoint, CancellationToken ct = default);
    Task<ResultadoEnvio> EnviarTesteAsync(CancellationToken ct = default);

    /// <summary>Envia este lembrete agora, com o conteúdo real (ex.: a lista de tarefas). Não muda o agendamento.</summary>
    Task<ResultadoEnvio> EnviarAgoraAsync(Guid id, CancellationToken ct = default);

    /// <summary>Dispara os lembretes que chegaram na hora. Chamado pelo agendador.</summary>
    Task<int> DispararDevidosAsync(DateTime agoraLocal, CancellationToken ct = default);
}
