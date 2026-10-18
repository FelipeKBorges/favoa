using Favoa.Core.Pessoas;
using Microsoft.EntityFrameworkCore;

namespace Favoa.Data;

public static class AbrirBanco
{
    /// <summary>Abre o banco já filtrado pela pessoa e pela família atuais (registros pessoais só dela; a casa só a dela; o que gravar sai com elas).</summary>
    public static async Task<FavoaDbContext> AbrirAsync(this IDbContextFactory<FavoaDbContext> fabrica, IPessoaAtual pessoa, CancellationToken ct = default)
    {
        var db = await fabrica.CreateDbContextAsync(ct);
        db.PessoaAtual = pessoa.Id;
        db.FamiliaAtual = pessoa.FamiliaId;
        return db;
    }

    /// <summary>Abre o banco filtrado só pela família (cadastros da casa: o que for gravado não ganha "dono" pessoal).</summary>
    public static async Task<FavoaDbContext> AbrirDaFamiliaAsync(this IDbContextFactory<FavoaDbContext> fabrica, IPessoaAtual pessoa, CancellationToken ct = default)
    {
        var db = await fabrica.CreateDbContextAsync(ct);
        db.FamiliaAtual = pessoa.FamiliaId;
        return db;
    }

    /// <summary>Chave de configuração de uma pessoa (metas): "meta_kcal@{id}". Sem pessoa, a chave da casa.</summary>
    public static string ChaveDaPessoa(string chave, Guid? pessoa) => pessoa is { } p ? $"{chave}@{p:N}" : chave;
}
