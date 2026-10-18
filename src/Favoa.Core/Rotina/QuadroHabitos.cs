namespace Favoa.Core.Rotina;

/// <summary>Números de um hábito a partir dos dias em que foi feito.</summary>
public sealed record EstatisticaHabito(int SequenciaAtual, int MelhorSequencia, int FeitosNoPeriodo)
{
    /// <summary>
    /// A sequência atual conta até hoje; se hoje ainda não foi feito, conta até ontem
    /// (o dia ainda não acabou, então a sequência não está perdida).
    /// </summary>
    public static EstatisticaHabito Calcular(IEnumerable<DateOnly> feitos, DateOnly hoje, int diasPeriodo)
    {
        var dias = feitos.Where(d => d <= hoje).ToHashSet();

        var atual = 0;
        var dia = dias.Contains(hoje) ? hoje : hoje.AddDays(-1);
        while (dias.Contains(dia)) { atual++; dia = dia.AddDays(-1); }

        var melhor = 0;
        foreach (var inicio in dias.Where(d => !dias.Contains(d.AddDays(-1))))
        {
            var tamanho = 0;
            for (var d = inicio; dias.Contains(d); d = d.AddDays(1)) tamanho++;
            melhor = Math.Max(melhor, tamanho);
        }

        var desde = hoje.AddDays(-(diasPeriodo - 1));
        return new EstatisticaHabito(atual, melhor, dias.Count(d => d >= desde));
    }
}

public sealed record LinhaHabito(
    Guid Id,
    string Nome,
    bool Ativo,
    IReadOnlySet<DateOnly> Feitos,
    EstatisticaHabito Estatistica)
{
    public bool FeitoEm(DateOnly dia) => Feitos.Contains(dia);
}

/// <summary>Grade dos últimos dias para a tela de Hábitos.</summary>
public sealed record QuadroHabitos(
    DateOnly Hoje,
    IReadOnlyList<DateOnly> Semana,
    IReadOnlyList<DateOnly> Historico,
    IReadOnlyList<LinhaHabito> Ativos,
    IReadOnlyList<LinhaHabito> Arquivados)
{
    /// <summary>Colunas clicáveis (hoje e os 6 dias anteriores).</summary>
    public const int DiasSemana = 7;

    /// <summary>Dias do histórico em miniatura e do "feito X de N".</summary>
    public const int DiasHistorico = 28;

    public int FeitosHoje => Ativos.Count(h => h.FeitoEm(Hoje));

    public static QuadroHabitos Montar(IEnumerable<Habito> habitos, IEnumerable<RegistroHabito> registros, DateOnly hoje)
    {
        var porHabito = registros
            .GroupBy(r => r.HabitoId)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<DateOnly>)g.Select(r => r.Data).ToHashSet());

        var linhas = habitos
            .OrderBy(h => h.Nome)
            .Select(h =>
            {
                var feitos = porHabito.GetValueOrDefault(h.Id) ?? new HashSet<DateOnly>();
                return new LinhaHabito(h.Id, h.Nome, h.Ativo, feitos, EstatisticaHabito.Calcular(feitos, hoje, DiasHistorico));
            })
            .ToList();

        return new QuadroHabitos(
            hoje,
            Enumerable.Range(0, DiasSemana).Select(i => hoje.AddDays(i - DiasSemana + 1)).ToList(),
            Enumerable.Range(0, DiasHistorico).Select(i => hoje.AddDays(i - DiasHistorico + 1)).ToList(),
            linhas.Where(l => l.Ativo).ToList(),
            linhas.Where(l => !l.Ativo).ToList());
    }
}
