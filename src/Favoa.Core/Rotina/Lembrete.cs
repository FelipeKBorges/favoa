namespace Favoa.Core.Rotina;

[Flags]
public enum DiasSemana
{
    Nenhum = 0,
    Domingo = 1,
    Segunda = 2,
    Terca = 4,
    Quarta = 8,
    Quinta = 16,
    Sexta = 32,
    Sabado = 64,
    Uteis = Segunda | Terca | Quarta | Quinta | Sexta,
    Todos = 127,
}

public enum TipoLembrete
{
    /// <summary>Texto fixo: "Beber água", "Hora do treino".</summary>
    Livre = 0,
    /// <summary>Lista as tarefas de hoje e as atrasadas (ou avisa que não há nenhuma).</summary>
    Tarefas = 1,
}

/// <summary>Aviso no celular (e no navegador do PC) em uma hora e dias da semana.</summary>
public sealed class Lembrete : Favoa.Core.Pessoas.IComDono
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Dono do registro (pessoa da casa).</summary>
    public Guid? PessoaId { get; set; }
    public string Titulo { get; set; } = "";
    public TipoLembrete Tipo { get; set; }
    public TimeOnly Hora { get; set; }
    public DiasSemana Dias { get; set; } = DiasSemana.Todos;
    public bool Ativo { get; set; } = true;

    /// <summary>Último dia em que disparou (ou foi pulado por estar atrasado demais). Evita repetir.</summary>
    public DateOnly? UltimoDisparo { get; set; }

    /// <summary>Quando foi o último envio (UTC) e como foi, para a tela mostrar se o aviso saiu.</summary>
    public DateTime? UltimoEnvioEm { get; set; }
    public string? UltimoResultado { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Um aparelho que aceitou receber notificações (Web Push). Fica só neste computador.</summary>
public sealed class AssinaturaPush : Favoa.Core.Pessoas.IComDono
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Dono do registro (pessoa da casa).</summary>
    public Guid? PessoaId { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string Aparelho { get; set; } = "";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public static class AgendaLembretes
{
    /// <summary>Atraso máximo para ainda avisar (PC desligado na hora, por exemplo). Depois disso, pula o dia.</summary>
    public static readonly TimeSpan Tolerancia = TimeSpan.FromHours(2);

    public static DiasSemana Dia(DayOfWeek d) => (DiasSemana)(1 << (int)d);

    /// <summary>
    /// O que fazer com cada lembrete agora: avisar (na hora ou pouco depois) ou só marcar o dia (atrasado demais).
    /// Os que não devem nada hoje ficam de fora.
    /// </summary>
    public static IEnumerable<(Lembrete Lembrete, bool Avisar)> Devidos(IEnumerable<Lembrete> lembretes, DateTime agora)
    {
        var hoje = DateOnly.FromDateTime(agora);
        var hora = TimeOnly.FromDateTime(agora);
        foreach (var l in lembretes)
        {
            if (!l.Ativo || l.UltimoDisparo == hoje || !l.Dias.HasFlag(Dia(agora.DayOfWeek)) || hora < l.Hora) continue;
            yield return (l, hora.ToTimeSpan() - l.Hora.ToTimeSpan() <= Tolerancia);
        }
    }

    /// <summary>
    /// Marca do dia ao salvar ou religar: horário que já passou hoje só vale a partir de amanhã;
    /// horário ainda por vir dispara hoje (mesmo que o lembrete já tenha disparado antes, em outro horário).
    /// </summary>
    public static DateOnly? MarcaAoSalvar(TimeOnly hora, DateTime agora) =>
        hora <= TimeOnly.FromDateTime(agora) ? DateOnly.FromDateTime(agora) : null;

    /// <summary>Próximo aviso a partir de agora, ou nulo se desligado ou sem dias.</summary>
    public static DateTime? Proximo(Lembrete l, DateTime agora)
    {
        if (!l.Ativo || l.Dias == DiasSemana.Nenhum) return null;
        var hoje = DateOnly.FromDateTime(agora);
        for (var d = 0; d <= 7; d++)
        {
            var dia = hoje.AddDays(d);
            if (!l.Dias.HasFlag(Dia(dia.DayOfWeek))) continue;
            if (d == 0 && (l.UltimoDisparo == hoje || l.Hora < TimeOnly.FromDateTime(agora))) continue;
            return dia.ToDateTime(l.Hora);
        }
        return null;
    }

    /// <summary>"hoje às 22:30", "amanhã às 08:00", "seg, 13/10 às 08:00".</summary>
    public static string Quando(DateTime quando, DateTime agora)
    {
        var dia = DateOnly.FromDateTime(quando);
        var hoje = DateOnly.FromDateTime(agora);
        var nome = dia == hoje ? "hoje" : dia == hoje.AddDays(1) ? "amanhã"
            : quando.ToString("ddd, dd/MM", new System.Globalization.CultureInfo("pt-BR")).Replace(".", "");
        return $"{nome} às {quando:HH:mm}";
    }

    public static string DescreverDias(DiasSemana dias) => dias switch
    {
        DiasSemana.Todos => "Todo dia",
        DiasSemana.Uteis => "Dias úteis",
        DiasSemana.Sabado | DiasSemana.Domingo => "Fim de semana",
        DiasSemana.Nenhum => "Nenhum dia",
        _ => string.Join(", ", Nomes.Where(n => dias.HasFlag(n.Dia)).Select(n => n.Curto)),
    };

    public static readonly (DiasSemana Dia, string Curto)[] Nomes =
    [
        (DiasSemana.Segunda, "Seg"), (DiasSemana.Terca, "Ter"), (DiasSemana.Quarta, "Qua"), (DiasSemana.Quinta, "Qui"),
        (DiasSemana.Sexta, "Sex"), (DiasSemana.Sabado, "Sáb"), (DiasSemana.Domingo, "Dom"),
    ];
}
