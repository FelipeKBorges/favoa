using System.Globalization;
using System.Text;
using Favoa.Core;
using Favoa.Core.Fitness;
using Favoa.Data;
using Favoa.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Servidor MCP do Fávoa: o cliente MCP inicia este processo e conversa por stdin/stdout.
// Tudo que não for protocolo vai para stderr (o app mostra nos logs de MCP).
//
// Uso: dotnet Favoa.Mcp.dll [--pasta-dados <pasta>]
// Pasta padrão: %LOCALAPPDATA%\Favoa (a mesma do app). Também aceita a variável FAVOA_PASTA_DADOS.

var cultura = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = cultura;
CultureInfo.DefaultThreadCurrentUICulture = cultura;
Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

var pasta = LerPasta(args) ?? Environment.GetEnvironmentVariable("FAVOA_PASTA_DADOS");
var opcoes = new OpcoesFavoa { PastaDados = pasta ?? "" };

var servicos = new ServiceCollection().AddFavoaDados(opcoes).BuildServiceProvider();
await servicos.PrepararBancoAsync(); // mesmas migrações do app; não faz nada se já estiver em dia

// A pessoa de cada chamada vem do argumento "pessoa" da ferramenta (padrão: a primeira da casa).
var fabrica = servicos.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
var pessoa = new Favoa.Core.Pessoas.PessoaFixa(null);
var servidor = new ServidorMcp(new Ferramentas(new FavoaServico(fabrica, pessoa), new FitnessServico(fabrica, pessoa), fabrica, pessoa));

Console.Error.WriteLine($"[favoa-mcp] pronto · dados em {opcoes.CaminhoBanco}");

using var entrada = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
var saida = Console.Out;

while (await entrada.ReadLineAsync() is { } linha)
{
    if (string.IsNullOrWhiteSpace(linha)) continue;
    string? resposta;
    try
    {
        resposta = await servidor.ProcessarAsync(linha);
    }
    catch (Exception e)
    {
        // Nunca derruba o servidor por causa de uma chamada.
        Console.Error.WriteLine($"[favoa-mcp] erro: {e}");
        continue;
    }

    if (resposta is null) continue;
    await saida.WriteLineAsync(resposta);
    await saida.FlushAsync();
}

static string? LerPasta(string[] args)
{
    var i = Array.IndexOf(args, "--pasta-dados");
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
