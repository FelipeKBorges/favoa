using Favoa.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <summary>Medidas caseiras por alimento (1 unidade = 45 g). Só uma tabela nova; os dados entram na subida do app.</summary>
    [DbContext(typeof(FavoaDbContext))]
    [Migration("20261019100000_PorcoesDosAlimentos")]
    public partial class PorcoesDosAlimentos : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fit_alimento_porcao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FamiliaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AlimentoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Gramas = table.Column<double>(type: "REAL", nullable: false),
                    Padrao = table.Column<bool>(type: "INTEGER", nullable: false),
                    Origem = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fit_alimento_porcao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fit_alimento_porcao_fit_alimento_AlimentoId",
                        column: x => x.AlimentoId,
                        principalTable: "fit_alimento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fit_alimento_porcao_AlimentoId_FamiliaId_Nome",
                table: "fit_alimento_porcao",
                columns: new[] { "AlimentoId", "FamiliaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fit_alimento_porcao_FamiliaId",
                table: "fit_alimento_porcao",
                column: "FamiliaId");

            // Como cada item foi lançado ("1,5 fatia"): os gramas continuam sendo o que vale nas contas.
            foreach (var tabela in new[] { "fit_consumo", "fit_plano_item" })
            {
                migrationBuilder.AddColumn<string>(name: "Unidade", table: tabela, type: "TEXT", maxLength: 60, nullable: true);
                migrationBuilder.AddColumn<double>(name: "Quantidade", table: tabela, type: "REAL", nullable: true);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabela in new[] { "fit_consumo", "fit_plano_item" })
            {
                migrationBuilder.DropColumn(name: "Unidade", table: tabela);
                migrationBuilder.DropColumn(name: "Quantidade", table: tabela);
            }

            migrationBuilder.DropTable(name: "fit_alimento_porcao");
        }
    }
}
