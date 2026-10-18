using Favoa.Core.Captura;

namespace Favoa.Core.Fitness;

/// <summary>Uma medida do corpo (cintura, quadril...) em centímetros. Uma por tipo e dia (registrar de novo substitui).</summary>
public sealed class MedidaCorporal : Favoa.Core.Pessoas.IComDono
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? PessoaId { get; set; }
    public DateOnly Data { get; set; }
    public string Tipo { get; set; } = "";
    public decimal Cm { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

public static class TiposMedida
{
    public static readonly IReadOnlyList<string> Todos = ["Cintura", "Quadril", "Peito", "Braço", "Coxa", "Panturrilha", "Pescoço"];

    /// <summary>Devolve o nome canônico do tipo (ignora maiúsculas e acentos) ou null.</summary>
    public static string? Canonico(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var alvo = Texto.Normalizar(texto);
        return Todos.FirstOrDefault(t => Texto.Normalizar(t) == alvo);
    }
}

public sealed record ResultadoImc(decimal Valor, string Faixa, string Dica);

/// <summary>Cálculos de corpo. IMC = peso (kg) / altura (m)². Faixas da OMS para adultos.</summary>
public static class CalculoCorpo
{
    public const int AlturaMinCm = 100, AlturaMaxCm = 250;

    public static ResultadoImc? Imc(decimal? kg, int? alturaCm)
    {
        if (kg is not { } p || alturaCm is not { } a || a < AlturaMinCm || a > AlturaMaxCm || p <= 0) return null;
        var m = a / 100m;
        var valor = Math.Round(p / (m * m), 1);
        var (faixa, dica) = valor switch
        {
            < 18.5m => ("Abaixo do peso", "Abaixo de 18,5."),
            < 25m => ("Peso normal", "Entre 18,5 e 24,9."),
            < 30m => ("Sobrepeso", "Entre 25 e 29,9."),
            < 35m => ("Obesidade grau I", "Entre 30 e 34,9."),
            < 40m => ("Obesidade grau II", "Entre 35 e 39,9."),
            _ => ("Obesidade grau III", "40 ou mais."),
        };
        return new ResultadoImc(valor, faixa, dica);
    }

    /// <summary>Faixa de peso para IMC de 18,5 a 24,9 na altura informada.</summary>
    public static (decimal Min, decimal Max)? PesoSaudavel(int? alturaCm)
    {
        if (alturaCm is not { } a || a < AlturaMinCm || a > AlturaMaxCm) return null;
        var m = a / 100m;
        return (Math.Round(18.5m * m * m, 1), Math.Round(24.9m * m * m, 1));
    }
}

public sealed record PontoMedida(DateOnly Data, decimal Cm);
public sealed record MedidaAtual(string Tipo, decimal Cm, DateOnly Data, decimal? Variacao, IReadOnlyList<PontoMedida> Pontos);
public sealed record PainelMedidas(int? AlturaCm, HistoricoPeso Pesos, IReadOnlyList<MedidaAtual> Medidas)
{
    public ResultadoImc? Imc => CalculoCorpo.Imc(Pesos.Atual?.Kg, AlturaCm);
}
