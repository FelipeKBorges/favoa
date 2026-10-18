using Favoa.Core;
using Favoa.Core.Acesso;
using Favoa.Core.Captura;
using Favoa.Core.Financas;
using Favoa.Core.Fitness;
using Favoa.Core.Pessoas;
using Favoa.Core.Projetos;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

/// <summary>Cada família só enxerga o que é dela: finanças, tarefas, projetos, exercícios, alimentos próprios e as pessoas.</summary>
public sealed class FamiliasTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = new(2026, 10, 18);
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private IDbContextFactory<FavoaDbContext> _fabrica = null!;
    private SessaoUsuario _ana = null!, _bia = null!;

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao)).BuildServiceProvider();
        await _sp.PrepararBancoAsync(); // base nova: sem família nenhuma
        _fabrica = _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        var acesso = new AcessoServico(_fabrica);
        _ana = (await acesso.CriarFamiliaAsync("Casa da Ana", "Ana", "ana@exemplo.com", CasaDeTeste.Senha)).Sessao!;
        _bia = (await acesso.CriarFamiliaAsync("Casa da Bia", "Bia", "bia@exemplo.com", CasaDeTeste.Senha)).Sessao!;
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private static PessoaFixa Eu(SessaoUsuario s) => new(s.PessoaId, s.FamiliaId);
    private FavoaServico ServicoFavoa(SessaoUsuario s) => new(_fabrica, Eu(s));
    private FitnessServico Fitness(SessaoUsuario s) => new(_fabrica, Eu(s));
    private CadastrosServico Cadastros(SessaoUsuario s) => new(_fabrica, Eu(s));
    private ProjetosServico Projetos(SessaoUsuario s) => new(_fabrica, Eu(s));

    private async Task Registrar(SessaoUsuario s, string frase)
    {
        var servico = ServicoFavoa(s);
        var r = await servico.RegistrarAsync(InterpretadorFrase.Interpretar(frase, await servico.ObterContextoAsync(Hoje)));
        Assert.True(r.Sucesso, r.Mensagem);
    }

    private Task<FavoaDbContext> Abrir(SessaoUsuario s) => _fabrica.AbrirAsync(Eu(s));

    [Fact]
    public async Task Cada_familia_nasce_com_as_proprias_categorias_e_conta_mesmo_com_nomes_iguais()
    {
        await using var a = await Abrir(_ana);
        await using var b = await Abrir(_bia);
        Assert.Equal(11, await a.Categorias.CountAsync());
        Assert.Equal(11, await b.Categorias.CountAsync());
        Assert.Equal("Carteira", (await a.Contas.SingleAsync()).Nome);
        Assert.Equal("Carteira", (await b.Contas.SingleAsync()).Nome);
        Assert.Empty((await a.Categorias.Select(c => c.Id).ToListAsync()).Intersect(await b.Categorias.Select(c => c.Id).ToListAsync()));
    }

    [Fact]
    public async Task Lancamentos_tarefas_e_exercicios_ficam_na_familia_de_quem_registrou()
    {
        await Registrar(_ana, "gastei 40 no mercado");
        await Registrar(_ana, "tarefa pagar luz sexta");
        await Registrar(_ana, "supino 3x10 40kg");
        await Registrar(_bia, "gastei 15 no uber");

        await using var a = await Abrir(_ana);
        await using var b = await Abrir(_bia);
        Assert.Equal(["gastei 40 no mercado"], (await a.Lancamentos.ToListAsync()).Select(l => $"gastei {l.ValorCentavos / 100} no {l.Categoria?.Nome.ToLowerInvariant() ?? "mercado"}").ToArray());
        Assert.Equal(1, await a.Lancamentos.CountAsync());
        Assert.Equal(1, await b.Lancamentos.CountAsync());
        Assert.Equal(1, await a.Tarefas.CountAsync());
        Assert.Equal(0, await b.Tarefas.CountAsync());
        Assert.Equal(1, await a.Exercicios.CountAsync());
        Assert.Equal(0, await b.Exercicios.CountAsync());
        Assert.All(await a.Lancamentos.ToListAsync(), l => Assert.Equal(_ana.FamiliaId, l.FamiliaId));

        // Quem não tem família atual (agendador, exportação) enxerga tudo.
        await using var tudo = await _fabrica.CreateDbContextAsync();
        Assert.Equal(2, await tudo.Lancamentos.CountAsync());
    }

    [Fact]
    public async Task Pessoas_e_dados_pessoais_nao_atravessam_familias()
    {
        await Registrar(_ana, "peso 60");
        await Registrar(_ana, "bebi 500 ml");
        await using var a = await Abrir(_ana);
        await using var b = await Abrir(_bia);
        Assert.Equal(["Ana"], await a.Pessoas.Select(p => p.Nome).ToListAsync());
        Assert.Equal(["Bia"], await b.Pessoas.Select(p => p.Nome).ToListAsync());
        Assert.Equal(1, await a.Pesos.CountAsync());
        Assert.Equal(0, await b.Pesos.CountAsync());
        Assert.Equal(0, await b.RegistrosAgua.CountAsync());
    }

    [Fact]
    public async Task Alimentos_da_taco_sao_comuns_e_os_cadastrados_sao_da_casa()
    {
        var catalogoVisto = (await Fitness(_ana).BuscarAlimentosAsync("arroz")).Count;
        Assert.True(catalogoVisto > 0);
        Assert.Equal(catalogoVisto, (await Fitness(_bia).BuscarAlimentosAsync("arroz")).Count);

        var r = await Fitness(_ana).SalvarAlimentoAsync(new AlimentoEdicao(null, "Bolo secreto da vovó", 350, 5, 50, 12, null, null));
        Assert.True(r.Sucesso, r.Mensagem);
        Assert.Single(await Fitness(_ana).BuscarAlimentosAsync("bolo secreto"));
        Assert.Empty(await Fitness(_bia).BuscarAlimentosAsync("bolo secreto"));

        await using var db = await _fabrica.CreateDbContextAsync();
        Assert.Equal(_ana.FamiliaId, (await db.Alimentos.SingleAsync(x => x.Nome == "Bolo secreto da vovó")).FamiliaId);
        Assert.True(await db.Alimentos.AnyAsync(x => x.FamiliaId == null && x.CodigoTaco != null));
    }

    [Fact]
    public async Task Cadastros_projetos_e_recorrencias_sao_da_familia()
    {
        Assert.True((await Cadastros(_ana).SalvarContaAsync(null, "Nubank")).Sucesso);
        Assert.True((await Cadastros(_ana).SalvarCategoriaAsync(null, "Pets", TipoLancamento.Despesa, 20000)).Sucesso);
        var rec = await Cadastros(_ana).SalvarRecorrenciaAsync(new DadosRecorrencia(null, TipoLancamento.Despesa, "Internet", 12000, "Moradia", null, 10, Hoje), Hoje);
        Assert.True(rec.Sucesso, rec.Mensagem);
        var projeto = await Projetos(_ana).SalvarAsync(new DadosProjeto(null, "Viagem", TipoProjeto.Viagem, SituacaoProjeto.Planejando, null, null, null, null, DaCasa: true));
        Assert.True(projeto.Sucesso, projeto.Mensagem);

        var quadroAna = await Cadastros(_ana).ObterAsync(Hoje);
        var quadroBia = await Cadastros(_bia).ObterAsync(Hoje);
        Assert.Contains(quadroAna.Contas, c => c.Nome == "Nubank");
        Assert.DoesNotContain(quadroBia.Contas, c => c.Nome == "Nubank");
        Assert.Contains(quadroAna.Categorias, c => c.Nome == "Pets");
        Assert.DoesNotContain(quadroBia.Categorias, c => c.Nome == "Pets");
        Assert.Single(quadroAna.Recorrencias);
        Assert.Empty(quadroBia.Recorrencias);
        Assert.Single(await Projetos(_ana).ListarAsync(Hoje));
        Assert.Empty(await Projetos(_bia).ListarAsync(Hoje));

        // O agendador roda sem família atual: o lançamento da recorrência herda a família da recorrência.
        var agendador = new CadastrosServico(_fabrica, new PessoaFixa(null));
        Assert.Equal(1, await agendador.ProcessarRecorrenciasAsync(new DateOnly(2026, 11, 10)));
        await using var a = await Abrir(_ana);
        await using var b = await Abrir(_bia);
        Assert.Equal(1, await a.Lancamentos.CountAsync(l => l.RecorrenciaId != null));
        Assert.Equal(0, await b.Lancamentos.CountAsync());
    }

    [Fact]
    public async Task Nao_se_acessa_registro_de_outra_familia_nem_pelo_id()
    {
        await Registrar(_ana, "gastei 40 no mercado");
        Guid id;
        await using (var a = await Abrir(_ana)) id = (await a.Lancamentos.SingleAsync()).Id;

        await using var b = await Abrir(_bia);
        Assert.Null(await b.Lancamentos.FirstOrDefaultAsync(l => l.Id == id));
        var servicoBia = ServicoFavoa(_bia);
        Assert.Null(await servicoBia.ObterParaEdicaoAsync(new Favoa.Core.Edicao.ReferenciaRegistro(Favoa.Core.Edicao.TipoReferencia.Lancamento, id)));
    }
}

