using System.Security.Claims;
using Favoa.Core.Pessoas;
using Microsoft.AspNetCore.Components.Authorization;

namespace Favoa.Web;

/// <summary>
/// Quem está usando, a partir do cookie (claims <see cref="AcessoWeb.ClaimPessoa"/> e <see cref="AcessoWeb.ClaimFamilia"/>).
/// Vale para a sessão inteira do Blazor. Se por algum motivo não houver login, usa ids que não existem:
/// melhor mostrar nada do que mostrar os dados de todos.
/// </summary>
public sealed class PessoaDoUsuario(IHttpContextAccessor http, AuthenticationStateProvider autenticacao) : IPessoaAtual
{
    private Guid? _id;
    private Guid? _familiaId;

    public Guid? Id => _id ??= Ler(Usuario(), AcessoWeb.ClaimPessoa) ?? Guid.Empty;

    public Guid? FamiliaId => _familiaId ??= Ler(Usuario(), AcessoWeb.ClaimFamilia) ?? Guid.Empty;

    /// <summary>True para quem criou a conta da família (convida pessoas, mexe em dados da casa inteira).</summary>
    public bool Dono => Usuario()?.HasClaim(AcessoWeb.ClaimDono, "1") == true;

    /// <summary>Login de quem está usando (nulo sem login).</summary>
    public Guid? UsuarioId => Ler(Usuario(), AcessoWeb.ClaimUsuario);

    private ClaimsPrincipal? Usuario()
    {
        // Durante a sessão do Blazor o estado de autenticação já está pronto (vem do pedido que abriu a conexão).
        try
        {
            var estado = autenticacao.GetAuthenticationStateAsync();
            if (estado.IsCompletedSuccessfully && estado.Result.User.HasClaim(c => c.Type == AcessoWeb.ClaimUsuario))
                return estado.Result.User;
        }
        catch (InvalidOperationException) { /* fora de uma sessão do Blazor */ }
        return http.HttpContext?.User;
    }

    private static Guid? Ler(ClaimsPrincipal? usuario, string claim) =>
        Guid.TryParse(usuario?.FindFirst(claim)?.Value, out var id) ? id : null;
}
