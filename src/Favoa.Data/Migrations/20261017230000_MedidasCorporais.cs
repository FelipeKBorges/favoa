using Favoa.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <summary>Medidas do corpo (cintura, quadril...). Só uma tabela nova.</summary>
    [DbContext(typeof(FavoaDbContext))]
    [Migration("20261017230000_MedidasCorporais")]
    public partial class MedidasCorporais : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fit_medida",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PessoaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Cm = table.Column<decimal>(type: "TEXT", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fit_medida", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fit_medida_PessoaId_Tipo_Data",
                table: "fit_medida",
                columns: new[] { "PessoaId", "Tipo", "Data" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "fit_medida");
        }
    }
}
