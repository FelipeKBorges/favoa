using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Favoa.Core.Fitness;
using Microsoft.Extensions.Logging;

namespace Favoa.Data;

/// <summary>
/// Busca produtos com marca no Open Food Facts (base aberta, licença ODbL: os dados são citados na tela).
/// Só devolve produtos com kcal e os três macros por 100 g. A busca por texto usa o site brasileiro;
/// números de 8 a 14 dígitos são tratados como código de barras.
/// </summary>
public sealed class OpenFoodFacts(ILogger<OpenFoodFacts> log) : IAlimentosOnline
{
    private const string Campos = "code,product_name,product_name_pt,brands,nutriments,serving_quantity,serving_size";

    private static readonly HttpClient Http = CriarHttp();

    private static HttpClient CriarHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Favoa/1.0 (painel pessoal)");
        return h;
    }

    public async Task<ResultadoBuscaOnline> BuscarAsync(string texto, CancellationToken ct = default)
    {
        texto = texto.Trim();
        if (texto.Length < 2) return new(false, "Escreva pelo menos 2 letras.", []);

        try
        {
            if (texto.All(char.IsDigit) && texto.Length is >= 8 and <= 14)
            {
                using var r = await Http.GetAsync($"https://world.openfoodfacts.org/api/v2/product/{texto}.json?fields={Campos}", ct);
                if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return new(true, null, []);
                r.EnsureSuccessStatusCode();
                using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                return new(true, null, doc.RootElement.TryGetProperty("product", out var p) && Ler(p) is { } produto ? [produto] : []);
            }

            var q = Uri.EscapeDataString(texto);
            using var resposta = await Http.GetAsync(
                $"https://br.openfoodfacts.org/cgi/search.pl?search_terms={q}&search_simple=1&action=process&json=1&page_size=24&lc=pt&fields={Campos}", ct);
            resposta.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return new(true, null, Interpretar(json.RootElement));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            log.LogWarning(e, "Open Food Facts indisponível");
            return new(false, "Não consegui falar com o Open Food Facts agora (o PC está sem internet ou o site está fora). Tente de novo em instantes.", []);
        }
    }

    /// <summary>Resposta da busca → produtos com macros completos, sem repetir código.</summary>
    public static IReadOnlyList<ProdutoOnline> Interpretar(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("products", out var lista) || lista.ValueKind != JsonValueKind.Array) return [];
        return lista.EnumerateArray().Select(Ler).OfType<ProdutoOnline>().DistinctBy(p => p.Codigo).Take(15).ToList();
    }

    public static ProdutoOnline? Ler(JsonElement p)
    {
        var codigo = Texto(p, "code");
        var nome = Texto(p, "product_name_pt") ?? Texto(p, "product_name");
        if (codigo is null || nome is null || !p.TryGetProperty("nutriments", out var n)) return null;

        var kcal = Numero(n, "energy-kcal_100g") ?? (Numero(n, "energy_100g") is { } kj ? kj / 4.184 : null);
        var prot = Numero(n, "proteins_100g");
        var carb = Numero(n, "carbohydrates_100g");
        var gord = Numero(n, "fat_100g");
        if (kcal is null || prot is null || carb is null || gord is null) return null;
        if (kcal is < 0 or > 950 || prot < 0 || carb < 0 || gord < 0) return null; // dado claramente errado

        var marca = Texto(p, "brands")?.Split(',')[0].Trim();
        var porcao = Numero(p, "serving_quantity") is { } g && g is > 0 and < 2000 ? (int?)Math.Round(g) : null;
        return new ProdutoOnline(codigo, nome.Trim(), string.IsNullOrWhiteSpace(marca) ? null : marca,
            kcal.Value, prot.Value, carb.Value, gord.Value, Numero(n, "fiber_100g") ?? 0,
            porcao is null ? null : "porção", porcao);
    }

    private static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;

    private static double? Numero(JsonElement e, string nome)
    {
        if (!e.TryGetProperty(nome, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }
}
