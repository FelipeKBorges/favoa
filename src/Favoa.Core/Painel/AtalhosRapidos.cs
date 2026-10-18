namespace Favoa.Core.Painel;

/// <summary>Em quantos dos últimos dias a pessoa fez cada registro (despesa lançada por ela, água, treino, peso).</summary>
public sealed record UsoRecente(int DiasDespesa, int DiasAgua, int DiasTreino, int DiasPeso);

/// <summary>Escolhe os 3 atalhos da Home do celular: os registros que a pessoa mais faz, sem inventar o que ela não usa.</summary>
public static class AtalhosRapidos
{
    public const string Despesa = "despesa";
    public const string Agua = "agua";
    public const string Treino = "treino";
    public const string Peso = "peso";

    // Ordem de desempate (e de quem nunca usou nada): despesa, água, treino, peso.
    private static readonly string[] Padrao = [Despesa, Agua, Treino, Peso];

    public static IReadOnlyList<string> Escolher(UsoRecente uso)
    {
        var dias = new Dictionary<string, int>
        {
            [Despesa] = uso.DiasDespesa, [Agua] = uso.DiasAgua, [Treino] = uso.DiasTreino, [Peso] = uso.DiasPeso,
        };
        return Padrao.OrderByDescending(k => dias[k]).ThenBy(k => Array.IndexOf(Padrao, k)).Take(3).ToList();
    }
}
