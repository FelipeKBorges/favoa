using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Favoa.Core.Pessoas;

namespace Favoa.Core.Acesso;

/// <summary>A casa (tenant): quem cria a conta é o dono e convida os outros, como numa assinatura de família.</summary>
public sealed class Familia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Login de uma pessoa: e-mail e senha. Cada usuário pertence a uma família e abre o Fávoa no perfil (<see cref="Pessoa"/>) dele.</summary>
public sealed class Usuario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FamiliaId { get; set; }
    public Guid PessoaId { get; set; }
    public string Email { get; set; } = "";
    /// <summary>E-mail em minúsculas, sem espaços: é o que vale para achar e para não repetir.</summary>
    public string EmailNormalizado { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    /// <summary>Quem criou a conta da família: convida pessoas e gerencia o acesso.</summary>
    public bool Dono { get; set; }
    /// <summary>Sobe ao trocar a senha ou desconectar aparelhos: cookies antigos deixam de valer.</summary>
    public int VersaoAcesso { get; set; }
    public int FalhasSeguidas { get; set; }
    public int Bloqueios { get; set; }
    public DateTime? BloqueadoAte { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? UltimoAcessoEm { get; set; }
}

/// <summary>Convite de uso único para uma pessoa da família criar o próprio login. Só o hash do código fica guardado.</summary>
public sealed class Convite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FamiliaId { get; set; }
    public Guid PessoaId { get; set; }
    public string CodigoHash { get; set; } = "";
    public DateTime ExpiraEm { get; set; }
    public DateTime? UsadoEm { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Senha guardada só como hash PBKDF2-SHA256 com sal aleatório. Formato: "v1:iteracoes:sal:hash" (base64).
/// </summary>
public static class SenhaSegura
{
    public const int TamanhoMinimo = 8;
    public const int TamanhoMaximo = 128;
    private const int Iteracoes = 210_000;

    private static readonly string[] Comuns =
    [
        "12345678", "123456789", "1234567890", "11111111", "00000000", "87654321", "password", "password1", "senha123", "senha1234",
        "qwertyui", "qwerty123", "abc12345", "iloveyou", "admin123", "brasil123", "favoa123", "mudar123", "12341234", "123123123",
    ];

    /// <summary>E-mail em minúsculas e sem espaços, ou nulo se não parece um e-mail.</summary>
    public static string? NormalizarEmail(string? email)
    {
        var e = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(e) || e.Length > 254) return null;
        try
        {
            var m = new MailAddress(e);
            return m.Address == e && m.Host.Contains('.') ? e : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Nulo quando a senha serve; senão, o motivo em uma frase.</summary>
    public static string? Validar(string? senha, string? email = null)
    {
        if (string.IsNullOrEmpty(senha) || senha.Length < TamanhoMinimo)
            return $"Use pelo menos {TamanhoMinimo} caracteres (uma frase longa é melhor que uma palavra difícil).";
        if (senha.Length > TamanhoMaximo)
            return $"Use no máximo {TamanhoMaximo} caracteres.";
        if (senha.Distinct().Count() == 1)
            return "Evite repetir o mesmo caractere.";
        if (Comuns.Contains(senha.ToLowerInvariant()))
            return "Essa senha é muito comum. Escolha outra.";
        var parteEmail = NormalizarEmail(email)?.Split('@')[0];
        if (parteEmail is { Length: >= 4 } && senha.Contains(parteEmail, StringComparison.OrdinalIgnoreCase))
            return "A senha não pode conter o seu e-mail.";
        return null;
    }

    public static string Gerar(string senha)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(senha), sal, Iteracoes, HashAlgorithmName.SHA256, 32);
        return $"v1:{Iteracoes}:{Convert.ToBase64String(sal)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string? senha, string? armazenado)
    {
        if (string.IsNullOrEmpty(senha) || string.IsNullOrEmpty(armazenado)) return false;
        var partes = armazenado.Split(':');
        if (partes is not ["v1", var iter, var sal, var hash] || !int.TryParse(iter, out var iteracoes)) return false;

        try
        {
            var esperado = Convert.FromBase64String(hash);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(senha), Convert.FromBase64String(sal), iteracoes, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Código aleatório de convite (URL-seguro) e o hash que fica no banco.</summary>
    public static (string Codigo, string Hash) NovoCodigo()
    {
        var codigo = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (codigo, HashDoCodigo(codigo));
    }

    public static string HashDoCodigo(string codigo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(codigo)));
}

/// <summary>
/// Freio contra quem tenta adivinhar o PIN: depois de 5 erros seguidos, bloqueia 1 minuto;
/// cada nova rodada de erros dobra o bloqueio, até 15 minutos. Acertar zera tudo.
/// </summary>
public sealed class ControleTentativas(Func<DateTimeOffset>? relogio = null)
{
    public const int ErrosAteBloquear = 5;
    private static readonly TimeSpan BloqueioInicial = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BloqueioMaximo = TimeSpan.FromMinutes(15);

    private sealed class Estado
    {
        public int Erros;
        public int Bloqueios;
        public DateTimeOffset? BloqueadoAte;
    }

    private readonly Dictionary<string, Estado> _porOrigem = [];
    private readonly Lock _trava = new();
    private DateTimeOffset Agora => relogio?.Invoke() ?? DateTimeOffset.UtcNow;

    /// <summary>Tempo que ainda falta de bloqueio para essa origem (zero = pode tentar).</summary>
    public TimeSpan Espera(string origem)
    {
        lock (_trava)
        {
            return _porOrigem.TryGetValue(origem, out var e) && e.BloqueadoAte is { } ate && ate > Agora ? ate - Agora : TimeSpan.Zero;
        }
    }

    public void RegistrarErro(string origem)
    {
        lock (_trava)
        {
            if (!_porOrigem.TryGetValue(origem, out var e)) _porOrigem[origem] = e = new Estado();
            e.Erros++;
            if (e.Erros < ErrosAteBloquear) return;

            var duracao = TimeSpan.FromTicks(Math.Min(BloqueioMaximo.Ticks, BloqueioInicial.Ticks << Math.Min(e.Bloqueios, 10)));
            e.BloqueadoAte = Agora + duracao;
            e.Bloqueios++;
            e.Erros = 0;
        }
    }

    public void RegistrarAcerto(string origem)
    {
        lock (_trava) _porOrigem.Remove(origem);
    }
}

/// <summary>Pessoas da família e quais já têm login.</summary>
public sealed record EstadoAcesso(IReadOnlyList<PessoaInfo> Pessoas);

/// <summary>Como está o Fávoa neste computador: sem nenhuma família, com família ainda sem login (veio do tempo do PIN) ou pronto.</summary>
public enum FaseAcesso
{
    SemFamilia,
    SemUsuarios,
    Pronto,
}

/// <summary>Quem entrou: vai no cookie. A versão derruba os aparelhos antigos quando a senha muda.</summary>
public sealed record SessaoUsuario(Guid UsuarioId, Guid FamiliaId, Guid PessoaId, string Nome, bool Dono, int Versao);

public sealed record ResultadoEntrada(bool Sucesso, string? Mensagem, SessaoUsuario? Sessao = null);

public sealed record ConviteInfo(string PessoaNome, string FamiliaNome);

public sealed record ResultadoConvite(bool Sucesso, string Mensagem, string? Codigo = null);

public sealed record DadosUsuario(Guid UsuarioId, string Email, bool Dono, string FamiliaNome);

/// <summary>Famílias, logins e convites. Fica fora do filtro por família de propósito: é ele que decide quem entra em qual.</summary>
public interface IAcessoServico
{
    Task<FaseAcesso> ObterFaseAsync(CancellationToken ct = default);

    /// <summary>Pessoas do perfil da família (para escolher "quem é você" no primeiro acesso, e para a tela de acesso).</summary>
    Task<IReadOnlyList<PessoaInfo>> ListarPessoasSemLoginAsync(CancellationToken ct = default);

    /// <summary>
    /// Primeiro acesso: cria a família (se não existir) e o dono. Com <paramref name="pessoaId"/> usa um perfil que já existia
    /// (dados de antes dos logins); sem ele, cria o perfil com <paramref name="nomePessoa"/>. Só funciona enquanto não há nenhum login.
    /// </summary>
    Task<ResultadoEntrada> ConfigurarPrimeiroAcessoAsync(string? nomeFamilia, Guid? pessoaId, string? nomePessoa, string email, string senha, CancellationToken ct = default);

    /// <summary>Cria uma família nova com o dono dela (cadastro aberto, para quando o Fávoa for hospedado para várias casas).</summary>
    Task<ResultadoEntrada> CriarFamiliaAsync(string nomeFamilia, string nomePessoa, string email, string senha, CancellationToken ct = default);

    Task<ResultadoEntrada> EntrarAsync(string email, string senha, CancellationToken ct = default);

    /// <summary>Confere o cookie: a versão, a família e a pessoa atuais do login; nulo se o login não existe mais.</summary>
    Task<SessaoUsuario?> ObterSessaoAsync(Guid usuarioId, CancellationToken ct = default);

    Task<DadosUsuario?> ObterUsuarioAsync(Guid usuarioId, CancellationToken ct = default);

    Task<ResultadoEntrada> TrocarSenhaAsync(Guid usuarioId, string senhaAtual, string senhaNova, CancellationToken ct = default);

    /// <summary>Derruba os cookies deste login (todos os aparelhos pedem a senha de novo).</summary>
    Task<ResultadoRegistro> DesconectarAparelhosAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Gera o link de convite de uma pessoa da família. Só o dono convida.</summary>
    Task<ResultadoConvite> CriarConviteAsync(Guid usuarioDonoId, Guid pessoaId, CancellationToken ct = default);

    Task<ConviteInfo?> ObterConviteAsync(string codigo, CancellationToken ct = default);

    /// <summary>O convidado escolhe o próprio apelido (o dono só sugere um); sem apelido, vale o que o dono digitou.</summary>
    Task<ResultadoEntrada> AceitarConviteAsync(string codigo, string email, string senha, string? apelido = null, CancellationToken ct = default);

    /// <summary>Redefinição feita por quem tem acesso à máquina (linha de comando): o app não manda e-mail ainda.</summary>
    Task<ResultadoRegistro> RedefinirSenhaAsync(string email, string senhaNova, CancellationToken ct = default);

    Task<EstadoAcesso> ObterEstadoAsync(Guid? familiaId, CancellationToken ct = default);

    /// <summary>Cria (id nulo) ou edita nome e cor de um perfil da família.</summary>
    Task<ResultadoRegistro> SalvarPessoaAsync(Guid familiaId, Guid? id, string nome, string cor, CancellationToken ct = default);

    /// <summary>Exclui um perfil sem dados e sem login.</summary>
    Task<ResultadoRegistro> ExcluirPessoaAsync(Guid familiaId, Guid id, CancellationToken ct = default);
}

/// <summary>
/// Decide se um pedido vem "do próprio computador" (entra sem PIN).
/// Pedidos repassados por proxy local (ex.: <c>tailscale serve</c>) chegam pelo 127.0.0.1,
/// mas vêm de outro aparelho: por isso cabeçalhos de encaminhamento e host diferente de localhost contam como "de fora".
/// </summary>
public static class RegraLocal
{
    private static readonly string[] HostsLocais = ["localhost", "127.0.0.1", "[::1]"];
    private static readonly string[] CabecalhosDeProxy = ["X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Real-IP", "Forwarded", "Via"];

    public static bool EhLocal(
        System.Net.IPAddress? remoto,
        System.Net.IPAddress? local,
        string? host,
        IEnumerable<string> cabecalhos,
        bool pcPeloIpDaRedeSemPin)
    {
        if (cabecalhos.Any(c => c.StartsWith("Tailscale-", StringComparison.OrdinalIgnoreCase) ||
                                CabecalhosDeProxy.Contains(c, StringComparer.OrdinalIgnoreCase)))
            return false;

        if (remoto is { IsIPv4MappedToIPv6: true }) remoto = remoto.MapToIPv4();
        if (local is { IsIPv4MappedToIPv6: true }) local = local.MapToIPv4();

        if (remoto is null || System.Net.IPAddress.IsLoopback(remoto))
        {
            // Loopback com host estranho (ex.: meu-pc.tailnet.ts.net) = alguém entrando por um proxy local.
            var nome = SemPorta(host);
            return nome.Length == 0 || HostsLocais.Contains(nome, StringComparer.OrdinalIgnoreCase);
        }

        // O próprio PC acessando pelo IP da rede (http://192.168.0.15:5080).
        return pcPeloIpDaRedeSemPin && remoto.Equals(local);
    }

    private static string SemPorta(string? host)
    {
        if (string.IsNullOrEmpty(host)) return "";
        if (host.StartsWith('[')) return host[..(host.IndexOf(']') + 1)];
        var i = host.LastIndexOf(':');
        return i > 0 ? host[..i] : host;
    }
}
