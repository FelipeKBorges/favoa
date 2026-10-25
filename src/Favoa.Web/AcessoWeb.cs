using System.Text;
using System.Net;
using System.Security.Claims;
using Favoa.Core.Acesso;
using Favoa.Core.Pessoas;
using Favoa.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace Favoa.Web;

/// <summary>
/// Porteiro do Fávoa: toda página exige login (e-mail e senha). O cookie diz quem entrou, de qual família e em qual perfil
/// (claims abaixo); é a família que isola os dados de cada casa e a pessoa que filtra treino, dieta, lembretes...
/// Sem login só abrem a entrada, o primeiro acesso (no computador onde o Fávoa roda), os convites e os arquivos públicos.
/// </summary>
public static class AcessoWeb
{
    public const string ClaimUsuario = "favoa:usuario";
    public const string ClaimFamilia = "favoa:familia";
    public const string ClaimPessoa = "favoa:pessoa";
    public const string ClaimDono = "favoa:dono";
    private const string ClaimVersao = "favoa:versao";
    private static readonly TimeSpan LembrarAparelho = TimeSpan.FromDays(30);

    // O que as telas de entrada precisam para aparecer bonitas, sem abrir o resto.
    private static readonly string[] LivresSemLogin =
        ["/entrar", "/sair", "/primeiro-acesso", "/criar-conta", "/recuperar", "/convite/", "/app.css", "/favicon.svg", "/fonts/", "/icones/", "/manifest.webmanifest", "/sw.js", "/offline.html"];

