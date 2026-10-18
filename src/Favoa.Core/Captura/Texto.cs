using System.Globalization;
using System.Text;

namespace Favoa.Core.Captura;

public static class Texto
{
    /// <summary>Minúsculas, sem acentos e com espaços simples — para comparar "Hábito" com "habito".</summary>
    public static string Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "";

        var decomposto = valor.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        var ultimoEspaco = false;

        foreach (var c in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c))
            {
                if (!ultimoEspaco) sb.Append(' ');
                ultimoEspaco = true;
                continue;
            }
            sb.Append(c);
            ultimoEspaco = false;
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>"mercado" → "Mercado".</summary>
    public static string Capitalizar(string valor)
    {
        valor = valor.Trim();
        return valor.Length == 0 ? valor : char.ToUpper(valor[0], new CultureInfo("pt-BR")) + valor[1..];
    }

    /// <summary>
    /// Lê um valor digitado num campo: "32", "32,5", "R$ 1.240,00", "32.50". Nulo se não for um valor positivo.
    /// </summary>
    public static long? LerMoeda(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace("\u00a0", "").Trim();
        if (t.Contains(','))
            t = t.Replace(".", "").Replace(',', '.');
        else if (t.Count(c => c == '.') == 1 && t.Length - t.IndexOf('.') - 1 is 1 or 2)
        { } // "32.50": ponto decimal
        else
            t = t.Replace(".", ""); // "1.240": milhar

        if (!decimal.TryParse(t, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var valor))
            return null;
        var centavos = (long)Math.Round(valor * 100, MidpointRounding.AwayFromZero);
        return centavos > 0 ? centavos : null;
    }

    public static string FormatarMoeda(long centavos) =>
        (centavos / 100m).ToString("C", new CultureInfo("pt-BR"));
}
