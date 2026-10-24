using Favoa.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <summary>Código de recuperação de senha (só o hash), para quem esquece a senha sem depender de e-mail.</summary>
    [DbContext(typeof(FavoaDbContext))]
    [Migration("20261024200000_CodigoDeRecuperacao")]
    public partial class CodigoDeRecuperacao : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.AddColumn<string>(name: "CodigoRecuperacaoHash", table: "cfg_usuario", type: "TEXT", maxLength: 200, nullable: true);

        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.DropColumn(name: "CodigoRecuperacaoHash", table: "cfg_usuario");
    }
}
