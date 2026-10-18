using Favoa.Core.Financas;

namespace Favoa.Core.Edicao;

public enum TipoReferencia
{
    Lancamento,
    Agua,
    RegistroHabito,
    Tarefa,
    Habito,
    Conta,
    Categoria,
    Orcamento,
    ExercicioRealizado,
    SessaoTreino,
    Exercicio,
    Consumo,
    Peso,
    Recorrencia,
    Projeto,
}

/// <summary>
/// Aponta para um registro específico. <paramref name="ValorAnterior"/> guarda o que existia antes,
/// quando desfazer significa restaurar em vez de apagar (ex.: orçamento).
/// <paramref name="CriadosJunto"/> lista o que a captura criou no mesmo passo (ex.: conta nova),
/// para o desfazer limpar também.
/// </summary>
public sealed record ReferenciaRegistro(
    TipoReferencia Tipo,
    Guid Id,
    string? ValorAnterior = null,
    IReadOnlyList<ReferenciaRegistro>? CriadosJunto = null);

/// <summary>Os campos que a tela de edição mostra. Cada tipo usa só os que fazem sentido.</summary>
public sealed record RegistroEditavel(ReferenciaRegistro Referencia)
{
    // Lançamento
    public TipoLancamento TipoLancamento { get; init; }
    public long ValorCentavos { get; init; }
    public string Categoria { get; init; } = "";
    public string Conta { get; init; } = "";
    public string Descricao { get; init; } = "";
    public IReadOnlyList<string> CategoriasDisponiveis { get; init; } = [];
    public IReadOnlyList<string> CategoriasDespesa { get; init; } = [];
    public IReadOnlyList<string> CategoriasReceita { get; init; } = [];
    public IReadOnlyList<string> ContasDisponiveis { get; init; } = [];

    // Comum
    public DateOnly Data { get; init; }

    // Lançamento e tarefa: projeto (opcional)
    public Guid? ProjetoId { get; init; }
    public IReadOnlyList<ProjetoOpcao> ProjetosDisponiveis { get; init; } = [];

    // Água
    public int Mililitros { get; init; }

    // Tarefa
    public string Titulo { get; init; } = "";
    public DateOnly? Prazo { get; init; }
    public bool Concluida { get; init; }
    public TimeOnly? Hora { get; init; }
    public int? AvisoMinutos { get; init; }
    /// <summary>Ids dos responsáveis. Vazio = a casa.</summary>
    public IReadOnlyList<Guid> Responsaveis { get; init; } = [];
    public IReadOnlyList<Pessoas.PessoaOpcao> PessoasDisponiveis { get; init; } = [];

    // Hábito (registro do dia)
    public string NomeHabito { get; init; } = "";

    public bool PodeEditar => Referencia.Tipo is TipoReferencia.Lancamento or TipoReferencia.Agua or TipoReferencia.Tarefa;

    public string TituloTela => Referencia.Tipo switch
    {
        TipoReferencia.Lancamento => TipoLancamento == TipoLancamento.Despesa ? "Editar despesa" : "Editar receita",
        TipoReferencia.Agua => "Editar água",
        TipoReferencia.Tarefa => "Editar tarefa",
        TipoReferencia.RegistroHabito => $"Hábito: {NomeHabito}",
        TipoReferencia.ExercicioRealizado => "Exercício",
        TipoReferencia.Consumo => "Refeição",
        TipoReferencia.Peso => "Peso",
        _ => "Editar registro",
    };
}

public sealed record ProjetoOpcao(Guid Id, string Nome);
