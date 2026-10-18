using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cfg_configuracao",
                columns: table => new
                {
                    Chave = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Valor = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cfg_configuracao", x => x.Chave);
                });

            migrationBuilder.CreateTable(
                name: "fin_categoria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fin_categoria", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fin_conta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Ativa = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fin_conta", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rot_agua",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mililitros = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rot_agua", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rot_habito",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Ativo = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rot_habito", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rot_tarefa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Titulo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Prazo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ConcluidaEm = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rot_tarefa", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fin_lancamento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoriaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fin_lancamento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fin_lancamento_fin_categoria_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "fin_categoria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fin_lancamento_fin_conta_ContaId",
                        column: x => x.ContaId,
                        principalTable: "fin_conta",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rot_habito_registro",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HabitoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rot_habito_registro", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rot_habito_registro_rot_habito_HabitoId",
                        column: x => x.HabitoId,
                        principalTable: "rot_habito",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fin_categoria_Nome_Tipo",
                table: "fin_categoria",
                columns: new[] { "Nome", "Tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fin_conta_Nome",
                table: "fin_conta",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fin_lancamento_CategoriaId",
                table: "fin_lancamento",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_fin_lancamento_ContaId",
                table: "fin_lancamento",
                column: "ContaId");

            migrationBuilder.CreateIndex(
                name: "IX_fin_lancamento_Data",
                table: "fin_lancamento",
                column: "Data");

            migrationBuilder.CreateIndex(
                name: "IX_rot_agua_Data",
                table: "rot_agua",
                column: "Data");

            migrationBuilder.CreateIndex(
                name: "IX_rot_habito_Nome",
                table: "rot_habito",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rot_habito_registro_HabitoId_Data",
                table: "rot_habito_registro",
                columns: new[] { "HabitoId", "Data" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rot_tarefa_Prazo",
                table: "rot_tarefa",
                column: "Prazo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cfg_configuracao");

            migrationBuilder.DropTable(
                name: "fin_lancamento");

            migrationBuilder.DropTable(
                name: "rot_agua");

            migrationBuilder.DropTable(
                name: "rot_habito_registro");

            migrationBuilder.DropTable(
                name: "rot_tarefa");

            migrationBuilder.DropTable(
                name: "fin_categoria");

            migrationBuilder.DropTable(
                name: "fin_conta");

            migrationBuilder.DropTable(
                name: "rot_habito");
        }
    }
}
