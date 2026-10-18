using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <inheritdoc />
    public partial class TarefaHorarioResponsaveis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AvisoEnviadoEm",
                table: "rot_tarefa",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AvisoMinutos",
                table: "rot_tarefa",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "Hora",
                table: "rot_tarefa",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "rot_tarefa_responsavel",
                columns: table => new
                {
                    TarefaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PessoaId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rot_tarefa_responsavel", x => new { x.TarefaId, x.PessoaId });
                    table.ForeignKey(
                        name: "FK_rot_tarefa_responsavel_rot_tarefa_TarefaId",
                        column: x => x.TarefaId,
                        principalTable: "rot_tarefa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rot_tarefa_responsavel_PessoaId",
                table: "rot_tarefa_responsavel",
                column: "PessoaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rot_tarefa_responsavel");

            migrationBuilder.DropColumn(
                name: "AvisoEnviadoEm",
                table: "rot_tarefa");

            migrationBuilder.DropColumn(
                name: "AvisoMinutos",
                table: "rot_tarefa");

            migrationBuilder.DropColumn(
                name: "Hora",
                table: "rot_tarefa");
        }
    }
}
