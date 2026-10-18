using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Favoa.Data;

/// <summary>
/// Web Push sem pacotes: criptografia da mensagem (RFC 8291, aes128gcm) e assinatura VAPID (RFC 8292).
/// É o que deixa o Fávoa mandar notificação para o celular pelo serviço de push do navegador (Apple, Google, Mozilla).
/// </summary>
public static class WebPush
{
    private const int TamanhoRegistro = 4096;

    /// <summary>Par de chaves VAPID novo: (pública não comprimida 65 bytes, privada 32 bytes), em base64url.</summary>
    public static (string Publica, string Privada) GerarChavesVapid()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(includePrivateParameters: true);
        return (Base64Url(Publica(p.Q)), Base64Url(p.D!));
    }

    /// <summary>Cifra o conteúdo para um aparelho. <paramref name="efemera"/> e <paramref name="salt"/> só são passados em testes.</summary>
    public static byte[] Criptografar(byte[] conteudo, byte[] publicaAparelho, byte[] segredoAuth, ECDiffieHellman? efemera = null, byte[]? salt = null)
    {
        if (publicaAparelho.Length != 65 || publicaAparelho[0] != 0x04) throw new ArgumentException("Chave do aparelho inválida.");
        if (conteudo.Length > TamanhoRegistro - 17 - 86) throw new ArgumentException("Mensagem grande demais para uma notificação.");

        using var minha = efemera is null ? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256) : null;
        var servidor = efemera ?? minha!;
        var publicaServidor = Publica(servidor.ExportParameters(false).Q);
        salt ??= RandomNumberGenerator.GetBytes(16);

        using var aparelho = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = publicaAparelho[1..33], Y = publicaAparelho[33..65] },
        });
        var segredo = servidor.DeriveRawSecretAgreement(aparelho.PublicKey);

        // IKM = HKDF(auth, ecdh, "WebPush: info\0" || ua_public || as_public, 32)
        var prkChave = HMACSHA256.HashData(segredoAuth, segredo);
        var info = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), publicaAparelho, publicaServidor);
        var ikm = HMACSHA256.HashData(prkChave, Concat(info, [1]));

        var prk = HMACSHA256.HashData(salt, ikm);
        var cek = HMACSHA256.HashData(prk, Concat(Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"), [1]))[..16];
        var nonce = HMACSHA256.HashData(prk, Concat(Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"), [1]))[..12];

        var claro = Concat(conteudo, [2]); // 0x02 = último registro
        var cifrado = new byte[claro.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16)) aes.Encrypt(nonce, claro, cifrado, tag);

        // Cabeçalho: salt(16) | tamanho do registro(4) | tamanho da chave(1) | chave pública do servidor(65)
        var cabecalho = new byte[16 + 4 + 1 + 65];
        salt.CopyTo(cabecalho, 0);
        BinaryPrimitives.WriteUInt32BigEndian(cabecalho.AsSpan(16), TamanhoRegistro);
        cabecalho[20] = 65;
        publicaServidor.CopyTo(cabecalho, 21);
        return Concat(cabecalho, cifrado, tag);
    }

    /// <summary>Cabeçalho Authorization: "vapid t=&lt;jwt&gt;, k=&lt;chave pública&gt;".</summary>
    public static string CabecalhoVapid(string endpoint, string publica, string privada, string contato, DateTimeOffset agora)
    {
        var uri = new Uri(endpoint);
        var publicaBytes = DeBase64Url(publica);
        using var ec = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = DeBase64Url(privada),
            Q = new ECPoint { X = publicaBytes[1..33], Y = publicaBytes[33..65] },
        });

        var cabecalho = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { typ = "JWT", alg = "ES256" }));
        var corpo = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            aud = $"{uri.Scheme}://{uri.Authority}",
            exp = agora.AddHours(12).ToUnixTimeSeconds(),
            sub = contato,
        }));
        var assinado = $"{cabecalho}.{corpo}";
        // Formato IEEE P1363 (r||s, 64 bytes), o que o JWT ES256 espera.
        var assinatura = ec.SignData(Encoding.ASCII.GetBytes(assinado), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"vapid t={assinado}.{Base64Url(assinatura)}, k={publica}";
    }

    public static string Base64Url(byte[] dados) => Convert.ToBase64String(dados).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] DeBase64Url(string texto)
    {
        var s = texto.Trim().Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    private static byte[] Publica(ECPoint q) => Concat([4], q.X!, q.Y!);

    private static byte[] Concat(params byte[][] partes)
    {
        var total = new byte[partes.Sum(p => p.Length)];
        var pos = 0;
        foreach (var p in partes) { p.CopyTo(total, pos); pos += p.Length; }
        return total;
    }
}
