namespace Favoa.Core.Projetos;

public enum TipoProjeto { Viagem, Trabalho, Casa, Outro }

public enum SituacaoProjeto { Ideia, Planejando, EmAndamento, Pausado, Concluido }

public enum TipoItemProjeto { Checklist, Marco, Nota }

/// <summary>
/// Uma pasta que junta tarefas, gastos e datas (uma viagem, um trabalho, uma reforma).
/// <see cref="PessoaId"/> nulo = da casa (os dois veem); preenchido = de uma pessoa só.
/// Não é <c>IComDono</c> de propósito: projeto da casa não pode ganhar dono ao salvar.
/// </summary>
public sealed class Projeto : Favoa.Core.Pessoas.IDaFamilia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Família dona do registro (tenant). Nulo = catálogo comum a todas.</summary>
    public Guid? FamiliaId { get; set; }
    public string Nome { get; set; } = "";
    public TipoProjeto Tipo { get; set; }
    public SituacaoProjeto Situacao { get; set; } = SituacaoProjeto.Planejando;
    public DateOnly? Inicio { get; set; }
    public DateOnly? Fim { get; set; }
    public long? OrcamentoCentavos { get; set; }
    public string Descricao { get; set; } = "";
    public Guid? PessoaId { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Item de checklist (marca feito), marco (data no calendário) ou nota (texto e links).</summary>
public sealed class ItemProjeto : Favoa.Core.Pessoas.IDaFamilia
{
    /// <summary>Casa (tenant) do registro. O banco filtra e preenche sozinho com a família atual.</summary>
    public Guid? FamiliaId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjetoId { get; set; }
    public TipoItemProjeto Tipo { get; set; }
    public string Texto { get; set; } = "";
    public bool Feito { get; set; }
    public DateOnly? Data { get; set; }
    public TimeOnly? Hora { get; set; }
    public int Ordem { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public static class TiposProjeto
{
    public static string Nome(TipoProjeto t) => t switch
    {
        TipoProjeto.Viagem => "Viagem",
        TipoProjeto.Trabalho => "Trabalho",
        TipoProjeto.Casa => "Casa",
        _ => "Outro",
    };

    public static string Nome(SituacaoProjeto s) => s switch
    {
        SituacaoProjeto.Ideia => "Ideia",
        SituacaoProjeto.Planejando => "Planejando",
        SituacaoProjeto.EmAndamento => "Em andamento",
        SituacaoProjeto.Pausado => "Pausado",
        _ => "Concluído",
    };

    /// <summary>
    /// Quanto o apelido escrito depois do # combina com o nome do projeto ("chile" → "Viagem Chile"):
    /// 3 = nome igual, 2 = uma palavra igual, 1 = começo de palavra, 0 = não combina.
    /// </summary>
    public static int Combina(string nome, string? apelido)
    {
        var alvo = Captura.Texto.Normalizar(apelido).Replace("-", " ").Replace("_", " ").Trim();
        if (alvo.Length == 0) return 0;
        var n = Captura.Texto.Normalizar(nome);
        var palavras = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (n == alvo || n.Replace(" ", "") == alvo.Replace(" ", "")) return 3;
        if (palavras.Contains(alvo)) return 2;
        return palavras.Any(w => w.StartsWith(alvo)) ? 1 : 0;
    }

    public static bool Ativo(SituacaoProjeto s) => s is SituacaoProjeto.Planejando or SituacaoProjeto.EmAndamento;

    /// <summary>Checklist inicial de cada tipo, para não começar do zero.</summary>
    public static IReadOnlyList<string> Modelo(TipoProjeto t) => t switch
    {
        TipoProjeto.Viagem => ["Documentos (RG ou passaporte) em dia", "Passagens compradas", "Hospedagem reservada", "Seguro viagem",
                               "Avisar o banco sobre o cartão", "Mala pronta", "Carregadores e adaptador de tomada"],
        TipoProjeto.Trabalho => ["Escopo combinado", "Valor e prazo aprovados", "Entrega final", "Recebimento confirmado"],
        TipoProjeto.Casa => ["Orçamentos de fornecedores", "Compra dos materiais", "Execução", "Limpeza e conferência final"],
        _ => [],
    };
}

// ---------- Leitura ----------

public sealed record ProjetoResumo(
    Guid Id,
    string Nome,
    TipoProjeto Tipo,
    SituacaoProjeto Situacao,
    DateOnly? Inicio,
    DateOnly? Fim,
    long? OrcamentoCentavos,
    long GastoCentavos,
    long RecebidoCentavos,
    int TarefasFeitas,
    int TarefasTotal,
    int ChecklistFeitos,
    int ChecklistTotal,
    bool DaCasa,
    string? Proximo)
{
    /// <summary>Tarefas e checklist feitos sobre o total (0 a 1). Sem nada a fazer: concluído = 1, senão 0.</summary>
    public double Progresso => TarefasTotal + ChecklistTotal == 0
        ? (Situacao == SituacaoProjeto.Concluido ? 1 : 0)
        : (double)(TarefasFeitas + ChecklistFeitos) / (TarefasTotal + ChecklistTotal);

    public int Percentual => (int)Math.Round(Progresso * 100);

    /// <summary>Dias até começar (viagem) ou até o prazo final; nulo quando não há data por vir.</summary>
    public int? DiasAte(DateOnly hoje) =>
        Inicio is { } i && i > hoje ? i.DayNumber - hoje.DayNumber
        : Fim is { } f && f >= hoje ? f.DayNumber - hoje.DayNumber
        : null;
}

public sealed record TarefaProjeto(Guid Id, string Titulo, DateOnly? Prazo, bool Feita, bool Atrasada);
public sealed record LancamentoProjeto(Guid Id, DateOnly Data, string Categoria, long ValorCentavos, bool Receita);
public sealed record ItemProjetoInfo(Guid Id, TipoItemProjeto Tipo, string Texto, bool Feito, DateOnly? Data, TimeOnly? Hora);

public sealed record ProjetoDetalhe(
    ProjetoResumo Resumo,
    string Descricao,
    Guid? PessoaId,
    IReadOnlyList<TarefaProjeto> Tarefas,
    IReadOnlyList<LancamentoProjeto> Lancamentos,
    IReadOnlyList<ItemProjetoInfo> Itens);

// ---------- Escrita ----------

public sealed record DadosProjeto(
    Guid? Id, string Nome, TipoProjeto Tipo, SituacaoProjeto Situacao, DateOnly? Inicio, DateOnly? Fim,
    long? OrcamentoCentavos, string? Descricao, bool DaCasa);

public sealed record DadosItemProjeto(Guid? Id, Guid ProjetoId, TipoItemProjeto Tipo, string Texto, DateOnly? Data, TimeOnly? Hora);

/// <summary>Projetos da casa e da pessoa atual. Tarefas e lançamentos continuam nos seus serviços; aqui só se ligam.</summary>
public interface IProjetosServico
{
    Task<IReadOnlyList<ProjetoResumo>> ListarAsync(DateOnly hoje, CancellationToken ct = default);
    Task<ProjetoDetalhe?> ObterAsync(Guid id, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Cria (com o checklist do modelo do tipo) ou altera. O <c>Criado.Id</c> do resultado é o projeto.</summary>
    Task<ResultadoRegistro> SalvarAsync(DadosProjeto dados, CancellationToken ct = default);

    /// <summary>Apaga o projeto e os itens dele. Tarefas e lançamentos ficam, só deixam de estar no projeto.</summary>
    Task<ResultadoRegistro> ExcluirAsync(Guid id, CancellationToken ct = default);

    Task<ResultadoRegistro> SalvarItemAsync(DadosItemProjeto dados, CancellationToken ct = default);
    Task AlternarItemAsync(Guid itemId, CancellationToken ct = default);
    Task ExcluirItemAsync(Guid itemId, CancellationToken ct = default);

    Task<ResultadoRegistro> CriarTarefaAsync(Guid projetoId, string titulo, DateOnly? prazo, CancellationToken ct = default);
}
