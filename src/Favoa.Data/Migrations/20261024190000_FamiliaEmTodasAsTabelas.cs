using Favoa.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <summary>
    /// Todas as tabelas de dados passam a ter FamiliaId (as pessoais e as filhas também), para o filtro da casa ser o mesmo em tudo.
    /// Só colunas e índices novos (sem recriar tabelas). O valor vem da pessoa dona, ou do pai no caso das filhas; o que ficar
    /// sem casa vai para a família mais antiga (a que já existia quando só havia uma).
    /// </summary>
    [DbContext(typeof(FavoaDbContext))]
    [Migration("20261024190000_FamiliaEmTodasAsTabelas")]
    public partial class FamiliaEmTodasAsTabelas : Migration
    {
        private static readonly string[] Pessoais =
        [
            "rot_habito", "rot_agua", "rot_lembrete", "cfg_push_assinatura", "fit_ficha", "fit_sessao",
            "fit_plano_item", "fit_consumo", "fit_peso", "fit_medida",
        ];

        // (filha, coluna que aponta para o pai, pai)
        private static readonly (string Filha, string Coluna, string Pai)[] Filhas =
        [
            ("fit_ficha_item", "FichaId", "fit_ficha"),
            ("fit_sessao_exercicio", "SessaoId", "fit_sessao"),
            ("rot_habito_registro", "HabitoId", "rot_habito"),
            ("prj_item", "ProjetoId", "prj_projeto"),
            ("rot_tarefa_responsavel", "TarefaId", "rot_tarefa"),
        ];

        private const string MaisAntiga = "(SELECT Id FROM cfg_familia ORDER BY CriadoEm LIMIT 1)";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var tabela in Pessoais.Concat(Filhas.Select(f => f.Filha)))
            {
                migrationBuilder.AddColumn<Guid>(name: "FamiliaId", table: tabela, type: "TEXT", nullable: true);
                migrationBuilder.CreateIndex(name: $"IX_{tabela}_FamiliaId", table: tabela, column: "FamiliaId");
            }

            foreach (var tabela in Pessoais)
                migrationBuilder.Sql(
                    $"UPDATE {tabela} SET FamiliaId = COALESCE((SELECT p.FamiliaId FROM cfg_pessoa p WHERE p.Id = {tabela}.PessoaId), {MaisAntiga}) WHERE FamiliaId IS NULL;");

            // As filhas leem do pai, que já foi preenchido acima (ou na migração das famílias, no caso de projeto e tarefa).
            foreach (var (filha, coluna, pai) in Filhas)
                migrationBuilder.Sql(
                    $"UPDATE {filha} SET FamiliaId = COALESCE((SELECT x.FamiliaId FROM {pai} x WHERE x.Id = {filha}.{coluna}), {MaisAntiga}) WHERE FamiliaId IS NULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabela in Pessoais.Concat(Filhas.Select(f => f.Filha)))
            {
                migrationBuilder.DropIndex(name: $"IX_{tabela}_FamiliaId", table: tabela);
                migrationBuilder.DropColumn(name: "FamiliaId", table: tabela);
            }
        }
    }
}
