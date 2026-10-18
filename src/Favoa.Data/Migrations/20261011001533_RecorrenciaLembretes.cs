using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecorrenciaLembretes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecorrenciaId",
                table: "fin_lancamento",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OrcamentoCentavos",
                table: "fin_categoria",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cfg_push_assinatura",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    P256dh = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Aparelho = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cfg_push_assinatura", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fin_recorrencia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    CategoriaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiaDoMes = table.Column<int>(type: "INTEGER", nullable: false),
                    Inicio = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    UltimaOcorrencia = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Ativa = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fin_recorrencia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fin_recorrencia_fin_categoria_CategoriaId",
                        column: x => x.CategoriaId,
                        principalTable: "fin_categoria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fin_recorrencia_fin_conta_ContaId",
                        column: x => x.ContaId,
                        principalTable: "fin_conta",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rot_lembrete",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Titulo = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Hora = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    Dias = table.Column<int>(type: "INTEGER", nullable: false),
                    Ativo = table.Column<bool>(type: "INTEGER", nullable: false),
                    UltimoDisparo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rot_lembrete", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fin_lancamento_RecorrenciaId",
                table: "fin_lancamento",
                column: "RecorrenciaId");

            migrationBuilder.CreateIndex(
                name: "IX_cfg_push_assinatura_Endpoint",
                table: "cfg_push_assinatura",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fin_recorrencia_CategoriaId",
                table: "fin_recorrencia",
                column: "CategoriaId");

            migrationBuilder.CreateIndex(
                name: "IX_fin_recorrencia_ContaId",
                table: "fin_recorrencia",
                column: "ContaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropTable(
                name: "cfg_push_assinatura");

            migrationBuilder.DropTable(
                name: "fin_recorrencia");

            migrationBuilder.DropTable(
                name: "rot_lembrete");

            migrationBuilder.DropIndex(
                name: "IX_fin_lancamento_RecorrenciaId",
                table: "fin_lancamento");

            migrationBuilder.DropColumn(
                name: "RecorrenciaId",
                table: "fin_lancamento");

            migrationBuilder.DropColumn(
                name: "OrcamentoCentavos",
                table: "fin_categoria");
        }
    }
}
