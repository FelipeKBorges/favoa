using System.Globalization;

namespace Favoa.Web.Components.Cadastros;

/// <summary>Valor para campos de texto: 1800,5 → "1.800,50".</summary>
public static class Dinheiro
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    public static string ParaCampo(long? centavos) => centavos is { } c ? (c / 100m).ToString("N2", PtBr) : "";
}
