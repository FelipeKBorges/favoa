namespace Favoa.Core.Financas;

public enum TipoLancamento
{
    Despesa = 0,
    Receita = 1,
}

public sealed class Conta : Favoa.Core.Pessoas.IDaFamilia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Família dona do registro (tenant). Nulo = catálogo comum a todas.</summary>
    public Guid? FamiliaId { get; set; }
    public string Nome { get; set; } = "";
    public bool Ativa { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public sealed class Categoria : Favoa.Core.Pessoas.IDaFamilia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Família dona do registro (tenant). Nulo = catálogo comum a todas.</summary>
    public Guid? FamiliaId { get; set; }
    public string Nome { get; set; } = "";
    public TipoLancamento Tipo { get; set; }

    /// <summary>Limite de gasto no mês (só despesas). Nulo = sem limite.</summary>
    public long? OrcamentoCentavos { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public sealed class Lancamento : Favoa.Core.Pessoas.IComDono, Favoa.Core.Pessoas.IDaFamilia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Família dona do registro (tenant). Nulo = catálogo comum a todas.</summary>
    public Guid? FamiliaId { get; set; }
    /// <summary>Quem lançou (nulo = lançado pelo próprio Fávoa, ex.: recorrência).</summary>
    public Guid? PessoaId { get; set; }
    public Guid ContaId { get; set; }
    public Guid CategoriaId { get; set; }
    public TipoLancamento Tipo { get; set; }

    /// <summary>Valor sempre positivo, em centavos. O sinal vem de <see cref="Tipo"/>.</summary>
    public long ValorCentavos { get; set; }

    public DateOnly Data { get; set; }
    public string Descricao { get; set; } = "";
    /// <summary>Projeto em que o gasto ou a receita entra (conta no mês e no total do projeto).</summary>
    public Guid? ProjetoId { get; set; }

    /// <summary>Recorrência que gerou este lançamento, se houver.</summary>
    public Guid? RecorrenciaId { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Conta? Conta { get; set; }
    public Categoria? Categoria { get; set; }
}
