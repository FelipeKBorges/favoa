namespace Favoa.Core.Financas;

/// <summary>
/// Lançamento que se repete todo mês no mesmo dia (aluguel, internet, salário).
/// O Fávoa cria o lançamento sozinho quando o dia chega; apagar um lançamento gerado não faz ele voltar.
/// </summary>
public sealed class Recorrencia : Favoa.Core.Pessoas.IDaFamilia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Família dona do registro (tenant). Nulo = catálogo comum a todas.</summary>
    public Guid? FamiliaId { get; set; }
    public TipoLancamento Tipo { get; set; }
    public string Descricao { get; set; } = "";
    public long ValorCentavos { get; set; }
    public Guid CategoriaId { get; set; }
    public Guid ContaId { get; set; }

    /// <summary>1 a 31. Em meses mais curtos, cai no último dia.</summary>
    public int DiaDoMes { get; set; }

    /// <summary>Primeira data que pode ser lançada.</summary>
    public DateOnly Inicio { get; set; }

    /// <summary>Última data já lançada. Só datas depois dela são geradas.</summary>
    public DateOnly? UltimaOcorrencia { get; set; }

    public bool Ativa { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Categoria? Categoria { get; set; }
    public Conta? Conta { get; set; }
}

public static class CalendarioRecorrencia
{
    /// <summary>A data do dia no mês, ajustando para o último dia quando o mês é mais curto (31 → 30/11, 28/02).</summary>
    public static DateOnly DataNoMes(int ano, int mes, int dia) =>
        new(ano, mes, Math.Min(Math.Clamp(dia, 1, 31), DateTime.DaysInMonth(ano, mes)));

    /// <summary>Datas a lançar até hoje: a partir do início e depois da última já lançada.</summary>
    public static IEnumerable<DateOnly> Pendentes(int dia, DateOnly inicio, DateOnly? ultima, DateOnly hoje)
    {
        var desde = ultima is { } u && u >= inicio ? u.AddDays(1) : inicio;
        for (var mes = new DateOnly(desde.Year, desde.Month, 1); mes <= hoje; mes = mes.AddMonths(1))
        {
            var data = DataNoMes(mes.Year, mes.Month, dia);
            if (data >= desde && data <= hoje) yield return data;
        }
    }

    /// <summary>Próxima data depois de hoje (e não antes do início).</summary>
    public static DateOnly Proxima(int dia, DateOnly inicio, DateOnly hoje)
    {
        var base_ = inicio > hoje ? inicio : hoje.AddDays(1);
        var data = DataNoMes(base_.Year, base_.Month, dia);
        if (data < base_)
        {
            var seguinte = base_.AddMonths(1);
            data = DataNoMes(seguinte.Year, seguinte.Month, dia);
        }
        return data;
    }

    /// <summary>Primeira vez em que cai a partir de hoje (inclusive): para criar uma recorrência nova.</summary>
    public static DateOnly PrimeiraAPartirDeHoje(int dia, DateOnly hoje)
    {
        var data = DataNoMes(hoje.Year, hoje.Month, dia);
        if (data >= hoje) return data;
        var seguinte = hoje.AddMonths(1);
        return DataNoMes(seguinte.Year, seguinte.Month, dia);
    }
}
