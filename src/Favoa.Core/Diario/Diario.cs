using Favoa.Core.Pessoas;

namespace Favoa.Core.Diario;

/// <summary>
/// Entrada do diário de uma pessoa (uma por dia). É sempre pessoal: nem a casa nem o dono veem a de outro.
/// <see cref="Texto"/> e <see cref="Tags"/> ficam cifrados no banco (veja <see cref="ICifraDiario"/>); humor e energia ficam abertos
/// porque alimentam o calendário e os resumos.
/// </summary>
public sealed class EntradaDiario : IComDono, IDaFamilia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? FamiliaId { get; set; }
    public Guid? PessoaId { get; set; }
    public DateOnly Data { get; set; }
    /// <summary>1 (péssimo) a 5 (ótimo); nulo = não marcou.</summary>
    public int? Humor { get; set; }
    /// <summary>1 (sem energia) a 5 (muita energia); nulo = não marcou.</summary>
    public int? Energia { get; set; }
    /// <summary>Texto cifrado (não leia direto: use <see cref="IDiarioServico"/>).</summary>
    public string Texto { get; set; } = "";
    /// <summary>Tags cifradas, separadas por vírgula.</summary>
    public string Tags { get; set; } = "";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Cifra o conteúdo do diário. Cada texto fica preso à própria entrada (trocar o conteúdo entre entradas falha).</summary>
public interface ICifraDiario
{
    string Cifrar(string texto, Guid entradaId);

    /// <summary>Nulo se a chave não confere (chave perdida, ou conteúdo adulterado ou de outra entrada).</summary>
    string? Decifrar(string cifrado, Guid entradaId);
}

public sealed record EntradaDia(DateOnly Data, int? Humor, int? Energia, string Texto, IReadOnlyList<string> Tags, DateTime AtualizadoEm, bool Legivel = true);

public sealed record ResumoEntrada(DateOnly Data, int? Humor, int? Energia, string Previa, IReadOnlyList<string> Tags, bool Legivel = true);

/// <summary>Humor e energia de um dia do mês (para colorir o calendário), sem abrir o texto.</summary>
public sealed record DiaDoDiario(DateOnly Data, int? Humor, int? Energia);

public interface IDiarioServico
{
    Task<EntradaDia?> ObterAsync(DateOnly data, CancellationToken ct = default);

    /// <summary>Cria ou atualiza a entrada do dia. Sem texto, humor, energia nem tags, não há o que guardar.</summary>
    Task<ResultadoRegistro> SalvarAsync(DateOnly data, int? humor, int? energia, string texto, IReadOnlyList<string> tags, CancellationToken ct = default);

    Task<ResultadoRegistro> ExcluirAsync(DateOnly data, CancellationToken ct = default);

    /// <summary>Linha do tempo (mais recentes primeiro), com busca no texto e filtro por tag.</summary>
    Task<IReadOnlyList<ResumoEntrada>> ListarAsync(string? busca, string? tag, int limite = 60, CancellationToken ct = default);

    Task<IReadOnlyList<DiaDoDiario>> MesAsync(int ano, int mes, CancellationToken ct = default);

    /// <summary>Tags mais usadas, para sugerir na hora de escrever.</summary>
    Task<IReadOnlyList<string>> TagsFrequentesAsync(int limite = 12, CancellationToken ct = default);
}

public static class TagsDiario
{
    public const int MaximoTags = 10;
    public const int MaximoLetras = 30;

    /// <summary>Minúsculas, sem #, espaço vira hífen, sem repetidas, no máximo 10.</summary>
    public static IReadOnlyList<string> Normalizar(IEnumerable<string>? tags) =>
        (tags ?? []).SelectMany(t => (t ?? "").Split(',', ';'))
            .Select(t => string.Join('-', t.Trim().TrimStart('#').ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(t => t.Length > 0)
            .Select(t => t.Length > MaximoLetras ? t[..MaximoLetras] : t)
            .Distinct().Take(MaximoTags).ToList();
}
