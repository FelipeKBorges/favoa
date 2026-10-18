using Favoa.Core.Acesso;
using Favoa.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

/// <summary>
/// Base nova não tem família nem pessoa (o primeiro acesso cria). Os testes de serviço precisam de uma casa pronta:
/// uma família com o Davi, as categorias e a conta iniciais. Idempotente.
/// </summary>
public static class CasaDeTeste
{
    public const string Senha = "uma frase longa e boa";

    public static async Task PrepararBancoComCasaAsync(this IServiceProvider sp, string nome = "Davi")
    {
        await sp.PrepararBancoAsync();
        var fabrica = sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        await using (var db = await fabrica.CreateDbContextAsync())
            if (await db.Familias.AnyAsync()) return;
        var r = await new AcessoServico(fabrica).ConfigurarPrimeiroAcessoAsync(null, null, nome, $"{nome.ToLowerInvariant()}@exemplo.com", Senha);
        Assert.True(r.Sucesso, r.Mensagem);
    }

    public static async Task<Guid> FamiliaIdAsync(IDbContextFactory<FavoaDbContext> fabrica)
    {
        await using var db = await fabrica.CreateDbContextAsync();
        return await db.Familias.Select(f => f.Id).FirstAsync();
    }
}
