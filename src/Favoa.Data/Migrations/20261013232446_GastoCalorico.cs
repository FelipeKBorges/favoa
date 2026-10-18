using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <inheritdoc />
    public partial class GastoCalorico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Intensidade",
                table: "fit_sessao_exercicio",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IntensidadeForca",
                table: "fit_sessao",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinutosForca",
                table: "fit_sessao",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Intensidade",
                table: "fit_sessao_exercicio");

            migrationBuilder.DropColumn(
                name: "IntensidadeForca",
                table: "fit_sessao");

            migrationBuilder.DropColumn(
                name: "MinutosForca",
                table: "fit_sessao");
        }
    }
}
