using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Favoa.Data.Migrations
{
    /// <inheritdoc />
    public partial class FamiliasEAcesso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fit_exercicio_Nome",
                table: "fit_exercicio");

            migrationBuilder.DropIndex(
                name: "IX_fit_alimento_Fonte_CodigoExterno",
                table: "fit_alimento");

            migrationBuilder.DropIndex(
                name: "IX_fin_conta_Nome",
                table: "fin_conta");

            migrationBuilder.DropIndex(
                name: "IX_fin_categoria_Nome_Tipo",
                table: "fin_categoria");

            migrationBuilder.AddColumn<Guid>(
                name: "FamiliaId",
                table: "rot_tarefa",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamiliaId",
                table: "prj_projeto",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamiliaId",
                table: "fit_exercicio",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamiliaId",
                table: "fit_alimento",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamiliaId",
                table: "fin_recorrencia",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamiliaId",
                table: "fin_lancamento",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamiliaId",
                table: "fin_conta",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamiliaId",
                table: "fin_categoria",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamiliaId",
                table: "cfg_pessoa",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cfg_convite",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FamiliaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PessoaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodigoHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsadoEm = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cfg_convite", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cfg_familia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cfg_familia", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cfg_usuario",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FamiliaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PessoaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    EmailNormalizado = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    SenhaHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Dono = table.Column<bool>(type: "INTEGER", nullable: false),
                    VersaoAcesso = table.Column<int>(type: "INTEGER", nullable: false),
                    FalhasSeguidas = table.Column<int>(type: "INTEGER", nullable: false),
                    Bloqueios = table.Column<int>(type: "INTEGER", nullable: false),
                    BloqueadoAte = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UltimoAcessoEm = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cfg_usuario", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rot_tarefa_FamiliaId",
                table: "rot_tarefa",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_prj_projeto_FamiliaId",
                table: "prj_projeto",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_exercicio_FamiliaId",
                table: "fit_exercicio",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_exercicio_FamiliaId_Nome",
                table: "fit_exercicio",
                columns: new[] { "FamiliaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fit_alimento_FamiliaId",
                table: "fit_alimento",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_fit_alimento_FamiliaId_Fonte_CodigoExterno",
                table: "fit_alimento",
                columns: new[] { "FamiliaId", "Fonte", "CodigoExterno" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fin_recorrencia_FamiliaId",
                table: "fin_recorrencia",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_fin_lancamento_FamiliaId",
                table: "fin_lancamento",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_fin_conta_FamiliaId",
                table: "fin_conta",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_fin_conta_FamiliaId_Nome",
                table: "fin_conta",
                columns: new[] { "FamiliaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fin_categoria_FamiliaId",
                table: "fin_categoria",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_fin_categoria_FamiliaId_Nome_Tipo",
                table: "fin_categoria",
                columns: new[] { "FamiliaId", "Nome", "Tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cfg_pessoa_FamiliaId",
                table: "cfg_pessoa",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_cfg_convite_CodigoHash",
                table: "cfg_convite",
                column: "CodigoHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cfg_convite_FamiliaId",
                table: "cfg_convite",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_cfg_usuario_EmailNormalizado",
                table: "cfg_usuario",
                column: "EmailNormalizado",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cfg_usuario_FamiliaId",
                table: "cfg_usuario",
                column: "FamiliaId");

            migrationBuilder.CreateIndex(
                name: "IX_cfg_usuario_PessoaId",
                table: "cfg_usuario",
                column: "PessoaId",
                unique: true);

            // Quem já usava o Fávoa vira a primeira família: pessoas, finanças, tarefas, projetos e exercícios passam a ser dela.
            // Alimentos da TACO e do IBGE ficam sem família (catálogo comum); os cadastrados à mão ou pela internet são dela.
            // Em base nova (sem pessoas) nada acontece: o primeiro acesso cria a família.
            var familiaId = Guid.NewGuid().ToString().ToUpperInvariant();
            migrationBuilder.Sql($"INSERT INTO cfg_familia (Id, Nome, CriadoEm) SELECT '{familiaId}', 'Casa de ' || Nome, strftime('%Y-%m-%d %H:%M:%f', 'now') FROM cfg_pessoa ORDER BY Ordem, CriadoEm LIMIT 1;");
            foreach (var tabela in new[] { "cfg_pessoa", "fin_conta", "fin_categoria", "fin_lancamento", "fin_recorrencia", "rot_tarefa", "prj_projeto", "fit_exercicio" })
                migrationBuilder.Sql($"UPDATE {tabela} SET FamiliaId = '{familiaId}' WHERE EXISTS (SELECT 1 FROM cfg_familia);");
            migrationBuilder.Sql($"UPDATE fit_alimento SET FamiliaId = '{familiaId}' WHERE EXISTS (SELECT 1 FROM cfg_familia) AND CodigoTaco IS NULL AND (Fonte IS NULL OR Fonte <> 'IBGE');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cfg_convite");

            migrationBuilder.DropTable(
                name: "cfg_familia");

            migrationBuilder.DropTable(
                name: "cfg_usuario");

            migrationBuilder.DropIndex(
                name: "IX_rot_tarefa_FamiliaId",
                table: "rot_tarefa");

            migrationBuilder.DropIndex(
                name: "IX_prj_projeto_FamiliaId",
                table: "prj_projeto");

            migrationBuilder.DropIndex(
                name: "IX_fit_exercicio_FamiliaId",
                table: "fit_exercicio");

            migrationBuilder.DropIndex(
                name: "IX_fit_exercicio_FamiliaId_Nome",
                table: "fit_exercicio");

            migrationBuilder.DropIndex(
                name: "IX_fit_alimento_FamiliaId",
                table: "fit_alimento");

            migrationBuilder.DropIndex(
                name: "IX_fit_alimento_FamiliaId_Fonte_CodigoExterno",
                table: "fit_alimento");

            migrationBuilder.DropIndex(
                name: "IX_fin_recorrencia_FamiliaId",
                table: "fin_recorrencia");

            migrationBuilder.DropIndex(
                name: "IX_fin_lancamento_FamiliaId",
                table: "fin_lancamento");

            migrationBuilder.DropIndex(
                name: "IX_fin_conta_FamiliaId",
                table: "fin_conta");

            migrationBuilder.DropIndex(
                name: "IX_fin_conta_FamiliaId_Nome",
                table: "fin_conta");

            migrationBuilder.DropIndex(
                name: "IX_fin_categoria_FamiliaId",
                table: "fin_categoria");

            migrationBuilder.DropIndex(
                name: "IX_fin_categoria_FamiliaId_Nome_Tipo",
                table: "fin_categoria");

            migrationBuilder.DropIndex(
                name: "IX_cfg_pessoa_FamiliaId",
                table: "cfg_pessoa");

            migrationBuilder.DropColumn(
                name: "FamiliaId",
                table: "rot_tarefa");

            migrationBuilder.DropColumn(
                name: "FamiliaId",
                table: "prj_projeto");

            migrationBuilder.DropColumn(
                name: "FamiliaId",
                table: "fit_exercicio");

            migrationBuilder.DropColumn(
                name: "FamiliaId",
                table: "fit_alimento");

            migrationBuilder.DropColumn(
                name: "FamiliaId",
                table: "fin_recorrencia");

            migrationBuilder.DropColumn(
                name: "FamiliaId",
                table: "fin_lancamento");

            migrationBuilder.DropColumn(
                name: "FamiliaId",
                table: "fin_conta");

            migrationBuilder.DropColumn(
                name: "FamiliaId",
                table: "fin_categoria");

            migrationBuilder.DropColumn(
                name: "FamiliaId",
                table: "cfg_pessoa");

            migrationBuilder.CreateIndex(
                name: "IX_fit_exercicio_Nome",
                table: "fit_exercicio",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fit_alimento_Fonte_CodigoExterno",
                table: "fit_alimento",
                columns: new[] { "Fonte", "CodigoExterno" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fin_conta_Nome",
                table: "fin_conta",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fin_categoria_Nome_Tipo",
                table: "fin_categoria",
                columns: new[] { "Nome", "Tipo" },
                unique: true);
        }
    }
}
