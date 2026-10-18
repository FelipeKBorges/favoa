namespace Favoa.Core.Rotina;

/// <summary>Regras do aviso de tarefa com horário: quando avisar, como escrever e o que validar.</summary>
public static class AvisoTarefa
{
    /// <summary>Antecedências oferecidas na tela, em minutos (0 = na hora).</summary>
    public static readonly IReadOnlyList<int> Opcoes = [0, 5, 15, 30, 60, 120, 1440];

    /// <summary>Até quanto depois do horário ainda vale avisar (Fávoa estava desligado, por exemplo).</summary>
    public static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(15);

    public const int MaximoMinutos = 7 * 24 * 60;

    public enum Situacao { Nenhum, Avisar, Perdido }

    public static string Rotulo(int minutos) => minutos switch
    {
        0 => "Na hora",
        < 60 => $"{minutos} min antes",
        _ when minutos % 1440 == 0 => minutos == 1440 ? "1 dia antes" : $"{minutos / 1440} dias antes",
        _ when minutos % 60 == 0 => $"{minutos / 60} h antes",
        _ => $"{minutos / 60} h {minutos % 60} min antes",
    };

    /// <summary>Data e hora da tarefa, quando tem as duas.</summary>
    public static DateTime? Momento(DateOnly? prazo, TimeOnly? hora) =>
        prazo is { } p && hora is { } h ? p.ToDateTime(h) : null;

    /// <summary>Explica o que está errado (ou nulo se está certo).</summary>
    public static string? Validar(DateOnly? prazo, TimeOnly? hora, int? avisoMinutos)
    {
        if (hora is not null && prazo is null) return "Para ter horário, escolha o dia da tarefa.";
        if (avisoMinutos is not null && hora is null) return "Para avisar antes, escolha o horário da tarefa.";
        if (avisoMinutos is < 0 or > MaximoMinutos) return "O aviso pode ser de até 7 dias antes.";
        return null;
    }

    /// <summary>
    /// Avisar agora? Só tarefa pendente, com horário e aviso, que ainda não avisou.
    /// Passou da hora da tarefa (mais a tolerância) sem avisar: perdido, marca e não manda.
    /// </summary>
    public static Situacao Verificar(Tarefa t, DateTime agoraLocal)
    {
        if (t.ConcluidaEm is not null || t.AvisoMinutos is not { } antes || t.AvisoEnviadoEm is not null) return Situacao.Nenhum;
        if (Momento(t.Prazo, t.Hora) is not { } momento) return Situacao.Nenhum;
        if (agoraLocal < momento.AddMinutes(-antes)) return Situacao.Nenhum;
        return agoraLocal > momento + Tolerancia ? Situacao.Perdido : Situacao.Avisar;
    }

    /// <summary>"às 15:00 (em 30 min)", "amanhã às 9:00", "agora".</summary>
    public static string Quando(Tarefa t, DateTime agoraLocal)
    {
        if (Momento(t.Prazo, t.Hora) is not { } m) return "";
        var hora = m.ToString("HH:mm");
        var falta = m - agoraLocal;
        if (falta <= TimeSpan.FromMinutes(1)) return falta < -TimeSpan.FromMinutes(1) ? $"era às {hora}" : $"agora, {hora}";
        var dia = DateOnly.FromDateTime(m).DayNumber - DateOnly.FromDateTime(agoraLocal).DayNumber;
        if (dia == 1) return $"amanhã às {hora}";
        if (dia > 1) return $"{m:dd/MM} às {hora}";
        var min = (int)Math.Round(falta.TotalMinutes);
        var texto = min < 60 ? $"{min} min" : min % 60 == 0 ? $"{min / 60} h" : $"{min / 60} h {min % 60} min";
        return $"às {hora} (em {texto})";
    }
}
