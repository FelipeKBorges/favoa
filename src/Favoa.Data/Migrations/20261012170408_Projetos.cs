using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <inheritdoc />
    public partial class Projetos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjetoId",
                table: "rot_tarefa",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjetoId",
                table: "fin_lancamento",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "prj_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjetoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Texto = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Feito = table.Column<bool>(type: "INTEGER", nullable: false),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Hora = table.Column<TimeOnly>(type: "TEXT", nullable: true),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prj_item", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "prj_projeto",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Situacao = table.Column<int>(type: "INTEGER", nullable: false),
                    Inicio = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Fim = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    OrcamentoCentavos = table.Column<long>(type: "INTEGER", nullable: true),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    PessoaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prj_projeto", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rot_tarefa_ProjetoId",
                table: "rot_tarefa",
                column: "ProjetoId");

            migrationBuilder.CreateIndex(
                name: "IX_fin_lancamento_ProjetoId",
                table: "fin_lancamento",
                column: "ProjetoId");

            migrationBuilder.CreateIndex(
                name: "IX_prj_item_ProjetoId",
                table: "prj_item",
                column: "ProjetoId");

            migrationBuilder.CreateIndex(
                name: "IX_prj_projeto_PessoaId",
                table: "prj_projeto",
                column: "PessoaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prj_item");

            migrationBuilder.DropTable(
                name: "prj_projeto");

            migrationBuilder.DropIndex(
                name: "IX_rot_tarefa_ProjetoId",
                table: "rot_tarefa");

            migrationBuilder.DropIndex(
                name: "IX_fin_lancamento_ProjetoId",
                table: "fin_lancamento");

            migrationBuilder.DropColumn(
                name: "ProjetoId",
                table: "rot_tarefa");

            migrationBuilder.DropColumn(
                name: "ProjetoId",
                table: "fin_lancamento");
        }
    }
}
