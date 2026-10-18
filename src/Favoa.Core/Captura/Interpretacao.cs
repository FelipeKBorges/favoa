using Projetos_ = Favoa.Core.Projetos;
namespace Favoa.Core.Captura;

public enum TipoCaptura
{
    Desconhecido,
    Despesa,
    Receita,
    Agua,
    Habito,
    Tarefa,
    NovoHabito,
    NovaConta,
    Orcamento,
    Treino,
    Cardio,
    Peso,
    Refeicao,
}

/// <summary>
/// O que o Fávoa entendeu de uma frase. É uma proposta: a pessoa vê, corrige e confirma.
/// </summary>
public sealed record Interpretacao(TipoCaptura Tipo, string TextoOriginal)
{
    public long? ValorCentavos { get; init; }
    public string? Categoria { get; init; }
    public string? Conta { get; init; }
    public DateOnly Data { get; init; }

    /// <summary>O que sobrou da frase depois de tirar valor, conta, categoria e data. Nulo quando não sobra nada útil.</summary>
    public string? Descricao { get; init; }

    /// <summary>Título da tarefa, nome do hábito ou da conta.</summary>
    public string? Titulo { get; init; }

    public int? Mililitros { get; init; }
    public DateOnly? Prazo { get; init; }

    // Fitness
    public int? Series { get; init; }
    public int? Repeticoes { get; init; }
    public decimal? CargaKg { get; init; }
    public int? Minutos { get; init; }
    public decimal? DistanciaKm { get; init; }
    /// <summary>Esporte (muay thai, futebol): leve, moderada ou intensa.</summary>
    public Fitness.Intensidade? Intensidade { get; init; }
    public decimal? PesoKg { get; init; }
    /// <summary>Quantidade em gramas (ou ml) quando dita; senão <see cref="Unidades"/> com a porção do alimento.</summary>
    public int? Gramas { get; init; }
    public decimal? Unidades { get; init; }
    public Fitness.Refeicao? Refeicao { get; init; }

    /// <summary>Projeto escrito na frase depois do # ("#chile"). O serviço acha o projeto pelo nome.</summary>
    public string? Projeto { get; init; }

    /// <summary>Projeto escolhido numa lista (formulário). Vale mais que <see cref="Projeto"/>.</summary>
    public Guid? ProjetoId { get; init; }

    // Tarefa: horário, aviso e responsáveis ("às 15h", "avisar 30 min antes", "@eva")
    public TimeOnly? Hora { get; init; }
    public int? AvisoMinutos { get; init; }
    /// <summary>Ids das pessoas responsáveis. Vazio = a casa.</summary>
    public IReadOnlyList<Guid> Responsaveis { get; init; } = [];

    /// <summary>Lançamento que se repete todo mês no mesmo dia (cria uma recorrência junto).</summary>
    public bool RepetirTodoMes { get; init; }

    /// <summary>Quando não dá para salvar, explica o porquê em uma frase.</summary>
    public string? Aviso { get; init; }

    public bool PodeSalvar => Aviso is null && Tipo is not TipoCaptura.Desconhecido;

    public string Modulo => Tipo switch
    {
        TipoCaptura.Despesa => "Finanças · Despesa",
        TipoCaptura.Receita => "Finanças · Receita",
        TipoCaptura.Orcamento => "Finanças · Orçamento",
        TipoCaptura.NovaConta => "Finanças · Nova conta",
        TipoCaptura.Agua => "Alimentação · Água",
        TipoCaptura.Habito => "Hábitos",
        TipoCaptura.NovoHabito => "Hábitos · Novo",
        TipoCaptura.Tarefa => "Tarefas",
        TipoCaptura.Treino => "Treino · Força",
        TipoCaptura.Cardio => "Treino · Cardio",
        TipoCaptura.Peso => "Corpo · Peso",
        TipoCaptura.Refeicao => "Dieta · Refeição",
        _ => "Não entendi ainda",
    };

    public static Interpretacao Vazia(DateOnly hoje) => new(TipoCaptura.Desconhecido, "") { Data = hoje };
}

/// <summary>O que já existe no banco e ajuda a interpretar a frase.</summary>
public sealed record ContextoCaptura(
    DateOnly Hoje,
    IReadOnlyList<string> Categorias,
    IReadOnlyList<string> Contas,
    IReadOnlyList<string> Habitos)
{
    /// <summary>Categorias cadastradas por tipo, das mais usadas para as menos. Lançamento só entra nelas.</summary>
    public IReadOnlyList<string> CategoriasDespesa { get; init; } = [];
    public IReadOnlyList<string> CategoriasReceita { get; init; } = [];

    /// <summary>Nomes dos projetos que a pessoa vê, para a prévia do "#projeto".</summary>
    public IReadOnlyList<string> Projetos { get; init; } = [];

    /// <summary>Pessoas da casa, para o "@nome" das tarefas.</summary>
    public IReadOnlyList<Pessoas.PessoaOpcao> Pessoas { get; init; } = [];

    /// <summary>A pessoa cujo nome começa com o apelido (ignorando maiúsculas e acentos).</summary>
    public Pessoas.PessoaOpcao? PessoaPorApelido(string apelido)
    {
        var a = Texto.Normalizar(apelido);
        if (a.Length == 0) return null;
        return Pessoas.FirstOrDefault(p => Texto.Normalizar(p.Nome) == a)
            ?? Pessoas.FirstOrDefault(p => Texto.Normalizar(p.Nome).StartsWith(a));
    }

    public string? ProjetoPorApelido(string? apelido) =>
        Projetos.Select(p => (Nome: p, Nota: Projetos_.TiposProjeto.Combina(p, apelido))).Where(x => x.Nota > 0)
            .OrderByDescending(x => x.Nota).Select(x => x.Nome).FirstOrDefault();

    public IReadOnlyList<string> CategoriasDo(TipoCaptura tipo) => tipo == TipoCaptura.Receita ? CategoriasReceita : CategoriasDespesa;

    /// <summary>A categoria cadastrada com esse nome (ignorando maiúsculas e acentos), ou nulo.</summary>
    public string? CategoriaCadastrada(TipoCaptura tipo, string? nome) =>
        CategoriasDo(tipo).FirstOrDefault(c => Texto.Normalizar(c) == Texto.Normalizar(nome));

    public static ContextoCaptura Vazio(DateOnly hoje) => new(hoje, [], [], []);
}
