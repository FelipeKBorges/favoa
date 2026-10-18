namespace Favoa.Core.Financas;

/// <summary>O que a tela de Finanças pede: um mês e filtros opcionais.</summary>
public sealed record FiltroExtrato(
    int Ano,
    int Mes,
    TipoLancamento? Tipo = null,
    Guid? CategoriaId = null,
    Guid? ContaId = null,
    string? Busca = null,
    Guid? PessoaId = null)
{
    public DateOnly Inicio => new(Ano, Mes, 1);
    public DateOnly Fim => Inicio.AddMonths(1); // exclusivo

    public bool TemFiltro => Tipo is not null || CategoriaId is not null || ContaId is not null || PessoaId is not null || !string.IsNullOrWhiteSpace(Busca);

    public static FiltroExtrato DoMes(DateOnly dia) => new(dia.Year, dia.Month);
}

public sealed record LinhaExtrato(
    Guid Id,
    TipoLancamento Tipo,
    long ValorCentavos,
    DateOnly Data,
    string Categoria,
    string Conta,
    string Descricao)
{
    /// <summary>Quem lançou (nulo = lançado pelo Fávoa, ex.: recorrência).</summary>
    public Guid? PessoaId { get; init; }

    /// <summary>Positivo para receita, negativo para despesa.</summary>
    public long ValorComSinal => Tipo == TipoLancamento.Receita ? ValorCentavos : -ValorCentavos;
}

public sealed record DiaExtrato(DateOnly Data, IReadOnlyList<LinhaExtrato> Lancamentos)
{
    public long SaldoCentavos => Lancamentos.Sum(l => l.ValorComSinal);
}

public sealed record FatiaCategoria(Guid CategoriaId, string Nome, long Centavos, double Fracao)
{
    /// <summary>Limite mensal da categoria, se houver.</summary>
    public long? OrcamentoCentavos { get; init; }
    public double? FracaoOrcamento => OrcamentoCentavos is > 0 and var o ? (double)Centavos / o : null;
}

public sealed record OpcaoFiltro(Guid Id, string Nome);

public sealed record ExtratoMes(
    FiltroExtrato Filtro,
    long ReceitasCentavos,
    long DespesasCentavos,
    IReadOnlyList<DiaExtrato> Dias,
    IReadOnlyList<FatiaCategoria> DespesasPorCategoria,
    IReadOnlyList<OpcaoFiltro> Categorias,
    IReadOnlyList<OpcaoFiltro> Contas,
    RitmoOrcamento? Ritmo,
    long DespesasMesAnteriorCentavos)
{
    public long SaldoCentavos => ReceitasCentavos - DespesasCentavos;
    public int QuantidadeLancamentos => Dias.Sum(d => d.Lancamentos.Count);
}

public enum SituacaoOrcamento
{
    /// <summary>Gastando menos do que o esperado para esta altura do mês.</summary>
    Folga,
    NoRitmo,
    /// <summary>Acima do esperado para esta altura, mas ainda dentro do orçamento.</summary>
    Acelerado,
    Estourado,
}

/// <summary>
/// Quanto do orçamento mensal já foi e quanto ainda dá para gastar por dia.
/// Para meses passados não há "por dia": só o resultado final.
/// </summary>
public sealed record RitmoOrcamento(
    long OrcamentoCentavos,
    long GastoCentavos,
    long EsperadoAteHojeCentavos,
    int DiasRestantes,
    SituacaoOrcamento Situacao)
{
    public long RestanteCentavos => OrcamentoCentavos - GastoCentavos;
    public double FracaoGasta => OrcamentoCentavos <= 0 ? 0 : (double)GastoCentavos / OrcamentoCentavos;
    public double FracaoEsperada => OrcamentoCentavos <= 0 ? 0 : (double)EsperadoAteHojeCentavos / OrcamentoCentavos;

    /// <summary>Nulo quando o mês já acabou ou quando não sobra nada.</summary>
    public long? PorDiaCentavos => DiasRestantes > 0 && RestanteCentavos > 0 ? RestanteCentavos / DiasRestantes : null;

    /// <summary>Margem de 10% do esperado antes de chamar de "acelerado".</summary>
    private const double Tolerancia = 0.10;

    public static RitmoOrcamento? Calcular(DateOnly hoje, int ano, int mes, long orcamentoCentavos, long gastoCentavos)
    {
        if (orcamentoCentavos <= 0) return null;

        var inicio = new DateOnly(ano, mes, 1);
        var diasNoMes = DateTime.DaysInMonth(ano, mes);
        var fim = inicio.AddDays(diasNoMes - 1);

        int diasDecorridos, diasRestantes;
        if (hoje < inicio) { diasDecorridos = 0; diasRestantes = diasNoMes; }
        else if (hoje > fim) { diasDecorridos = diasNoMes; diasRestantes = 0; }
        else { diasDecorridos = hoje.Day; diasRestantes = diasNoMes - hoje.Day + 1; } // hoje ainda conta

        var esperado = orcamentoCentavos * diasDecorridos / diasNoMes;

        var situacao =
            gastoCentavos > orcamentoCentavos ? SituacaoOrcamento.Estourado
            : gastoCentavos > esperado * (1 + Tolerancia) ? SituacaoOrcamento.Acelerado
            : gastoCentavos < esperado * (1 - Tolerancia) ? SituacaoOrcamento.Folga
            : SituacaoOrcamento.NoRitmo;

        return new RitmoOrcamento(orcamentoCentavos, gastoCentavos, esperado, diasRestantes, situacao);
    }
}

