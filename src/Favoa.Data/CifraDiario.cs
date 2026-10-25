using System.Security.Cryptography;
using System.Text;
using Favoa.Core.Diario;

namespace Favoa.Data;

/// <summary>
/// AES-256-GCM com uma chave deste computador (<c>PastaDados\chaves\diario.key</c>, criada no primeiro uso). Quem copia só o
/// arquivo do banco (backup, OneDrive, JSON) leva texto cifrado. A chave NÃO vai na cópia da nuvem de propósito: guarde a sua
/// (Seus dados baixa um arquivo) ou o diário de um disco perdido não abre. Formato: <c>v1:</c> + base64(nonce + etiqueta + texto).
/// </summary>
public sealed class CifraDiario : ICifraDiario
{
    private const string Prefixo = "v1:";
    private readonly Lazy<byte[]> _chave;

    public CifraDiario(OpcoesFavoa opcoes) => _chave = new(() => LerOuCriarChave(ArquivoChave(opcoes)));

    /// <summary>Para testes e restauração: usa uma chave conhecida.</summary>
    public CifraDiario(byte[] chave)
    {
        if (chave.Length != 32) throw new ArgumentException("A chave precisa ter 32 bytes.", nameof(chave));
        _chave = new(() => chave);
    }

    public static string ArquivoChave(OpcoesFavoa opcoes) => Path.Combine(opcoes.PastaDados, "chaves", "diario.key");

    public string Cifrar(string texto, Guid entradaId)
    {
        var claro = Encoding.UTF8.GetBytes(texto);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cifrado = new byte[claro.Length];
        var etiqueta = new byte[16];
        using var aes = new AesGcm(_chave.Value, 16);
        aes.Encrypt(nonce, claro, cifrado, etiqueta, entradaId.ToByteArray());
        return Prefixo + Convert.ToBase64String([.. nonce, .. etiqueta, .. cifrado]);
    }

    public string? Decifrar(string cifrado, Guid entradaId)
    {
        if (!cifrado.StartsWith(Prefixo, StringComparison.Ordinal)) return null;
        try
        {
            var dados = Convert.FromBase64String(cifrado[Prefixo.Length..]);
            if (dados.Length < 28) return null;
            var claro = new byte[dados.Length - 28];
            using var aes = new AesGcm(_chave.Value, 16);
            aes.Decrypt(dados.AsSpan(0, 12), dados.AsSpan(28), dados.AsSpan(12, 16), claro, entradaId.ToByteArray());
            return Encoding.UTF8.GetString(claro);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }

    /// <summary>A chave em texto (base64), para a pessoa guardar fora do computador.</summary>
    public string ExportarChave() => Convert.ToBase64String(_chave.Value);

    private static byte[] LerOuCriarChave(string arquivo)
    {
        if (File.Exists(arquivo))
        {
            var lida = Convert.FromBase64String(File.ReadAllText(arquivo).Trim());
            if (lida.Length == 32) return lida;
            throw new InvalidOperationException($"A chave do diário em {arquivo} está inválida. Restaure a cópia guardada; não crie outra, ou as entradas atuais deixam de abrir.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        var nova = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(arquivo, Convert.ToBase64String(nova));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(arquivo, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return nova;
    }
}
