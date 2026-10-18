using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <inheritdoc />
    public partial class LembreteStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UltimoEnvioEm",
                table: "rot_lembrete",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UltimoResultado",
                table: "rot_lembrete",
                type: "TEXT",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UltimoEnvioEm",
                table: "rot_lembrete");

            migrationBuilder.DropColumn(
                name: "UltimoResultado",
                table: "rot_lembrete");
        }
    }
}
