using System;
using Favoa.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <summary>Diário pessoal: uma entrada por pessoa por dia, com texto e tags cifrados.</summary>
    [DbContext(typeof(FavoaDbContext))]
    [Migration("20261025120000_Diario")]
    public partial class Diario : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dia_entrada",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FamiliaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PessoaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Data = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Humor = table.Column<int>(type: "INTEGER", nullable: true),
                    Energia = table.Column<int>(type: "INTEGER", nullable: true),
                    Texto = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_dia_entrada", x => x.Id));

            migrationBuilder.CreateIndex(name: "IX_dia_entrada_FamiliaId", table: "dia_entrada", column: "FamiliaId");
            migrationBuilder.CreateIndex(name: "IX_dia_entrada_PessoaId_Data", table: "dia_entrada", columns: new[] { "PessoaId", "Data" }, unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "dia_entrada");
    }
}
