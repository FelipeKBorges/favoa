using Favoa.Core.Captura;
using Favoa.Core.Painel;
using Favoa.Core.Pessoas;
using Favoa.Core.Projetos;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

/// <summary>Projetos: viagem, trabalho, casa. Juntam tarefas, gastos, checklist e marcos.</summary>
public sealed class ProjetosTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.Now);
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private IDbContextFactory<FavoaDbContext> _fabrica = null!;
    private Guid _felipe, _gab;

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao)).BuildServiceProvider();
        await _sp.PrepararBancoComCasaAsync();
        _fabrica = _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        var acesso = new AcessoServico(_fabrica);
        await acesso.SalvarPessoaAsync(await CasaDeTeste.FamiliaIdAsync(_fabrica), null, "Eva", "#B9A3E3");
        var pessoas = (await acesso.ObterEstadoAsync(null)).Pessoas;
        _felipe = pessoas.Single(p => p.Nome == "Davi").Id;
        _gab = pessoas.Single(p => p.Nome == "Eva").Id;
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private ProjetosServico Projetos(Guid pessoa) => new(_fabrica, new PessoaFixa(pessoa));
    private FavoaServico Favoa(Guid pessoa) => new(_fabrica, new PessoaFixa(pessoa));

    private async Task<Guid> Criar(Guid pessoa, string nome, TipoProjeto tipo = TipoProjeto.Viagem, bool daCasa = true,
        DateOnly? inicio = null, DateOnly? fim = null, long? orcamento = null)
    {
        var r = await Projetos(pessoa).SalvarAsync(new DadosProjeto(null, nome, tipo, SituacaoProjeto.Planejando, inicio, fim, orcamento, null, daCasa));
        Assert.True(r.Sucesso, r.Mensagem);
        return r.Criado!.Id;
    }

    private async Task Registrar(Guid pessoa, string frase)
    {
        var s = Favoa(pessoa);
        var r = await s.RegistrarAsync(InterpretadorFrase.Interpretar(frase, await s.ObterContextoAsync(Hoje)));
        Assert.True(r.Sucesso, r.Mensagem);
    }

    [Fact]
    public async Task Viagem_nova_vem_com_checklist_do_modelo()
    {
        var id = await Criar(_felipe, "viagem chile", inicio: Hoje.AddDays(30), fim: Hoje.AddDays(37), orcamento: 600000);
        var p = await Projetos(_felipe).ObterAsync(id, Hoje);
        Assert.NotNull(p);
        Assert.Equal("Viagem chile", p.Resumo.Nome);
        Assert.Equal(TiposProjeto.Modelo(TipoProjeto.Viagem).Count, p.Itens.Count(i => i.Tipo == TipoItemProjeto.Checklist));
        Assert.Equal(30, p.Resumo.DiasAte(Hoje));
        Assert.Equal(0, p.Resumo.Progresso);
    }

    [Fact]
    public async Task Hashtag_liga_tarefa_e_gasto_ao_projeto()
    {
        var id = await Criar(_felipe, "Viagem Chile", orcamento: 600000);
        await Registrar(_felipe, "tarefa comprar adaptador #chile amanhã");
        await Registrar(_gab, "mercado 120 #chile");
        await Registrar(_felipe, "uber 23"); // sem projeto

        var p = (await Projetos(_gab).ObterAsync(id, Hoje))!;
        var tarefa = Assert.Single(p.Tarefas);
        Assert.Equal("Comprar adaptador", tarefa.Titulo);
        Assert.Equal(Hoje.AddDays(1), tarefa.Prazo);
        var gasto = Assert.Single(p.Lancamentos);
        Assert.Equal(12000, gasto.ValorCentavos);
        Assert.Equal(12000, p.Resumo.GastoCentavos);

        // O gasto continua contando no mês normalmente
        var extrato = await Favoa(_felipe).ObterExtratoAsync(global::Favoa.Core.Financas.FiltroExtrato.DoMes(Hoje), Hoje);
        Assert.Equal(14300, extrato.DespesasCentavos);
    }

    [Fact]
    public async Task Hashtag_de_projeto_inexistente_nao_salva()
    {
        var s = Favoa(_felipe);
        var i = InterpretadorFrase.Interpretar("mercado 50 #japao", await s.ObterContextoAsync(Hoje));
        Assert.Equal("japao", i.Projeto);
        Assert.Equal("mercado 50 #japao", i.TextoOriginal);
        var r = await s.RegistrarAsync(i);
        Assert.False(r.Sucesso);
        Assert.Contains("#japao", r.Mensagem);
        await using var db = await _fabrica.CreateDbContextAsync();
        Assert.Equal(0, await db.Lancamentos.CountAsync());
    }

    [Fact]
    public void Apelido_combina_com_nome_do_projeto()
    {
        Assert.Equal(3, TiposProjeto.Combina("Viagem Chile", "viagem-chile"));
        Assert.Equal(2, TiposProjeto.Combina("Viagem Chile", "chile"));
        Assert.Equal(1, TiposProjeto.Combina("Reforma da cozinha", "cozi"));
        Assert.Equal(0, TiposProjeto.Combina("Viagem Chile", "japao"));
    }

    [Fact]
    public async Task Progresso_soma_tarefas_e_checklist()
    {
        var id = await Criar(_felipe, "Site do cliente", TipoProjeto.Trabalho);
        var s = Projetos(_felipe);
        var p = (await s.ObterAsync(id, Hoje))!;
        foreach (var item in p.Itens.Take(2)) await s.AlternarItemAsync(item.Id); // 2 de 4 do modelo
        await s.CriarTarefaAsync(id, "enviar proposta", Hoje.AddDays(2));
        await s.CriarTarefaAsync(id, "reunião de alinhamento", null);
        var tarefas = (await s.ObterAsync(id, Hoje))!.Tarefas;
        await Favoa(_felipe).AlternarTarefaAsync(tarefas.First(t => t.Titulo == "Reunião de alinhamento").Id, Hoje);

        var resumo = (await s.ObterAsync(id, Hoje))!.Resumo;
        Assert.Equal(3, resumo.TarefasFeitas + resumo.ChecklistFeitos);
        Assert.Equal(6, resumo.TarefasTotal + resumo.ChecklistTotal);
        Assert.Equal(0.5, resumo.Progresso);
        Assert.StartsWith("Enviar proposta", resumo.Proximo);
    }

    [Fact]
    public async Task Projeto_pessoal_so_o_dono_ve_e_o_da_casa_os_dois_veem()
    {
        await Criar(_felipe, "Viagem Chile");
        await Criar(_felipe, "Freela do site", TipoProjeto.Trabalho, daCasa: false);

        Assert.Equal(2, (await Projetos(_felipe).ListarAsync(Hoje)).Count);
        var daGab = Assert.Single(await Projetos(_gab).ListarAsync(Hoje));
        Assert.Equal("Viagem Chile", daGab.Nome);
        Assert.True(daGab.DaCasa);

        // A Eva também não consegue lançar no projeto pessoal do Davi
        var s = Favoa(_gab);
        Assert.False((await s.RegistrarAsync(InterpretadorFrase.Interpretar("mercado 10 #freela", await s.ObterContextoAsync(Hoje)))).Sucesso);
    }

    [Fact]
    public async Task Excluir_projeto_mantem_tarefas_e_lancamentos()
    {
        var id = await Criar(_felipe, "Reforma", TipoProjeto.Casa);
        await Registrar(_felipe, "tarefa comprar tinta #reforma");
        await Registrar(_felipe, "moradia 350 #reforma");

        Assert.True((await Projetos(_felipe).ExcluirAsync(id)).Sucesso);
        await using var db = await _fabrica.CreateDbContextAsync();
        Assert.Equal(0, await db.Projetos.CountAsync());
        Assert.Equal(0, await db.ItensProjeto.CountAsync());
        Assert.Null((await db.Tarefas.SingleAsync()).ProjetoId);
        Assert.Null((await db.Lancamentos.SingleAsync()).ProjetoId);
    }

    [Fact]
    public async Task Marcos_e_datas_aparecem_no_calendario_e_projeto_ativo_enche_a_colmeia()
    {
        var id = await Criar(_felipe, "Viagem Chile", inicio: Hoje.AddDays(2), fim: Hoje.AddDays(5));
        var s = Projetos(_felipe);
        Assert.False((await s.SalvarItemAsync(new DadosItemProjeto(null, id, TipoItemProjeto.Marco, "Voo", null, null))).Sucesso);
        Assert.True((await s.SalvarItemAsync(new DadosItemProjeto(null, id, TipoItemProjeto.Marco, "Voo LA800", Hoje.AddDays(2), new TimeOnly(8, 40)))).Sucesso);

        var cal = await Favoa(_gab).ObterCalendarioAsync(Hoje, Hoje.AddDays(6), Hoje);
        var dia2 = cal.Dias[2].Itens.Where(i => i.Tipo == TipoItemDia.Projeto).Select(i => i.Texto).ToList();
        Assert.Contains("Voo LA800", dia2);
        Assert.Contains("Ida: Viagem Chile", dia2);
        Assert.Contains(cal.Dias[5].Itens, i => i.Texto == "Volta: Viagem Chile");

        var painel = await Favoa(_felipe).ObterPainelAsync(Hoje);
        var celula = painel.Celulas.Single(c => c.Chave == CelulaColmeia.Projetos);
        Assert.Equal(EstadoCelula.Ativa, celula.Estado);
        Assert.Contains("Viagem Chile", celula.Detalhe);
        Assert.Equal("Voo LA800 · " + Hoje.AddDays(2).ToString("dd/MM"), (await s.ObterAsync(id, Hoje))!.Resumo.Proximo);
    }

    [Fact]
    public async Task Exportar_e_importar_leva_os_projetos()
    {
        var id = await Criar(_felipe, "Viagem Chile", orcamento: 100000);
        await Registrar(_felipe, "tarefa renovar passaporte #chile");
        await Registrar(_felipe, "mercado 99 #chile");

        var dados = new DadosFavoa(_fabrica, new OpcoesFavoa { PastaDados = Directory.CreateTempSubdirectory("favoa-prj-").FullName });
        var arquivo = (await dados.ExportarJsonAsync()).Conteudo;
        Assert.True((await dados.ImportarJsonAsync(arquivo)).Sucesso);

        var p = (await Projetos(_gab).ObterAsync(id, Hoje))!;
        Assert.Equal(100000, p.Resumo.OrcamentoCentavos);
        Assert.Single(p.Tarefas);
        Assert.Single(p.Lancamentos);
        Assert.Equal(TiposProjeto.Modelo(TipoProjeto.Viagem).Count, p.Itens.Count);
    }
}
