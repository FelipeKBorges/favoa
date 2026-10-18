using Favoa.Core.Captura;
using Favoa.Core.Financas;
using Favoa.Core.Fitness;
using Favoa.Core.Pessoas;
using Favoa.Core.Rotina;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Favoa.Tests;

/// <summary>Casal no mesmo Fávoa: finanças e tarefas da casa, o resto de cada um.</summary>
public sealed class PessoasTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = new(2026, 10, 10);
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
    private FitnessServico Fitness(Guid pessoa) => new(_fabrica, new PessoaFixa(pessoa));
    private LembretesServico Lembretes(Guid? pessoa) => new(_fabrica, new PessoaFixa(pessoa), new OpcoesFavoa(), NullLogger<LembretesServico>.Instance);

    private static async Task Registrar(FavoaServico s, string frase)
    {
        var r = await s.RegistrarAsync(InterpretadorFrase.Interpretar(frase, await s.ObterContextoAsync(Hoje)));
        Assert.True(r.Sucesso, r.Mensagem);
    }

    [Fact]
    public async Task Agua_habitos_peso_treino_e_comida_sao_de_cada_um()
    {
        foreach (var f in new[] { "bebi 500 ml", "novo hábito ler", "fiz ler", "peso 82,5", "supino 3x10 40kg", "comi 150g de arroz tipo 1 cozido no almoço" })
            await Registrar(Favoa(_felipe), f);
        foreach (var f in new[] { "bebi 300 ml", "novo hábito ler", "peso 61" })
            await Registrar(Favoa(_gab), f); // "ler" de novo: hábitos são por pessoa

        var davi = (await Favoa(_felipe).ObterPainelAsync(Hoje)).Resumo;
        var eva = (await Favoa(_gab).ObterPainelAsync(Hoje)).Resumo;
        Assert.Equal(500, davi.AguaHojeMl);
        Assert.Equal(300, eva.AguaHojeMl);
        Assert.Equal(1, davi.HabitosFeitosHoje);
        Assert.Equal(0, eva.HabitosFeitosHoje);
        Assert.True(davi.TemTreinos);
        Assert.False(eva.TemTreinos);
        Assert.True(davi.KcalHoje > 0);
        Assert.Equal(0, eva.KcalHoje);

        Assert.Equal(82.5m, (await Fitness(_felipe).ObterPesosAsync(30, Hoje)).Atual!.Kg);
        Assert.Equal(61m, (await Fitness(_gab).ObterPesosAsync(30, Hoje)).Atual!.Kg);
        Assert.Empty((await Fitness(_gab).ObterPainelTreinoAsync(Hoje)).Recentes);
    }

    [Fact]
    public async Task Plano_e_metas_de_dieta_sao_de_cada_um()
    {
        var arroz = (await Fitness(_felipe).BuscarAlimentosAsync("arroz tipo 1 cozido", 1)).Single(); // base de alimentos é da casa
        await Fitness(_felipe).AdicionarItemPlanoAsync(Refeicao.Almoco, arroz.Id, 150);
        await Fitness(_felipe).SalvarMetasAsync(new MetasFitness(2000, 150, 230, 50, 4));
        await Fitness(_gab).SalvarMetasAsync(new MetasFitness(1600, 110, 180, 45, 3));

        var diaFelipe = await Fitness(_felipe).ObterDiaDietaAsync(Hoje);
        var diaGab = await Fitness(_gab).ObterDiaDietaAsync(Hoje);
        Assert.Single(diaFelipe.Refeicoes.Single(r => r.Refeicao == Refeicao.Almoco).Plano);
        Assert.Empty(diaGab.Refeicoes.SelectMany(r => r.Plano));
        Assert.Equal(2000, diaFelipe.Metas.Kcal);
        Assert.Equal(1600, diaGab.Metas.Kcal);
        Assert.Equal(4, (await Fitness(_felipe).ObterPainelTreinoAsync(Hoje)).MetaSemana);
        Assert.Equal(3, (await Fitness(_gab).ObterPainelTreinoAsync(Hoje)).MetaSemana);
    }

    [Fact]
    public async Task Financas_sao_da_casa_e_guardam_quem_lancou()
    {
        await Registrar(Favoa(_felipe), "uber 23");
        await Registrar(Favoa(_gab), "mercado 100");
        await Registrar(Favoa(_gab), "tarefa comprar pão amanhã");

        foreach (var quem in new[] { _felipe, _gab })
        {
            var extrato = await Favoa(quem).ObterExtratoAsync(FiltroExtrato.DoMes(Hoje), Hoje);
            Assert.Equal(12300, extrato.DespesasCentavos); // os dois veem tudo
            Assert.Single((await Favoa(quem).ObterQuadroTarefasAsync(Hoje)).Proximas);
        }

        var soGab = await Favoa(_felipe).ObterExtratoAsync(FiltroExtrato.DoMes(Hoje) with { PessoaId = _gab }, Hoje);
        var linha = Assert.Single(soGab.Dias.SelectMany(d => d.Lancamentos));
        Assert.Equal(10000, linha.ValorCentavos);
        Assert.Equal(_gab, linha.PessoaId);
    }

    [Fact]
    public async Task Lembretes_sao_de_cada_um_e_so_tocam_nos_aparelhos_do_dono()
    {
        await Lembretes(_felipe).SalvarAsync(new DadosLembrete(null, "Treino", TipoLembrete.Livre, new TimeOnly(7, 0), DiasSemana.Todos));
        await Lembretes(_gab).SalvarAsync(new DadosLembrete(null, "Água", TipoLembrete.Livre, new TimeOnly(7, 0), DiasSemana.Todos));
        Assert.Equal("Treino", (await Lembretes(_felipe).ObterAsync()).Lembretes.Single().Titulo);
        Assert.Equal("Água", (await Lembretes(_gab).ObterAsync()).Lembretes.Single().Titulo);

        // Só o Davi tem aparelho (endereço que não responde: o envio falha, mas prova para quem foi).
        using var ec = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var q = ec.ExportParameters(false).Q;
        await Lembretes(_felipe).InscreverAparelhoAsync(new InscricaoPush("https://127.0.0.1:9/push",
            WebPush.Base64Url([4, .. q.X!, .. q.Y!]), WebPush.Base64Url(new byte[16]), "Android Chrome"));
        Assert.Single((await Lembretes(_felipe).ObterAsync()).Aparelhos);
        Assert.Empty((await Lembretes(_gab).ObterAsync()).Aparelhos);

        await using (var db = await _fabrica.CreateDbContextAsync())
            await db.Lembretes.ExecuteUpdateAsync(u => u.SetProperty(l => l.UltimoDisparo, (DateOnly?)null));

        // Agendador: sem pessoa atual, enxerga todos e manda cada lembrete para o dono.
        await Lembretes(null).DispararDevidosAsync(new DateTime(2026, 10, 10, 7, 0, 30));
        Assert.Contains("falharam", (await Lembretes(_felipe).ObterAsync()).Lembretes.Single().UltimoResultado);
        Assert.Equal("Não avisou: nenhum aparelho com notificações ativadas.", (await Lembretes(_gab).ObterAsync()).Lembretes.Single().UltimoResultado);
    }

    [Fact]
    public async Task Banco_de_antes_das_pessoas_vira_todo_da_primeira_pessoa()
    {
        // Simula dados do tempo de uma pessoa só.
        await using (var db = await _fabrica.CreateDbContextAsync())
        {
            db.RegistrosAgua.Add(new RegistroAgua { Mililitros = 700, Data = Hoje });
            db.Configuracoes.Add(new Configuracao { Chave = MetasFitness.ChaveKcal, Valor = "2100" });
            db.Configuracoes.RemoveRange(db.Configuracoes.Where(c => c.Chave == "acesso_pessoas_migrado"));
            await db.SaveChangesAsync();
        }

        await _sp.PrepararBancoComCasaAsync();

        Assert.Equal(700, (await Favoa(_felipe).ObterPainelAsync(Hoje)).Resumo.AguaHojeMl);
        Assert.Equal(0, (await Favoa(_gab).ObterPainelAsync(Hoje)).Resumo.AguaHojeMl);
        Assert.Equal(2100, (await Fitness(_felipe).ObterMetasAsync()).Kcal);
    }
}
