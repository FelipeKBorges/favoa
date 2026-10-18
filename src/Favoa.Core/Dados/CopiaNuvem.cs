namespace Favoa.Core.Dados;

/// <summary>Como está a cópia para uma pasta sincronizada (OneDrive): onde, quando foi a última e se deu erro.</summary>
public sealed record EstadoNuvem(
    string? Pasta,
    string? PastaProjeto,
    string? PastaSugerida,
    string? ProjetoSugerido,
    DateTime? UltimaCopia,
    string? UltimoErro,
    string? Detalhe)
{
    public bool Ativa => !string.IsNullOrWhiteSpace(Pasta);
}

/// <summary>
/// Cópia contínua do Fávoa para uma pasta sincronizada: backups diários do banco, os dados atuais
/// (banco, JSON e CSV) e o código do projeto (git bundle com todo o histórico).
/// </summary>
public interface ICopiaNuvem
{
    Task<EstadoNuvem> ObterAsync(CancellationToken ct = default);

    /// <summary>Liga (ou muda a pasta) e já faz uma cópia completa. Pasta vazia desliga.</summary>
    Task<ResultadoRegistro> ConfigurarAsync(string? pasta, string? pastaProjeto, CancellationToken ct = default);

    /// <summary>Copia o que mudou. <paramref name="forcar"/> copia tudo, mesmo sem mudanças.</summary>
    Task<ResultadoRegistro> CopiarAsync(bool forcar, CancellationToken ct = default);
}
