using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <inheritdoc />
    public partial class Fitness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fit_alimento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Grupo = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Kcal = table.Column<double>(type: "REAL", nullable: false),
                    Proteina = table.Column<double>(type: "REAL", nullable: false),
                    Carboidrato = table.Column<double>(type: "REAL", nullable: false),
                    Gordura = table.Column<double>(type: "REAL", nullable: false),
                    Fibra = table.Column<double>(type: "REAL", nullable: false),
                    CodigoTaco = table.Column<int>(type: "INTEGER", nullable: true),
                    Personalizado = table.Column<bool>(type: "INTEGER", nullable: false),
                    NomePorcao = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    GramasPorcao = table.Column<int>(type: "INTEGER", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fit_alimento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fit_exercicio",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Grupo = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Ativo = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fit_exercicio", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fit_ficha",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false),
                    Ativa = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fit_ficha", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fit_peso",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kg = table.Column<decimal>(type: "TEXT", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fit_peso", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fit_consumo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Refeicao = table.Column<int>(type: "INTEGER", nullable: false),
                    AlimentoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Gramas = table.Column<int>(type: "INTEGER", nullable: false),
                    DoPlano = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fit_consumo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fit_consumo_fit_alimento_AlimentoId",
                        column: x => x.AlimentoId,
                        principalTable: "fit_alimento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fit_plano_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Refeicao = table.Column<int>(type: "INTEGER", nullable: false),
                    AlimentoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Gramas = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fit_plano_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fit_plano_item_fit_alimento_AlimentoId",
                        column: x => x.AlimentoId,
                        principalTable: "fit_alimento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fit_ficha_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FichaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExercicioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false),
                    Series = table.Column<int>(type: "INTEGER", nullable: true),
                    Repeticoes = table.Column<int>(type: "INTEGER", nullable: true),
                    CargaKg = table.Column<decimal>(type: "TEXT", nullable: true),
                    Minutos = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fit_ficha_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fit_ficha_item_fit_exercicio_ExercicioId",
                        column: x => x.ExercicioId,
                        principalTable: "fit_exercicio",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fit_ficha_item_fit_ficha_FichaId",
                        column: x => x.FichaId,
                        principalTable: "fit_ficha",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fit_sessao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FichaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Observacao = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fit_sessao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fit_sessao_fit_ficha_FichaId",
                        column: x => x.FichaId,
                        principalTable: "fit_ficha",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "fit_sessao_exercicio",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessaoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExercicioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false),
                    Series = table.Column<int>(type: "INTEGER", nullable: true),
                    Repeticoes = table.Column<int>(type: "INTEGER", nullable: true),
                    CargaKg = table.Column<decimal>(type: "TEXT", nullable: true),
                    Minutos = table.Column<int>(type: "INTEGER", nullable: true),
                    DistanciaKm = table.Column<decimal>(type: "TEXT", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fit_sessao_exercicio", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fit_sessao_exercicio_fit_exercicio_ExercicioId",
                        column: x => x.ExercicioId,
                        principalTable: "fit_exercicio",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fit_sessao_exercicio_fit_sessao_SessaoId",
                        column: x => x.SessaoId,
                        principalTable: "fit_sessao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fit_alimento_CodigoTaco",
                table: "fit_alimento",
                column: "CodigoTaco",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fit_alimento_Nome",
                table: "fit_alimento",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_fit_consumo_AlimentoId",
                table: "fit_consumo",
                column: "AlimentoId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_consumo_Data",
                table: "fit_consumo",
                column: "Data");

            migrationBuilder.CreateIndex(
                name: "IX_fit_exercicio_Nome",
                table: "fit_exercicio",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fit_ficha_item_ExercicioId",
                table: "fit_ficha_item",
                column: "ExercicioId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_ficha_item_FichaId",
                table: "fit_ficha_item",
                column: "FichaId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_peso_Data",
                table: "fit_peso",
                column: "Data",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fit_plano_item_AlimentoId",
                table: "fit_plano_item",
                column: "AlimentoId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_plano_item_Refeicao",
                table: "fit_plano_item",
                column: "Refeicao");

            migrationBuilder.CreateIndex(
                name: "IX_fit_sessao_Data",
                table: "fit_sessao",
                column: "Data");

            migrationBuilder.CreateIndex(
                name: "IX_fit_sessao_FichaId",
                table: "fit_sessao",
                column: "FichaId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_sessao_exercicio_ExercicioId",
                table: "fit_sessao_exercicio",
                column: "ExercicioId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_sessao_exercicio_SessaoId",
                table: "fit_sessao_exercicio",
                column: "SessaoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fit_consumo");

            migrationBuilder.DropTable(
                name: "fit_ficha_item");

            migrationBuilder.DropTable(
                name: "fit_peso");

            migrationBuilder.DropTable(
                name: "fit_plano_item");

            migrationBuilder.DropTable(
                name: "fit_sessao_exercicio");

            migrationBuilder.DropTable(
                name: "fit_alimento");

            migrationBuilder.DropTable(
                name: "fit_exercicio");

            migrationBuilder.DropTable(
                name: "fit_sessao");

            migrationBuilder.DropTable(
                name: "fit_ficha");
        }
    }
}
