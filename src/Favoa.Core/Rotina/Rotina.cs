namespace Favoa.Core.Rotina;

public sealed class Tarefa : Favoa.Core.Pessoas.IDaFamilia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Família dona do registro (tenant). Nulo = catálogo comum a todas.</summary>
    public Guid? FamiliaId { get; set; }
    public string Titulo { get; set; } = "";
    public DateOnly? Prazo { get; set; }
    public DateOnly? ConcluidaEm { get; set; }
    /// <summary>Projeto a que a tarefa pertence (viagem, trabalho...). Sem chave estrangeira, como os outros vínculos.</summary>
    public Guid? ProjetoId { get; set; }
    /// <summary>Horário no dia do prazo (opcional). Só vale com prazo.</summary>
    public TimeOnly? Hora { get; set; }
    /// <summary>Avisar no celular quantos minutos antes do horário. Nulo = sem aviso; 0 = na hora.</summary>
    public int? AvisoMinutos { get; set; }
    /// <summary>Quando o aviso saiu (UTC). Volta a nulo quando o dia, o horário ou o aviso mudam.</summary>
    public DateTime? AvisoEnviadoEm { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    /// <summary>Quem cuida da tarefa. Vazio = a casa (todos).</summary>
    public List<TarefaResponsavel> Responsaveis { get; set; } = [];

    public bool EstaAtrasada(DateOnly hoje) => ConcluidaEm is null && Prazo is { } p && p < hoje;
}

/// <summary>Uma pessoa responsável por uma tarefa. Chave (TarefaId, PessoaId).</summary>
public sealed class TarefaResponsavel
{
    public Guid TarefaId { get; set; }
    public Guid PessoaId { get; set; }
}

public sealed class Habito : Favoa.Core.Pessoas.IComDono
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Dono do registro (pessoa da casa).</summary>
    public Guid? PessoaId { get; set; }
    public string Nome { get; set; } = "";
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Um check de hábito em um dia. Único por (HabitoId, Data).</summary>
public sealed class RegistroHabito
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HabitoId { get; set; }
    public DateOnly Data { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Habito? Habito { get; set; }
}

public sealed class RegistroAgua : Favoa.Core.Pessoas.IComDono
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Dono do registro (pessoa da casa).</summary>
    public Guid? PessoaId { get; set; }
    public int Mililitros { get; set; }
    public DateOnly Data { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Configurações simples em chave/valor (orçamento mensal, meta de água...).</summary>
public sealed class Configuracao
{
    public string Chave { get; set; } = "";
    public string Valor { get; set; } = "";

    public const string OrcamentoMensalCentavos = "orcamento_mensal_centavos";
    public const string MetaAguaMl = "meta_agua_ml";
}
