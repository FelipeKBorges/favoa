using Favoa.Core;
using Favoa.Core.Acesso;
using Favoa.Core.Captura;
using Favoa.Core.Fitness;
using Favoa.Core.Pessoas;
using Favoa.Core.Projetos;
using Favoa.Core.Rotina;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

/// <summary>Auditoria do isolamento entre casas: o que cada família grava não aparece para a outra, em nenhuma tabela.</summary>
public sealed class IsolamentoTests : IAsyncLifetime
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
        await _sp.PrepararBancoAsync();
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
    private Task<FavoaDbContext> Abrir(SessaoUsuario s) => _fabrica.AbrirAsync(Eu(s));

    /// <summary>Tabelas sem filtro de propósito: o acesso (famílias, usuários, convites) e a chave-valor, cujas chaves levam o id da casa ou da pessoa.</summary>
    private static readonly HashSet<string> SemFiltro = ["Familia", "Usuario", "Convite", "Configuracao"];

    [Fact]
    public async Task Toda_tabela_de_dados_tem_filtro_de_familia_ou_pessoa()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var sem = db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.ClrType.Name != "HistoryRow" && t.GetDeclaredQueryFilters().Count == 0 && !SemFiltro.Contains(t.ClrType.Name))
            .Select(t => t.ClrType.Name).OrderBy(n => n).ToList();
        Assert.True(sem.Count == 0, "Entidades sem filtro de tenant (adicione o filtro ou, se for de propósito, a lista SemFiltro): " + string.Join(", ", sem));
    }

    [Fact]
    public async Task Toda_tabela_de_dados_tem_FamiliaId_e_indice()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var sem = db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.ClrType.Name != "HistoryRow" && !SemFiltro.Contains(t.ClrType.Name))
            .Where(t => t.FindProperty("FamiliaId") is null
                        || (t.ClrType.Name != "Pessoa" && !t.GetIndexes().Any(i => i.Properties.Count == 1 && i.Properties[0].Name == "FamiliaId")))
            .Select(t => t.ClrType.Name).OrderBy(n => n).ToList();
        Assert.True(sem.Count == 0, "Entidades sem FamiliaId indexado: " + string.Join(", ", sem));
    }

    [Fact]
    public async Task Depois_dos_fluxos_normais_nenhum_dado_da_casa_fica_sem_FamiliaId_e_toda_chave_tem_dono()
    {
        await PopularAna();
        await using var db = await _fabrica.CreateDbContextAsync();
        var nulos = new List<string>();
        foreach (var t in db.Model.GetEntityTypes().Where(t => typeof(IDaFamilia).IsAssignableFrom(t.ClrType) && t.ClrType.Name != "Alimento" && t.ClrType.Name != "PorcaoAlimento"))
        {
            var n = (int)typeof(IsolamentoTests).GetMethod(nameof(ContarSemFamilia), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(t.ClrType).Invoke(null, [db])!;
            if (n > 0) nulos.Add($"{t.ClrType.Name}={n}");
        }
        Assert.True(nulos.Count == 0, "Linhas sem FamiliaId: " + string.Join(", ", nulos));

        var chaves = await db.Configuracoes.Select(c => c.Chave).ToListAsync();
        var soltas = chaves.Where(c => !c.Contains('#') && !c.StartsWith("acesso_") && !c.StartsWith("push_")).ToList();
        Assert.True(soltas.Count == 0, "Chaves de configuração sem dono (casa/pessoa): " + string.Join(", ", soltas));
    }

    private static int ContarSemFamilia<T>(FavoaDbContext db) where T : class, IDaFamilia =>
        db.Set<T>().IgnoreQueryFilters().Count(x => x.FamiliaId == null);

    private async Task PopularAna()
    {
        await using var db = await Abrir(_ana);
        var projeto = new Projeto { Nome = "Viagem da Ana" };
        db.Projetos.Add(projeto);
        db.ItensProjeto.Add(new ItemProjeto { ProjetoId = projeto.Id, Texto = "Passagem", Tipo = TipoItemProjeto.Checklist });
        var tarefa = new Tarefa { Titulo = "Pagar luz" };
        db.Tarefas.Add(tarefa);
        db.ResponsaveisTarefa.Add(new TarefaResponsavel { TarefaId = tarefa.Id, PessoaId = _ana.PessoaId });
        var ficha = new FichaTreino { Nome = "A" };
        var exercicio = new Exercicio { Nome = "Supino" };
        db.Exercicios.Add(exercicio);
        ficha.Itens.Add(new ItemFicha { ExercicioId = exercicio.Id });
        db.Fichas.Add(ficha);
        db.Habitos.Add(new Habito { Nome = "Ler" });
        db.RegistrosAgua.Add(new RegistroAgua { Mililitros = 250, Data = Hoje });
        db.Pesos.Add(new RegistroPeso { Kg = 70, Data = Hoje });
        await db.SaveChangesAsync();

        var servico = new FavoaServico(_fabrica, Eu(_ana));
        foreach (var frase in new[] { "gastei 40 no mercado", "orçamento 3000" })
        {
            var r = await servico.RegistrarAsync(InterpretadorFrase.Interpretar(frase, await servico.ObterContextoAsync(Hoje)));
            Assert.True(r.Sucesso, r.Mensagem);
        }
    }

    [Fact]
    public async Task Nada_do_que_a_Ana_grava_aparece_para_a_Bia()
    {
        await PopularAna();
        await using (var a = await Abrir(_ana))
        {
            Assert.Equal(1, await a.ItensProjeto.CountAsync());
            Assert.Equal(1, await a.ResponsaveisTarefa.CountAsync());
            Assert.Equal(1, await a.ItensFicha.CountAsync());
        }

        await using var b = await Abrir(_bia);
        Assert.Empty(await b.Projetos.ToListAsync());
        Assert.Empty(await b.ItensProjeto.ToListAsync());
        Assert.Empty(await b.Tarefas.ToListAsync());
        Assert.Empty(await b.ResponsaveisTarefa.ToListAsync());
        Assert.Empty(await b.Fichas.ToListAsync());
        Assert.Empty(await b.ItensFicha.ToListAsync());
        Assert.Empty(await b.Exercicios.ToListAsync());
        Assert.Empty(await b.Habitos.ToListAsync());
        Assert.Empty(await b.RegistrosAgua.ToListAsync());
        Assert.Empty(await b.Pesos.ToListAsync());
        Assert.Empty(await b.Lancamentos.ToListAsync());
        Assert.DoesNotContain(await b.Pessoas.ToListAsync(), p => p.Id == _ana.PessoaId);
    }

    [Fact]
    public async Task O_orcamento_do_mes_e_de_cada_casa()
    {
        await PopularAna();
        var bia = new FavoaServico(_fabrica, Eu(_bia));
        var ana = new FavoaServico(_fabrica, Eu(_ana));
        Assert.Equal(0, (await bia.ObterPainelAsync(Hoje)).Resumo.OrcamentoMensalCentavos);

        var r = await bia.RegistrarAsync(InterpretadorFrase.Interpretar("orçamento 1500", await bia.ObterContextoAsync(Hoje)));
        Assert.True(r.Sucesso, r.Mensagem);
        Assert.Equal(150000, (await bia.ObterPainelAsync(Hoje)).Resumo.OrcamentoMensalCentavos);
        Assert.Equal(300000, (await ana.ObterPainelAsync(Hoje)).Resumo.OrcamentoMensalCentavos); // o da Ana não mudou
    }

    [Fact]
    public async Task Item_de_projeto_de_outra_casa_nao_se_altera_pelo_id()
    {
        await PopularAna();
        Guid itemId;
        await using (var a = await Abrir(_ana)) itemId = (await a.ItensProjeto.SingleAsync()).Id;
        await using var b = await Abrir(_bia);
        Assert.Null(await b.ItensProjeto.FirstOrDefaultAsync(i => i.Id == itemId));
        Assert.Equal(0, await b.ItensProjeto.Where(i => i.Id == itemId).ExecuteDeleteAsync());
        await using var a2 = await Abrir(_ana);
        Assert.Equal(1, await a2.ItensProjeto.CountAsync());
    }

    [Fact]
    public async Task Orcamento_de_antes_das_familias_passa_para_a_familia_mais_antiga()
    {
        await using (var db = await _fabrica.CreateDbContextAsync())
        {
            db.Configuracoes.Add(new Configuracao { Chave = Configuracao.OrcamentoMensalCentavos, Valor = "250000" });
            await db.SaveChangesAsync();
        }
        await _sp.PrepararBancoAsync(); // ao subir o app, a chave antiga vai para a primeira casa

        var ana = new FavoaServico(_fabrica, Eu(_ana));
        var bia = new FavoaServico(_fabrica, Eu(_bia));
        Assert.Equal(250000, (await ana.ObterPainelAsync(Hoje)).Resumo.OrcamentoMensalCentavos);
        Assert.Equal(0, (await bia.ObterPainelAsync(Hoje)).Resumo.OrcamentoMensalCentavos);
        await using var verifica = await _fabrica.CreateDbContextAsync();
        Assert.Null(await verifica.Configuracoes.FindAsync([Configuracao.OrcamentoMensalCentavos]));
    }

    [Fact]
    public async Task Senha_temporaria_troca_a_senha_e_derruba_os_aparelhos()
    {
        var acesso = new AcessoServico(_fabrica);
        var contas = await acesso.ListarContasAsync();
        Assert.Equal(["Casa da Ana", "Casa da Bia"], contas.Select(c => c.Casa).ToArray());
        Assert.All(contas, c => Assert.True(c.Dono));

        var antes = (await acesso.ObterSessaoAsync(_bia.UsuarioId))!.Versao;
        var r = await acesso.GerarSenhaTemporariaAsync(_bia.UsuarioId);
        Assert.True(r.Sucesso);
        Assert.Matches(@"^[a-z2-9]{4}-[a-z2-9]{4}-[a-z2-9]{4}$", r.Senha!);

        Assert.False((await acesso.EntrarAsync("bia@exemplo.com", CasaDeTeste.Senha)).Sucesso); // a antiga não vale mais
        Assert.True((await acesso.EntrarAsync("bia@exemplo.com", r.Senha!)).Sucesso);
        Assert.True((await acesso.ObterSessaoAsync(_bia.UsuarioId))!.Versao > antes); // cookies antigos caem
        Assert.True((await acesso.EntrarAsync("ana@exemplo.com", CasaDeTeste.Senha)).Sucesso); // a outra conta não mexeu

        Assert.False((await acesso.GerarSenhaTemporariaAsync(Guid.NewGuid())).Sucesso);
    }
}