/// <summary>A migração que cria a primeira família a partir de quem já usava o Fávoa.</summary>
public sealed class MigracaoFamiliasTests
{
    private const string Antes = "20261017230000_MedidasCorporais";

    [Fact]
    public async Task Dados_existentes_viram_a_primeira_familia_e_o_catalogo_continua_comum()
    {
        await using var conexao = new SqliteConnection("Data Source=:memory:");
        await conexao.OpenAsync();
        var opcoes = new DbContextOptionsBuilder<FavoaDbContext>().UseSqlite(conexao).Options;
        await using var db = new FavoaDbContext(opcoes);
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db)!;

        await migrator.MigrateAsync(Antes); // esquema de antes das famílias
        const string agora = "2026-10-10 12:00:00";
        string[] sql =
        [
            $"INSERT INTO cfg_pessoa (Id, Nome, Cor, Ordem, VersaoAcesso, CriadoEm) VALUES ('11111111-1111-1111-1111-111111111111', 'Davi', '#F2A93B', 0, 0, '{agora}')",
            $"INSERT INTO cfg_pessoa (Id, Nome, Cor, Ordem, VersaoAcesso, CriadoEm) VALUES ('22222222-2222-2222-2222-222222222222', 'Eva', '#B9A3E3', 1, 0, '{agora}')",
            $"INSERT INTO fin_conta (Id, Nome, Ativa, CriadoEm) VALUES ('AAAAAAAA-0000-0000-0000-000000000001', 'Carteira', 1, '{agora}')",
            $"INSERT INTO fin_categoria (Id, Nome, Tipo, CriadoEm) VALUES ('AAAAAAAA-0000-0000-0000-000000000002', 'Mercado', 0, '{agora}')",
            $"INSERT INTO fin_lancamento (Id, ContaId, CategoriaId, Tipo, ValorCentavos, Data, Descricao, CriadoEm) VALUES ('AAAAAAAA-0000-0000-0000-000000000003', 'AAAAAAAA-0000-0000-0000-000000000001', 'AAAAAAAA-0000-0000-0000-000000000002', 0, 4000, '2026-10-10', 'feira', '{agora}')",
            $"INSERT INTO rot_tarefa (Id, Titulo, CriadoEm) VALUES ('AAAAAAAA-0000-0000-0000-000000000004', 'pagar luz', '{agora}')",
            $"INSERT INTO prj_projeto (Id, Nome, Tipo, Situacao, Descricao, CriadoEm) VALUES ('AAAAAAAA-0000-0000-0000-000000000005', 'Viagem', 0, 0, '', '{agora}')",
            $"INSERT INTO fit_exercicio (Id, Nome, Grupo, Tipo, Ativo, CriadoEm) VALUES ('AAAAAAAA-0000-0000-0000-000000000006', 'Supino', 'Peito', 0, 1, '{agora}')",
            $"INSERT INTO fit_alimento (Id, Nome, Grupo, Kcal, Proteina, Carboidrato, Gordura, Fibra, Personalizado, CriadoEm, CodigoTaco) VALUES ('BBBBBBBB-0000-0000-0000-000000000001', 'Arroz TACO', 'Cereais', 128, 2.5, 28, 0.2, 1.6, 0, '{agora}', 1)",
            $"INSERT INTO fit_alimento (Id, Nome, Grupo, Kcal, Proteina, Carboidrato, Gordura, Fibra, Personalizado, CriadoEm, Fonte, CodigoExterno) VALUES ('BBBBBBBB-0000-0000-0000-000000000002', 'Prato IBGE', 'IBGE', 100, 1, 1, 1, 1, 0, '{agora}', 'IBGE', 'i1')",
            $"INSERT INTO fit_alimento (Id, Nome, Grupo, Kcal, Proteina, Carboidrato, Gordura, Fibra, Personalizado, CriadoEm) VALUES ('BBBBBBBB-0000-0000-0000-000000000003', 'Bolo da casa', '', 350, 5, 50, 12, 1, 1, '{agora}')",
            $"INSERT INTO fit_alimento (Id, Nome, Grupo, Kcal, Proteina, Carboidrato, Gordura, Fibra, Personalizado, CriadoEm, Fonte, CodigoExterno) VALUES ('BBBBBBBB-0000-0000-0000-000000000004', 'Produto OFF', '', 200, 5, 20, 8, 1, 1, '{agora}', 'Open Food Facts', '789')",
        ];
        foreach (var comando in sql) await db.Database.ExecuteSqlRawAsync(comando);

