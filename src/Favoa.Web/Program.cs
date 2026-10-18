using System.Globalization;
using Favoa.Data;
using Favoa.Web;
using Favoa.Web.Components;

var cultura = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;

var builder = WebApplication.CreateBuilder(args);

var opcoes = builder.Configuration.GetSection("Favoa").Get<OpcoesFavoa>() ?? new OpcoesFavoa();
builder.Services.AddFavoaDados(opcoes);
builder.Services.AddFavoaAcesso(opcoes); // login por e-mail e senha; a família isola os dados de cada casa
builder.Services.AddScoped<EventosFavoa>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await app.Services.PrepararBancoAsync();

// Esqueceu a senha? Quem tem acesso à máquina redefine pela linha de comando (o app ainda não manda e-mail):
//   dotnet Favoa.Web.dll --redefinir-senha seu@email.com NovaSenhaLonga
if (Array.IndexOf(args, "--redefinir-senha") is var i and >= 0)
{
    if (args.Length < i + 3)
    {
        Console.Error.WriteLine("Uso: --redefinir-senha <e-mail> <nova-senha>");
        return 2;
    }
    var r = await app.Services.GetRequiredService<Favoa.Core.Acesso.IAcessoServico>().RedefinirSenhaAsync(args[i + 1], args[i + 2]);
    Console.WriteLine(r.Mensagem);
    return r.Sucesso ? 0 : 1;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Sem HTTPS de propósito: o Fávoa roda só no seu computador (localhost) e, pela rede, atrás do Tailscale.
// Toda página exige login (e-mail e senha): veja AcessoWeb.
app.UseFavoaAcesso();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapFavoaAcesso();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Logger.LogInformation("Dados do Fávoa em {Pasta}", opcoes.PastaDados);
app.Run();
return 0;
