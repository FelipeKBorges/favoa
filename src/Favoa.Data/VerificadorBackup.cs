using Microsoft.Data.Sqlite;

namespace Favoa.Data;

/// <summary>Confere se uma cópia do banco abre e está íntegra. Uma cópia que nunca foi aberta não é garantia de nada.</summary>
public static class VerificadorBackup
{
    /// <summary>Abre o arquivo só para leitura, roda <c>PRAGMA integrity_check</c> e confere que as tabelas principais existem.</summary>
    public static (bool Ok, string Detalhe) Verificar(string caminho)
    {
        try
        {
            using var conexao = new SqliteConnection($"Data Source={caminho};Mode=ReadOnly;Pooling=False");
            conexao.Open();
            using var integridade = conexao.CreateCommand();
            integridade.CommandText = "PRAGMA integrity_check";
            var resposta = integridade.ExecuteScalar()?.ToString();
            if (resposta != "ok") return (false, $"integridade: {resposta}");

            using var tabelas = conexao.CreateCommand();
            tabelas.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('cfg_familia', 'cfg_pessoa', 'cfg_usuario', '__EFMigrationsHistory')";
            var n = Convert.ToInt32(tabelas.ExecuteScalar());
            return n == 4 ? (true, "ok") : (false, "faltam tabelas essenciais");
        }
        catch (SqliteException ex)
        {
            return (false, ex.Message);
        }
    }
}