    public static IServiceCollection AddFavoaAcesso(this IServiceCollection services, OpcoesFavoa opcoes)
    {
        // Chaves que assinam o cookie, guardadas junto dos dados: o aparelho continua conectado depois de reiniciar o app.
        services.AddDataProtection()
            .SetApplicationName("Favoa")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(opcoes.PastaDados, "chaves")));

        services.AddSingleton(new ControleTentativas());
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "favoa_acesso";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = LembrarAparelho;
                o.SlidingExpiration = true;
                o.LoginPath = "/entrar";
                o.Events.OnValidatePrincipal = async ctx =>
                {
                    // Senha trocada, "desconectar aparelhos" ou usuário removido: cookies antigos deixam de valer.
                    var acesso = ctx.HttpContext.RequestServices.GetRequiredService<IAcessoServico>();
                    var p = ctx.Principal;
                    var valido = Guid.TryParse(p?.FindFirst(ClaimUsuario)?.Value, out var usuarioId)
                                 && await acesso.ObterSessaoAsync(usuarioId) is { } sessao
                                 && p!.FindFirst(ClaimVersao)?.Value == sessao.Versao.ToString()
                                 && p.FindFirst(ClaimFamilia)?.Value == sessao.FamiliaId.ToString()
                                 && p.FindFirst(ClaimPessoa)?.Value == sessao.PessoaId.ToString();
                    if (!valido)
                    {
                        ctx.RejectPrincipal();
                        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddCascadingAuthenticationState();
        services.AddScoped<IPessoaAtual, PessoaDoUsuario>(); // substitui a pessoa fixa (nula) registrada em AddFavoaDados
        return services;
    }

    public static bool EhLocal(HttpContext ctx) => RegraLocal.EhLocal(
        ctx.Connection.RemoteIpAddress,
        ctx.Connection.LocalIpAddress,
        ctx.Request.Host.Value,
        ctx.Request.Headers.Keys,
        ctx.RequestServices.GetRequiredService<OpcoesFavoa>().PcPeloIpDaRedeSemPin);

    public static WebApplication UseFavoaAcesso(this WebApplication app)
    {
        // Atrás do Tailscale com HTTPS, o app enxerga HTTP: marca o pedido como HTTPS para o cookie sair com "Secure".
        // Os cabeçalhos de proxy ficam como vieram (a regra de "é o próprio PC?" depende deles).
        app.Use(async (ctx, proximo) =>
        {
            if (RegraLocal.VeioPorHttps(ctx.Connection.RemoteIpAddress, ctx.Request.Headers["X-Forwarded-Proto"].ToString()))
                ctx.Request.Scheme = "https";
            await proximo();
        });
        app.Use(async (ctx, proximo) =>
        {
            AplicarCabecalhosDeSeguranca(ctx.Response.Headers, ctx.Request.IsHttps);
            await proximo();
        });
        app.UseAuthentication();
        app.Use(async (ctx, proximo) =>
        {
            var caminho = ctx.Request.Path.Value ?? "/";
            var logado = ctx.User.Identity?.IsAuthenticated == true && ctx.User.HasClaim(c => c.Type == ClaimUsuario);

            if (logado || EhLivre(caminho))
            {
                await proximo();
                return;
            }

            // Conexão do Blazor e arquivos internos: só recusa (o navegador não segue redirecionamento aqui).
            if (caminho.StartsWith("/_", StringComparison.Ordinal))
            {
                if (ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } paginas) paginas.Enabled = false;
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            ctx.Response.Redirect("/entrar?volta=" + Uri.EscapeDataString(caminho + ctx.Request.QueryString));
        });
        return app;
    }

    /// <summary>
    /// Política de conteúdo e afins. Scripts e estilos inline continuam permitidos porque o Blazor e o tema inicial precisam deles;
    /// o ganho está em proibir quadros (clickjacking), plugins, base trocada, formulários para outro site e qualquer recurso de fora.
    /// </summary>
    internal const string PoliticaDeConteudo =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "font-src 'self' data:; connect-src 'self' ws: wss:; worker-src 'self'; manifest-src 'self'; object-src 'none'; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    internal static void AplicarCabecalhosDeSeguranca(IHeaderDictionary cabecalhos, bool https)
    {
        cabecalhos["Content-Security-Policy"] = PoliticaDeConteudo;
        cabecalhos["X-Content-Type-Options"] = "nosniff";
        cabecalhos["X-Frame-Options"] = "DENY";
        cabecalhos["Referrer-Policy"] = "no-referrer";
        cabecalhos["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        cabecalhos["Cross-Origin-Opener-Policy"] = "same-origin";
        if (https) cabecalhos["Strict-Transport-Security"] = "max-age=15552000"; // 6 meses, sem subdomínios
    }

    private static bool EhLivre(string caminho) =>
        LivresSemLogin.Any(l => caminho.Equals(l, StringComparison.OrdinalIgnoreCase) || (l.EndsWith('/') && caminho.StartsWith(l, StringComparison.OrdinalIgnoreCase)));

    private static async Task EntrarAsync(HttpContext ctx, SessaoUsuario s)
    {
        var identidade = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, s.Nome),
                new Claim(ClaimUsuario, s.UsuarioId.ToString()),
                new Claim(ClaimFamilia, s.FamiliaId.ToString()),
                new Claim(ClaimPessoa, s.PessoaId.ToString()),
                new Claim(ClaimDono, s.Dono ? "1" : "0"),
                new Claim(ClaimVersao, s.Versao.ToString()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identidade),
            new AuthenticationProperties { IsPersistent = true });
    }

    public static WebApplication MapFavoaAcesso(this WebApplication app)
    {
        // ---------- Entrar ----------
        app.MapGet("/entrar", async (HttpContext ctx, IAcessoServico acesso, ControleTentativas tentativas, string? volta, string? aviso) =>
        {
            if (await acesso.ObterFaseAsync() != FaseAcesso.Pronto) return Results.Redirect("/primeiro-acesso");
            if (ctx.User.HasClaim(c => c.Type == ClaimUsuario)) return Results.Redirect(Destino(volta));
            var aviso2 = aviso == "senha" ? "Senha trocada. Entre com a senha nova." : null;
            return Pagina("Entrar", FormularioEntrada(MensagemDeEspera(tentativas.Espera(Origem(ctx))), aviso2, volta, "", AbertoParaCadastro(ctx)));
        });

        app.MapPost("/entrar", async (HttpContext ctx, IAcessoServico acesso, ControleTentativas tentativas) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var volta = form["volta"].ToString();
            var email = form["email"].ToString().Trim();
            var origem = Origem(ctx);
            var aberto = AbertoParaCadastro(ctx);

            if (await acesso.ObterFaseAsync() != FaseAcesso.Pronto) return Results.Redirect("/primeiro-acesso");
            if (MensagemDeEspera(tentativas.Espera(origem)) is { } espera) return Pagina("Entrar", FormularioEntrada(espera, null, volta, email, aberto));

            var r = await acesso.EntrarAsync(email, form["senha"].ToString());
            if (!r.Sucesso || r.Sessao is not { } sessao)
            {
                tentativas.RegistrarErro(origem);
                return Pagina("Entrar", FormularioEntrada(MensagemDeEspera(tentativas.Espera(origem)) ?? r.Mensagem, null, volta, email, aberto));
            }

            tentativas.RegistrarAcerto(origem);
            await EntrarAsync(ctx, sessao);
            return Results.Redirect(Destino(volta));
        }).DisableAntiforgery(); // o cookie é SameSite=Strict e o formulário só pede e-mail e senha

        // ---------- Primeiro acesso (cria a família e o dono) ----------
        app.MapGet("/primeiro-acesso", async (HttpContext ctx, IAcessoServico acesso) =>
        {
            var fase = await acesso.ObterFaseAsync();
            if (fase == FaseAcesso.Pronto) return Results.Redirect("/");
            if (!EhLocal(ctx)) return Pagina("Primeiro acesso", AvisoSomenteNoPc());
            return Pagina("Primeiro acesso", await FormularioPrimeiroAcessoAsync(acesso, fase, null, null));
        });

        app.MapPost("/primeiro-acesso", async (HttpContext ctx, IAcessoServico acesso) =>
        {
            var fase = await acesso.ObterFaseAsync();
            if (fase == FaseAcesso.Pronto) return Results.Redirect("/");
            if (!EhLocal(ctx)) return Pagina("Primeiro acesso", AvisoSomenteNoPc());

            var form = await ctx.Request.ReadFormAsync();
            var dados = new DadosForm(form);
            if (dados.Senha != dados.Confirmacao) return Pagina("Primeiro acesso", await FormularioPrimeiroAcessoAsync(acesso, fase, "As duas senhas não batem.", dados));

            // Ninguém escolhe de uma lista: a pessoa digita o apelido. Em banco migrado, se o apelido
            // bate com alguém que já tem registros, o acesso se liga a ele; senão pede confirmação.
            Guid? pessoaId = null;
            if (fase == FaseAcesso.SemUsuarios)
            {
                var alvo = Favoa.Core.Captura.Texto.Normalizar(dados.Nome);
                if (alvo.Length == 0)
                    return Pagina("Primeiro acesso", await FormularioPrimeiroAcessoAsync(acesso, fase, "Escreva o seu apelido.", dados));
                var existentes = await acesso.ListarPessoasSemLoginAsync();
                pessoaId = existentes.FirstOrDefault(p => Favoa.Core.Captura.Texto.Normalizar(p.Nome) == alvo)?.Id;
                if (pessoaId is null && existentes.Count > 0 && !dados.PessoaNova)
                    return Pagina("Primeiro acesso", await FormularioPrimeiroAcessoAsync(acesso, fase,
                        "Não achei esse apelido nos registros que já existem aqui. Confira como você aparece nos seus registros ou marque que é uma pessoa nova.", dados, pedirConfirmacao: true));
            }

            var r = await acesso.ConfigurarPrimeiroAcessoAsync(dados.Casa, pessoaId, dados.Nome, dados.Email, dados.Senha);
            if (!r.Sucesso || r.Sessao is not { } sessao)
                return Pagina("Primeiro acesso", await FormularioPrimeiroAcessoAsync(acesso, fase, r.Mensagem, dados));

            await EntrarAsync(ctx, sessao);
            return Results.Redirect("/");
        }).DisableAntiforgery();

        // ---------- Criar uma família nova (só quando o cadastro está aberto) ----------
        app.MapGet("/criar-conta", (HttpContext ctx) =>
            AbertoParaCadastro(ctx) ? Pagina("Criar conta", FormularioNovaFamilia(null, null)) : Results.Redirect("/entrar"));

        app.MapPost("/criar-conta", async (HttpContext ctx, IAcessoServico acesso) =>
        {
            if (!AbertoParaCadastro(ctx)) return Results.Redirect("/entrar");
            var dados = new DadosForm(await ctx.Request.ReadFormAsync());
            if (dados.Senha != dados.Confirmacao) return Pagina("Criar conta", FormularioNovaFamilia("As duas senhas não batem.", dados));
            var r = await acesso.CriarFamiliaAsync(dados.Casa ?? "", dados.Nome, dados.Email, dados.Senha);
            if (!r.Sucesso || r.Sessao is not { } sessao) return Pagina("Criar conta", FormularioNovaFamilia(r.Mensagem, dados));
            await EntrarAsync(ctx, sessao);
            return Results.Redirect("/");
        }).DisableAntiforgery();

        // ---------- Esqueci a senha (código de recuperação guardado antes) ----------
        app.MapGet("/recuperar", (HttpContext ctx) => Pagina("Recuperar senha", FormularioRecuperar(null, "")));
        app.MapPost("/recuperar", async (HttpContext ctx, IAcessoServico acesso, ControleTentativas tentativas) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim();
            var origem = Origem(ctx);
            if (MensagemDeEspera(tentativas.Espera(origem)) is { } espera) return Pagina("Recuperar senha", FormularioRecuperar(espera, email));
            if (form["senha"].ToString() != form["confirmacao"].ToString()) return Pagina("Recuperar senha", FormularioRecuperar("As duas senhas não batem.", email));

            var r = await acesso.RecuperarSenhaAsync(email, form["codigo"].ToString(), form["senha"].ToString());
            if (!r.Sucesso)
            {
                tentativas.RegistrarErro(origem);
                return Pagina("Recuperar senha", FormularioRecuperar(MensagemDeEspera(tentativas.Espera(origem)) ?? r.Mensagem, email));
            }
            tentativas.RegistrarAcerto(origem);
            return Results.Redirect("/entrar?aviso=senha");
        }).DisableAntiforgery();

        // ---------- Convite: a pessoa convidada cria o próprio login ----------
        app.MapGet("/convite/{codigo}", async (string codigo, IAcessoServico acesso) =>
            await acesso.ObterConviteAsync(codigo) is { } convite
                ? Pagina("Convite", FormularioConvite(convite, codigo, null, ""))
                : Pagina("Convite", ConviteInvalido()));

        app.MapPost("/convite/{codigo}", async (HttpContext ctx, string codigo, IAcessoServico acesso) =>
        {
            if (await acesso.ObterConviteAsync(codigo) is not { } convite) return Pagina("Convite", ConviteInvalido());
            var dados = new DadosForm(await ctx.Request.ReadFormAsync());
            if (dados.Senha != dados.Confirmacao) return Pagina("Convite", FormularioConvite(convite, codigo, "As duas senhas não batem.", dados.Email, dados.Apelido));
            var r = await acesso.AceitarConviteAsync(codigo, dados.Email, dados.Senha, dados.Apelido);
            if (!r.Sucesso || r.Sessao is not { } sessao) return Pagina("Convite", FormularioConvite(convite, codigo, r.Mensagem, dados.Email, dados.Apelido));
            await EntrarAsync(ctx, sessao);
            return Results.Redirect("/");
        }).DisableAntiforgery();

        // ---------- Chave do diário (só no próprio computador, por quem é dono) ----------
        // Sem essa chave o diário cifrado não abre (disco perdido, computador novo). Fica fora da cópia do OneDrive de propósito.
        app.MapGet("/diario/chave", (HttpContext ctx, Favoa.Core.Diario.ICifraDiario cifra) =>
        {
            if (!EhLocal(ctx) || !ctx.User.HasClaim(ClaimDono, "1"))
                return Results.Text("A chave do diário só pode ser baixada por quem criou a conta da casa, no próprio computador onde o Fávoa roda.", "text/plain; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
            var chave = ((Favoa.Data.CifraDiario)cifra).ExportarChave();
            var texto = "Chave do diário do Fávoa\r\n\r\n" + chave + "\r\n\r\n" +
                "Guarde este arquivo fora do computador (gerenciador de senhas, pendrive). Quem tiver a chave e o banco lê os diários.\r\n" +
                "Para restaurar num computador novo: antes de abrir o Fávoa, crie a pasta chaves dentro da pasta de dados e salve a linha da chave, sozinha, no arquivo chaves\\diario.key.\r\n";
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.File(System.Text.Encoding.UTF8.GetBytes(texto), "text/plain; charset=utf-8", "favoa-chave-do-diario.txt");
        });

        // ---------- Senhas das contas (só no próprio computador, por quem é dono) ----------
        // O app não manda e-mail: quem esquece a senha pede ao administrador do Fávoa, que gera uma temporária aqui.
        app.MapGet("/admin/senhas", async (HttpContext ctx, IAcessoServico acesso) =>
            await SenhasAsync(ctx, acesso, null));

        app.MapPost("/admin/senhas/gerar", async (HttpContext ctx, IAcessoServico acesso) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            return await SenhasAsync(ctx, acesso, Guid.TryParse(form["usuario"], out var id) ? id : Guid.Empty);
        }).DisableAntiforgery(); // cookie SameSite=Strict, e só vale no próprio computador

        app.MapGet("/sair", async (HttpContext ctx, string? aviso) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect(aviso == "senha" ? "/entrar?aviso=senha" : "/entrar");
        });

        return app;
    }

    private static async Task<IResult> SenhasAsync(HttpContext ctx, IAcessoServico acesso, Guid? gerarPara)
    {
        var sessao = Guid.TryParse(ctx.User.FindFirst(ClaimUsuario)?.Value, out var eu) ? await acesso.ObterSessaoAsync(eu) : null;
        if (sessao is not { Dono: true } || !EhLocal(ctx))
            return Pagina("Senhas das contas", """
                <p class="entrada__erro" role="alert">Esta página só abre no computador onde o Fávoa roda, com o login de dono.</p>
                <p class="entrada__dica">Abra <strong>http://localhost:5080/admin/senhas</strong> nesse computador.</p>
                """);

        var aviso = "";
        if (gerarPara is { } id)
        {
            var r = await acesso.GerarSenhaTemporariaAsync(id);
            aviso = r.Sucesso && r.Senha is not null
                ? $"""<p class="entrada__dica" role="status">{H(r.Mensagem)}</p><p class="entrada__dica">Senha temporária (aparece só agora):</p><p><code style="font-size:1.4rem;letter-spacing:.05em">{H(r.Senha)}</code></p><p class="entrada__dica">Passe para a pessoa por um canal privado. Ao entrar, ela troca em Seus dados.</p>"""
                : $"""<p class="entrada__erro" role="alert">{H(r.Mensagem)}</p>""";
        }

        var linhas = new StringBuilder();
        foreach (var c in await acesso.ListarContasAsync())
            linhas.Append($"""
                <form method="post" action="/admin/senhas/gerar" class="entrada__form" style="margin:.75rem 0;padding-top:.75rem;border-top:1px solid var(--borda)">
                  <p style="margin:0"><strong>{H(c.Pessoa)}</strong> · {H(c.Casa)}{(c.Dono ? " · dono" : "")}<br><span class="entrada__dica">{H(c.Email)}</span></p>
                  <input type="hidden" name="usuario" value="{c.UsuarioId}" />
                  <button type="submit" class="botao">Gerar senha temporária</button>
                </form>
                """);

        return Pagina("Senhas das contas", $"""
            <h1>Senhas das contas</h1>
            {aviso}
            {linhas}
            <p class="entrada__dica"><a href="/">Voltar</a></p>
            """);
    }

    /// <summary>Cadastro de famílias novas só quando o dono do Fávoa liga a opção (publicação para várias casas).</summary>
    private static bool AbertoParaCadastro(HttpContext ctx) =>
        ctx.RequestServices.GetRequiredService<OpcoesFavoa>().CadastroAberto;

    /// <summary>
    /// Quem está tentando, para o bloqueio por erros. Cabeçalhos de encaminhamento só valem se o pedido veio
    /// de um proxy no próprio PC (Tailscale); vindos direto da rede, poderiam ser forjados para fugir do bloqueio.
    /// </summary>
    private static string Origem(HttpContext ctx)
    {
        var remoto = ctx.Connection.RemoteIpAddress;
        if (remoto is not null && IPAddress.IsLoopback(remoto))
        {
            var h = ctx.Request.Headers;
            var encaminhado = h["X-Forwarded-For"].ToString().Split(',')[0].Trim();
            if (encaminhado.Length > 0) return "xff:" + encaminhado;
            if (h["Tailscale-User-Login"].ToString() is { Length: > 0 } usuario) return "ts:" + usuario;
        }
        return remoto?.ToString() ?? "desconhecida";
    }

    /// <summary>Só caminhos internos ("/financas"), nunca outro site.</summary>
    private static string Destino(string? volta) =>
        !string.IsNullOrEmpty(volta) && volta.StartsWith('/') && !volta.StartsWith("//") && !volta.StartsWith("/\\") ? volta : "/";

    private static string? MensagemDeEspera(TimeSpan espera) =>
        espera <= TimeSpan.Zero ? null
        : $"Muitas tentativas erradas. Tente de novo em {(espera.TotalSeconds < 60 ? $"{Math.Ceiling(espera.TotalSeconds)} segundos" : $"{Math.Ceiling(espera.TotalMinutes)} minuto(s)")}.";

    // ---------- Telas (HTML simples, sem Blazor: abrem antes de haver login) ----------

    private sealed class DadosForm(IFormCollection f)
    {
        public string Email { get; } = f["email"].ToString().Trim();
        public string Senha { get; } = f["senha"].ToString();
        public string Confirmacao { get; } = f["confirmacao"].ToString();
        public string Nome { get; } = f["nome"].ToString().Trim();
        public string? Casa { get; } = f["casa"].ToString().Trim() is { Length: > 0 } c ? c : null;
        public bool PessoaNova { get; } = f["novo"].ToString() == "1";
        public string Apelido { get; } = f["apelido"].ToString().Trim();
    }

    private static string H(string? texto) => WebUtility.HtmlEncode(texto ?? "");

    private static string Erro(string? erro) =>
        erro is null ? "" : $"<p class=\"entrada__erro\" role=\"alert\">{H(erro)}</p>";

    private static string Aviso(string? aviso) =>
        aviso is null ? "" : $"<p class=\"entrada__dica\" role=\"status\">{H(aviso)}</p>";

    private const string CampoSenhaNova = """
        <label for="senha">Senha (8 ou mais caracteres)</label>
        <input id="senha" name="senha" type="password" autocomplete="new-password" minlength="8" maxlength="128" required />
        <label for="confirmacao">Repita a senha</label>
        <input id="confirmacao" name="confirmacao" type="password" autocomplete="new-password" minlength="8" maxlength="128" required />
        """;

    private static string FormularioRecuperar(string? erro, string email) => $"""
        <p class="entrada__dica">Use o código de recuperação que você guardou em <strong>Seus dados</strong>. Ele vale uma vez.</p>
        <form method="post" action="/recuperar" class="entrada__form">
          <label for="email">E-mail</label>
          <input id="email" name="email" type="email" inputmode="email" autocomplete="username" maxlength="254" value="{H(email)}" required />
          <label for="codigo">Código de recuperação</label>
          <input id="codigo" name="codigo" type="text" autocomplete="off" autocapitalize="off" spellcheck="false" maxlength="40" placeholder="xxxx-xxxx-xxxx-xxxx" required />
          <label for="senha">Senha nova</label>
          <input id="senha" name="senha" type="password" autocomplete="new-password" minlength="8" maxlength="128" required />
          <label for="confirmacao">Repita a senha nova</label>
          <input id="confirmacao" name="confirmacao" type="password" autocomplete="new-password" minlength="8" maxlength="128" required />
          {Erro(erro)}
          <button type="submit" class="botao botao--primario">Trocar a senha</button>
        </form>
        <p class="entrada__dica">Sem código? Peça uma senha temporária a quem administra o Fávoa neste computador. <a href="/entrar">Voltar</a></p>
        """;

    private static string FormularioEntrada(string? erro, string? aviso, string? volta, string email, bool cadastroAberto) => $"""
        <form method="post" action="/entrar" class="entrada__form">
          <input type="hidden" name="volta" value="{H(Destino(volta))}" />
          <label for="email">E-mail</label>
          <input id="email" name="email" type="email" inputmode="email" autocomplete="username" maxlength="254" value="{H(email)}" required {(email.Length == 0 ? "autofocus" : "")} />
          <label for="senha">Senha</label>
          <input id="senha" name="senha" type="password" autocomplete="current-password" maxlength="128" required {(email.Length > 0 ? "autofocus" : "")} />
          {Erro(erro)}
          {Aviso(aviso)}
          <button type="submit" class="botao botao--primario">Entrar</button>
        </form>
        <p class="entrada__dica">Este aparelho fica lembrado por 30 dias. <a href="/recuperar">Esqueci a senha</a></p>
        {(cadastroAberto ? "<p class=\"entrada__dica\"><a href=\"/criar-conta\">Criar uma conta para a minha casa</a></p>" : "")}
        """;

    private static Task<string> FormularioPrimeiroAcessoAsync(IAcessoServico acesso, FaseAcesso fase, string? erro, DadosForm? d, bool pedirConfirmacao = false)
    {
        var confirmacao = pedirConfirmacao
            ? "<label class=\"entrada__opcao\"><input type=\"checkbox\" name=\"novo\" value=\"1\" /> Sou uma pessoa nova, sem registros anteriores</label>"
            : "";
        var dica = fase == FaseAcesso.SemUsuarios
            ? "<p class=\"entrada__dica\">Já usa o Fávoa neste computador? Digite o apelido que aparece nos seus registros para continuar com eles.</p>"
            : "";
        return Task.FromResult($"""
            <form method="post" action="/primeiro-acesso" class="entrada__form">
              <p class="entrada__intro"><strong>Bem-vindo ao Fávoa.</strong> Crie o seu acesso: quem cria é o dono da casa e convida os outros depois.</p>
              <label for="casa">Nome da casa (opcional)</label>
              <input id="casa" name="casa" type="text" maxlength="60" value="{H(d?.Casa)}" placeholder="Ex.: Casa do Davi e da Eva" />
              <label for="nome">Seu apelido</label>
              <input id="nome" name="nome" type="text" maxlength="40" value="{H(d?.Nome)}" autocomplete="nickname" required />
              {dica}
              {confirmacao}
              <label for="email">E-mail</label>
              <input id="email" name="email" type="email" inputmode="email" autocomplete="username" maxlength="254" value="{H(d?.Email)}" required />
              {CampoSenhaNova}
              {Erro(erro)}
              <button type="submit" class="botao botao--primario">Criar acesso</button>
            </form>
            """);
    }

    private static string FormularioNovaFamilia(string? erro, DadosForm? d) => $"""
        <form method="post" action="/criar-conta" class="entrada__form">
          <p class="entrada__intro"><strong>Criar uma conta.</strong> Você será o dono da casa e poderá convidar os outros.</p>
          <label for="casa">Nome da casa (opcional)</label>
          <input id="casa" name="casa" type="text" maxlength="60" value="{H(d?.Casa)}" />
          <label for="nome">Seu apelido</label>
          <input id="nome" name="nome" type="text" maxlength="40" value="{H(d?.Nome)}" autocomplete="nickname" required />
          <label for="email">E-mail</label>
          <input id="email" name="email" type="email" inputmode="email" autocomplete="username" maxlength="254" value="{H(d?.Email)}" required />
          {CampoSenhaNova}
          {Erro(erro)}
          <button type="submit" class="botao botao--primario">Criar conta</button>
        </form>
        <p class="entrada__dica">Seus dados ficam no computador de quem hospeda o Fávoa, que consegue abri-los. Use uma senha só sua e não registre nada que você não confiaria a essa pessoa.</p>
        <p class="entrada__dica"><a href="/entrar">Já tenho conta</a></p>
        """;

    private static string FormularioConvite(ConviteInfo convite, string codigo, string? erro, string email, string? apelido = null) => $"""
        <form method="post" action="/convite/{H(Uri.EscapeDataString(codigo))}" class="entrada__form">
          <p class="entrada__intro">Você foi convidado(a) para <strong>{H(convite.FamiliaNome)}</strong>. Crie o seu acesso.</p>
          <label for="apelido">Seu apelido</label>
          <input id="apelido" name="apelido" type="text" maxlength="40" value="{H(apelido ?? convite.PessoaNome)}" autocomplete="nickname" required />
          <label for="email">E-mail</label>
          <input id="email" name="email" type="email" inputmode="email" autocomplete="username" maxlength="254" value="{H(email)}" required autofocus />
          {CampoSenhaNova}
          {Erro(erro)}
          <button type="submit" class="botao botao--primario">Entrar na casa</button>
        </form>
        <p class="entrada__dica">Seus dados ficam no computador de quem hospeda o Fávoa, que consegue abri-los. Use uma senha só sua.</p>
        """;

    private static string ConviteInvalido() => """
        <p class="entrada__erro" role="alert">Esse convite não vale mais.</p>
        <p class="entrada__dica">Peça um novo link a quem criou a conta. Cada convite vale por 7 dias e só pode ser usado uma vez.</p>
        <p class="entrada__dica"><a href="/entrar">Ir para a entrada</a></p>
        """;

    private static string AvisoSomenteNoPc() => """
        <p class="entrada__erro" role="alert">O primeiro acesso só pode ser feito no computador onde o Fávoa roda.</p>
        <p class="entrada__dica">Abra <strong>http://localhost:5080</strong> nesse computador e crie o acesso lá. Depois entre aqui com o seu e-mail e senha.</p>
        """;

    private static IResult Pagina(string titulo, string corpo)
    {
        var html = $"""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <meta name="color-scheme" content="dark" />
              <meta name="theme-color" content="#15110C" />
              <meta name="referrer" content="no-referrer" />
              <title>{H(titulo)} · Fávoa</title>
              <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
              <link rel="manifest" href="/manifest.webmanifest" />
              <link rel="apple-touch-icon" href="/icones/apple-touch-icon.png" />
              <meta name="apple-mobile-web-app-capable" content="yes" />
              <meta name="apple-mobile-web-app-title" content="Fávoa" />
              <link rel="stylesheet" href="/app.css" />
            </head>
            <body>
              <main class="entrada">
                <div class="entrada__cartao">
                  <div class="entrada__marca">
                    <svg width="44" height="44" viewBox="0 0 32 32" aria-hidden="true"><path d="M8 3h16l8 13-8 13H8L0 16z" fill="none" stroke="#F2A93B" stroke-width="2.5"/><path d="M11 9h10l5 7-5 7H11l-5-7z" fill="#F2A93B"/></svg>
                    <span>fávoa</span>
                  </div>
                  {corpo}
                </div>
              </main>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html; charset=utf-8");
    }
}
