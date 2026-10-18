using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlimentosFontes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoExterno",
                table: "fit_alimento",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fonte",
                table: "fit_alimento",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fit_alimento_Fonte_CodigoExterno",
                table: "fit_alimento",
                columns: new[] { "Fonte", "CodigoExterno" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fit_alimento_Fonte_CodigoExterno",
                table: "fit_alimento");

            migrationBuilder.DropColumn(
                name: "CodigoExterno",
                table: "fit_alimento");

            migrationBuilder.DropColumn(
                name: "Fonte",
                table: "fit_alimento");
        }
    }
}
