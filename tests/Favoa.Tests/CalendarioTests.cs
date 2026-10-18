using Favoa.Core.Captura;
using Favoa.Core.Financas;
using Favoa.Core.Painel;
using Favoa.Core.Pessoas;
using Favoa.Core.Rotina;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Favoa.Tests;

/// <summary>Tela inicial em calendário: histórico da colmeia, itens de cada dia e preferências por pessoa.</summary>
public sealed class CalendarioTests : IAsyncLifetime
{
    // Data real: tarefas e hábitos carimbam a criação com o relógio.
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

    private FavoaServico Favoa(Guid pessoa) => new(_fabrica, new PessoaFixa(pessoa));

    private static async Task Registrar(FavoaServico s, string frase)
    {
        var r = await s.RegistrarAsync(InterpretadorFrase.Interpretar(frase, await s.ObterContextoAsync(Hoje)));
        Assert.True(r.Sucesso, r.Mensagem);
    }

    /// <summary>Joga a criação de tudo para dias atrás, como se o Fávoa estivesse em uso há tempo.</summary>
    private async Task Envelhecer(int dias)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var antes = DateTime.UtcNow.AddDays(-dias);
        await db.Tarefas.ExecuteUpdateAsync(u => u.SetProperty(t => t.CriadoEm, antes));
        await db.Habitos.ExecuteUpdateAsync(u => u.SetProperty(h => h.CriadoEm, antes));
    }

    [Fact]
    public async Task Progresso_de_hoje_e_o_mesmo_da_colmeia()
    {
        var s = Favoa(_felipe);
        foreach (var f in new[] { "orçamento 3000", "mercado 400", "bebi 1200 ml", "novo hábito ler", "novo hábito correr", "fiz ler",
                     "tarefa pagar luz hoje", "tarefa ligar banco hoje", "supino 3x10 40kg", "comi 150g de arroz tipo 1 cozido no almoço" })
            await Registrar(s, f);
        var painel = await s.ObterPainelAsync(Hoje);
        await s.AlternarTarefaAsync(painel.ProximasTarefas[0].Id, Hoje);
        painel = await s.ObterPainelAsync(Hoje);

        var hoje = Assert.Single(await s.ObterProgressoAsync(Hoje, Hoje, Hoje));
        Assert.NotNull(hoje);
        Assert.Equal(painel.Centro.Preenchimento, hoje.Geral, 6);
        Assert.Equal(painel.Celulas.Single(c => c.Chave == CelulaColmeia.Tarefas).Preenchimento, hoje.Tarefas, 6);
        Assert.Equal(painel.Celulas.Single(c => c.Chave == CelulaColmeia.Alimentacao).Preenchimento, hoje.Alimentacao, 6);
        Assert.Equal(painel.Celulas.Single(c => c.Chave == CelulaColmeia.Treino).Preenchimento, hoje.Treino!.Value, 6);
    }

    [Fact]
    public async Task Dias_passados_contam_o_que_foi_feito_naquele_dia()
    {
        var s = Favoa(_felipe);
        await Registrar(s, "novo hábito ler");
        await Registrar(s, "tarefa pagar luz");
        await Envelhecer(5);

        var ontem = Hoje.AddDays(-1);
        var habito = (await s.ObterPainelAsync(Hoje)).HabitosPendentes.Single();
        await s.AlternarHabitoAsync(habito.Id, ontem); // marcou ontem, ainda não hoje
        await using (var db = await _fabrica.CreateDbContextAsync())
        {
            var t = await db.Tarefas.SingleAsync();
            t.Prazo = ontem.AddDays(-1);
            t.ConcluidaEm = ontem; // atrasada anteontem, feita ontem
            db.RegistrosAgua.Add(new RegistroAgua { PessoaId = _felipe, Data = ontem, Mililitros = 2500 });
            await db.SaveChangesAsync();
        }

        var dias = await s.ObterProgressoAsync(Hoje.AddDays(-2), Hoje, Hoje);
        var (anteontem, d1, d0) = (dias[0]!, dias[1]!, dias[2]!);
        Assert.Equal(0, anteontem.Tarefas); // tinha uma para o dia e não fez
        Assert.Equal(1, d1.Tarefas);         // fez a atrasada
        Assert.Equal(1, d1.Habitos);
        Assert.Equal(1, d1.Alimentacao);
        Assert.Equal(0, d0.Habitos);
        Assert.Equal(0, d0.Alimentacao);
        Assert.Null(d1.Treino);              // ainda sem treinos: fica fora da média
        Assert.True(d1.Geral > d0.Geral);
    }

    [Fact]
    public async Task Antes_do_primeiro_registro_e_no_futuro_nao_ha_progresso()
    {
        var s = Favoa(_felipe);
        Assert.All(await s.ObterProgressoAsync(Hoje.AddDays(-3), Hoje, Hoje), Assert.Null);

        await Registrar(s, "bebi 300 ml");
        var dias = await s.ObterProgressoAsync(Hoje.AddDays(-3), Hoje.AddDays(2), Hoje);
        Assert.Equal(6, dias.Count);
        Assert.All(dias.Take(3), Assert.Null);
        Assert.NotNull(dias[3]);
        Assert.All(dias.Skip(4), Assert.Null);
    }

    [Fact]
    public void Tarefa_atrasada_pesa_ate_o_dia_em_que_foi_feita()
    {
        var d0 = new DateOnly(2026, 10, 1);
        var dados = new DadosHistorico
        {
            InicioDeUso = d0,
            Tarefas = [(d0, d0, d0.AddDays(3))],
        };
        var dias = HistoricoColmeia.Calcular(dados, d0, d0.AddDays(4), d0.AddDays(10));
        Assert.Equal([0d, 0, 0, 1, 1], dias.Select(p => p!.Tarefas)); // dia 4: sem nada pendente, "livre"
        var resumo = HistoricoColmeia.Resumo(dados, d0.AddDays(2));
        Assert.Equal(1, resumo.TarefasAtrasadas);
        Assert.Equal(0, resumo.TarefasParaHoje);
    }

    [Fact]
    public void Sequencia_conta_dias_seguidos_acima_do_minimo()
    {
        var hoje = new DateOnly(2026, 10, 10);
        ProgressoDia P(int atras, double g) => new(hoje.AddDays(-atras), g, 0, 0, 0, 0, null);
        Assert.Equal(3, HistoricoColmeia.Sequencia([P(4, 0.2), P(3, 0.9), P(2, 0.8), P(1, 0.7), P(0, 0.1)], hoje)); // hoje ainda enchendo
        Assert.Equal(2, HistoricoColmeia.Sequencia([P(2, 0.1), P(1, 0.9), P(0, 1)], hoje));
        Assert.Equal(0, HistoricoColmeia.Sequencia([P(1, 0.5), null], hoje));
    }

    [Fact]
    public async Task Calendario_mostra_tarefas_contas_previstas_lembretes_e_registros()
    {
        var s = Favoa(_felipe);
        var amanha = Hoje.AddDays(1);
        Assert.True((await s.CriarTarefaAsync("levar o carro na revisão", amanha)).Sucesso);
        await Registrar(s, "uber 23");
        await Registrar(s, "peso 82,5");
        var cadastros = new CadastrosServico(_fabrica, new Favoa.Core.Pessoas.PessoaFixa(null));
        var rec = await cadastros.SalvarRecorrenciaAsync(new DadosRecorrencia(null, TipoLancamento.Despesa, "Internet", 12000, "Moradia", null,
            amanha.AddDays(1).Day, Hoje), Hoje);
        Assert.True(rec.Sucesso, rec.Mensagem);
        await new LembretesServico(_fabrica, new PessoaFixa(_felipe), new OpcoesFavoa(), NullLogger<LembretesServico>.Instance)
            .SalvarAsync(new DadosLembrete(null, "Tomar água", TipoLembrete.Livre, new TimeOnly(9, 0), DiasSemana.Todos));

        var cal = await s.ObterCalendarioAsync(Hoje, Hoje.AddDays(3), Hoje);
        var hoje = cal.Dias[0];
        Assert.Equal(2300, hoje.DespesasCentavos);
        Assert.Contains(hoje.Itens, i => i.Tipo == TipoItemDia.Peso && i.Texto == "Peso 82,5 kg");
        Assert.Contains(hoje.Itens, i => i.Tipo == TipoItemDia.Lancamento && i.Detalhe == "R$ 23,00");
        Assert.Contains(hoje.Itens, i => i.Tipo == TipoItemDia.Lembrete && i.Detalhe == "09:00");

        var diaAmanha = cal.Dias[1];
        var tarefa = Assert.Single(diaAmanha.Itens, i => i.Tipo == TipoItemDia.Tarefa);
        Assert.Equal("Levar o carro na revisão", tarefa.Texto);
        Assert.Null(diaAmanha.Progresso);
        Assert.Contains(cal.Dias[2].Itens, i => i.Tipo == TipoItemDia.ContaPrevista && i.Texto == "Internet" && i.Detalhe == "R$ 120,00");

        // Concluir pelo calendário
        await s.AlternarTarefaAsync(tarefa.Id!.Value, Hoje);
        var depois = await s.ObterCalendarioAsync(amanha, amanha, Hoje);
        Assert.True(depois.Dias[0].Itens.Single(i => i.Tipo == TipoItemDia.Tarefa).Feito);
    }

    [Fact]
    public async Task Calendario_de_cada_um_mostra_so_o_pessoal_dele_e_a_casa_inteira()
    {
        await Registrar(Favoa(_felipe), "peso 82");
        await Registrar(Favoa(_felipe), "supino 3x10 40kg");
        await Registrar(Favoa(_felipe), "mercado 100");
        await Registrar(Favoa(_gab), "tarefa comprar pão hoje");

        var eva = (await Favoa(_gab).ObterCalendarioAsync(Hoje, Hoje, Hoje)).Dias[0];
        Assert.DoesNotContain(eva.Itens, i => i.Tipo is TipoItemDia.Peso or TipoItemDia.Treino);
        Assert.Contains(eva.Itens, i => i.Tipo == TipoItemDia.Lancamento);
        Assert.Contains(eva.Itens, i => i.Tipo == TipoItemDia.Tarefa);

        var davi = (await Favoa(_felipe).ObterCalendarioAsync(Hoje, Hoje, Hoje)).Dias[0];
        Assert.Contains(davi.Itens, i => i.Tipo == TipoItemDia.Treino && i.Detalhe!.Contains("Supino"));
        Assert.Contains(davi.Itens, i => i.Tipo == TipoItemDia.Tarefa);
    }

    [Fact]
    public async Task Tela_inicial_e_escolha_de_cada_pessoa()
    {
        Assert.Equal(PreferenciasInicio.Padrao, await Favoa(_felipe).ObterPreferenciasInicioAsync());
        await Favoa(_gab).SalvarPreferenciasInicioAsync(new PreferenciasInicio(TelaInicial.Calendario, VisaoCalendario.Semana));
        Assert.Equal(new PreferenciasInicio(TelaInicial.Calendario, VisaoCalendario.Semana), await Favoa(_gab).ObterPreferenciasInicioAsync());
        Assert.Equal(PreferenciasInicio.Padrao, await Favoa(_felipe).ObterPreferenciasInicioAsync());
        await Favoa(_gab).SalvarPreferenciasInicioAsync(new PreferenciasInicio(TelaInicial.Colmeia, VisaoCalendario.Mes));
        Assert.Equal(TelaInicial.Colmeia, (await Favoa(_gab).ObterPreferenciasInicioAsync()).Tela);
    }

    [Fact]
    public async Task Criar_tarefa_valida_o_titulo()
    {
        Assert.False((await Favoa(_felipe).CriarTarefaAsync("   ", Hoje)).Sucesso);
        var r = await Favoa(_felipe).CriarTarefaAsync("pagar boleto", Hoje.AddDays(5));
        Assert.True(r.Sucesso);
        Assert.Equal($"Tarefa para {Hoje.AddDays(5):dd/MM}: Pagar boleto", r.Mensagem);
    }
}