        await migrator.MigrateAsync(); // as migrações de hoje

        var familia = await db.Familias.SingleAsync();
        Assert.Equal("Casa de Davi", familia.Nome);
        Assert.All(await db.Pessoas.IgnoreQueryFilters().ToListAsync(), p => Assert.Equal(familia.Id, p.FamiliaId));
        Assert.Equal(familia.Id, (await db.Contas.IgnoreQueryFilters().SingleAsync()).FamiliaId);
        Assert.Equal(familia.Id, (await db.Categorias.IgnoreQueryFilters().SingleAsync()).FamiliaId);
        Assert.Equal(familia.Id, (await db.Lancamentos.IgnoreQueryFilters().SingleAsync()).FamiliaId);
        Assert.Equal(familia.Id, (await db.Tarefas.IgnoreQueryFilters().SingleAsync()).FamiliaId);
        Assert.Equal(familia.Id, (await db.Projetos.IgnoreQueryFilters().SingleAsync()).FamiliaId);
        Assert.Equal(familia.Id, (await db.Exercicios.IgnoreQueryFilters().SingleAsync()).FamiliaId);

        var alimentos = await db.Alimentos.IgnoreQueryFilters().ToDictionaryAsync(a => a.Nome, a => a.FamiliaId);
        Assert.Null(alimentos["Arroz TACO"]);       // catálogo comum
        Assert.Null(alimentos["Prato IBGE"]);       // catálogo comum
        Assert.Equal(familia.Id, alimentos["Bolo da casa"]);
        Assert.Equal(familia.Id, alimentos["Produto OFF"]);
        Assert.Equal(0, await db.Usuarios.CountAsync()); // o primeiro acesso cria o login
    }

    [Fact]
    public async Task Base_nova_nao_ganha_familia_na_migracao()
    {
        await using var conexao = new SqliteConnection("Data Source=:memory:");
        await conexao.OpenAsync();
        await using var db = new FavoaDbContext(new DbContextOptionsBuilder<FavoaDbContext>().UseSqlite(conexao).Options);
        await db.Database.MigrateAsync();
        Assert.Equal(0, await db.Familias.CountAsync());
    }
}
