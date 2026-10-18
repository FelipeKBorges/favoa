using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <inheritdoc />
    public partial class Pessoas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_rot_habito_Nome",
                table: "rot_habito");

            migrationBuilder.DropIndex(
                name: "IX_fit_peso_Data",
                table: "fit_peso");

            migrationBuilder.AddColumn<Guid>(
                name: "PessoaId",
                table: "rot_lembrete",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PessoaId",
                table: "rot_habito",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PessoaId",
                table: "rot_agua",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PessoaId",
                table: "fit_sessao",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PessoaId",
                table: "fit_plano_item",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PessoaId",
                table: "fit_peso",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PessoaId",
                table: "fit_ficha",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PessoaId",
                table: "fit_consumo",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PessoaId",
                table: "fin_lancamento",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PessoaId",
                table: "cfg_push_assinatura",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cfg_pessoa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Cor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false),
                    PinHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    VersaoAcesso = table.Column<int>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cfg_pessoa", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rot_lembrete_PessoaId",
                table: "rot_lembrete",
                column: "PessoaId");

            migrationBuilder.CreateIndex(
                name: "IX_rot_habito_PessoaId",
                table: "rot_habito",
                column: "PessoaId");

            migrationBuilder.CreateIndex(
                name: "IX_rot_habito_PessoaId_Nome",
                table: "rot_habito",
                columns: new[] { "PessoaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rot_agua_PessoaId",
                table: "rot_agua",
                column: "PessoaId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_sessao_PessoaId",
                table: "fit_sessao",
                column: "PessoaId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_plano_item_PessoaId",
                table: "fit_plano_item",
                column: "PessoaId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_peso_PessoaId_Data",
                table: "fit_peso",
                columns: new[] { "PessoaId", "Data" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fit_ficha_PessoaId",
                table: "fit_ficha",
                column: "PessoaId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_consumo_PessoaId",
                table: "fit_consumo",
                column: "PessoaId");

            migrationBuilder.CreateIndex(
                name: "IX_fin_lancamento_PessoaId",
                table: "fin_lancamento",
                column: "PessoaId");

            migrationBuilder.CreateIndex(
                name: "IX_cfg_push_assinatura_PessoaId",
                table: "cfg_push_assinatura",
                column: "PessoaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cfg_pessoa");

            migrationBuilder.DropIndex(
                name: "IX_rot_lembrete_PessoaId",
                table: "rot_lembrete");

            migrationBuilder.DropIndex(
                name: "IX_rot_habito_PessoaId",
                table: "rot_habito");

            migrationBuilder.DropIndex(
                name: "IX_rot_habito_PessoaId_Nome",
                table: "rot_habito");

            migrationBuilder.DropIndex(
                name: "IX_rot_agua_PessoaId",
                table: "rot_agua");

            migrationBuilder.DropIndex(
                name: "IX_fit_sessao_PessoaId",
                table: "fit_sessao");

            migrationBuilder.DropIndex(
                name: "IX_fit_plano_item_PessoaId",
                table: "fit_plano_item");

            migrationBuilder.DropIndex(
                name: "IX_fit_peso_PessoaId_Data",
                table: "fit_peso");

            migrationBuilder.DropIndex(
                name: "IX_fit_ficha_PessoaId",
                table: "fit_ficha");

            migrationBuilder.DropIndex(
                name: "IX_fit_consumo_PessoaId",
                table: "fit_consumo");

            migrationBuilder.DropIndex(
                name: "IX_fin_lancamento_PessoaId",
                table: "fin_lancamento");

            migrationBuilder.DropIndex(
                name: "IX_cfg_push_assinatura_PessoaId",
                table: "cfg_push_assinatura");

            migrationBuilder.DropColumn(
                name: "PessoaId",
                table: "rot_lembrete");

            migrationBuilder.DropColumn(
                name: "PessoaId",
                table: "rot_habito");

            migrationBuilder.DropColumn(
                name: "PessoaId",
                table: "rot_agua");

            migrationBuilder.DropColumn(
                name: "PessoaId",
                table: "fit_sessao");

            migrationBuilder.DropColumn(
                name: "PessoaId",
                table: "fit_plano_item");

            migrationBuilder.DropColumn(
                name: "PessoaId",
                table: "fit_peso");

            migrationBuilder.DropColumn(
                name: "PessoaId",
                table: "fit_ficha");

            migrationBuilder.DropColumn(
                name: "PessoaId",
                table: "fit_consumo");

            migrationBuilder.DropColumn(
                name: "PessoaId",
                table: "fin_lancamento");

            migrationBuilder.DropColumn(
                name: "PessoaId",
                table: "cfg_push_assinatura");

            migrationBuilder.CreateIndex(
                name: "IX_rot_habito_Nome",
                table: "rot_habito",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fit_peso_Data",
                table: "fit_peso",
                column: "Data",
                unique: true);
        }
    }
}
