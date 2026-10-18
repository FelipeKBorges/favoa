using Favoa.Core.Captura;
using Favoa.Core.Fitness;
using Favoa.Core.Painel;
using Favoa.Core.Pessoas;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

/// <summary>Copiar refeição, alimentos recentes e repetir treino: gravam dados todo dia e são de cada pessoa.</summary>
public sealed class RepeticaoDietaTreinoTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = new(2026, 10, 14);
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

    private FavoaServico Favoa(Guid p) => new(_fabrica, new PessoaFixa(p));
    private FitnessServico Fitness(Guid p) => new(_fabrica, new PessoaFixa(p));

    private async Task Dizer(Guid pessoa, string frase, DateOnly? dia = null)
    {
        var s = Favoa(pessoa);
        var r = await s.RegistrarAsync(InterpretadorFrase.Interpretar(frase, await s.ObterContextoAsync(dia ?? Hoje)));
        Assert.True(r.Sucesso, r.Mensagem);
    }

    private static int Itens(DiaDieta d, Refeicao r) => d.Refeicoes.Single(x => x.Refeicao == r).Consumido.Count;

    [Fact]
    public async Task Copiar_refeicao_leva_o_que_foi_comido_para_o_outro_dia()
    {
        var ontem = Hoje.AddDays(-1);
        await Dizer(_felipe, "comi 150g de arroz tipo 1 cozido no almoço", ontem);
        var fit = Fitness(_felipe);
        Assert.Equal(1, Itens(await fit.ObterDiaDietaAsync(ontem), Refeicao.Almoco));

        var r = await fit.CopiarRefeicaoAsync(ontem, Hoje, Refeicao.Almoco);

        Assert.True(r.Sucesso, r.Mensagem);
        var hoje = await fit.ObterDiaDietaAsync(Hoje);
        Assert.Equal(1, Itens(hoje, Refeicao.Almoco));
        Assert.Equal(1, Itens(await fit.ObterDiaDietaAsync(ontem), Refeicao.Almoco)); // a origem não muda
    }

    [Fact]
    public async Task Copiar_refeicao_sem_origem_nao_grava_nada()
    {
        var fit = Fitness(_felipe);

        var r = await fit.CopiarRefeicaoAsync(Hoje.AddDays(-1), Hoje, Refeicao.Almoco);

        Assert.False(r.Sucesso);
        Assert.Equal(0, Itens(await fit.ObterDiaDietaAsync(Hoje), Refeicao.Almoco));
    }

    [Fact]
    public async Task Copiar_refeicao_nao_atravessa_pessoas()
    {
        var ontem = Hoje.AddDays(-1);
        await Dizer(_felipe, "comi 150g de arroz tipo 1 cozido no almoço", ontem);

        var r = await Fitness(_gab).CopiarRefeicaoAsync(ontem, Hoje, Refeicao.Almoco);

        Assert.False(r.Sucesso); // a Eva não tem nada ontem
        Assert.Equal(0, Itens(await Fitness(_gab).ObterDiaDietaAsync(Hoje), Refeicao.Almoco));
        Assert.Equal(0, Itens(await Fitness(_felipe).ObterDiaDietaAsync(Hoje), Refeicao.Almoco));
    }

    [Fact]
    public async Task Alimentos_recentes_ordenam_por_frequencia_e_trazem_a_ultima_quantidade()
    {
        await Dizer(_felipe, "comi 100g de arroz tipo 1 cozido no almoço", Hoje.AddDays(-3));
        await Dizer(_felipe, "comi 150g de arroz tipo 1 cozido no almoço", Hoje.AddDays(-2));
        await Dizer(_felipe, "comi 200g de arroz tipo 1 cozido no jantar", Hoje.AddDays(-1));

        var recentes = await Fitness(_felipe).ObterAlimentosRecentesAsync(Hoje);

        var arroz = Assert.Single(recentes);
        Assert.Equal(3, arroz.Vezes);
        Assert.Equal(200, arroz.Gramas);
    }

    [Fact]
    public async Task Alimentos_recentes_ignoram_o_que_e_da_outra_pessoa_e_o_que_e_muito_antigo()
    {
        await Dizer(_gab, "comi 150g de arroz tipo 1 cozido no almoço", Hoje.AddDays(-1));
        await Dizer(_felipe, "comi 150g de arroz tipo 1 cozido no almoço", Hoje.AddDays(-90));

        Assert.Empty(await Fitness(_felipe).ObterAlimentosRecentesAsync(Hoje));
        Assert.Single(await Fitness(_gab).ObterAlimentosRecentesAsync(Hoje));
    }

    [Fact]
    public async Task Repetir_treino_copia_o_ultimo_com_tudo_marcado_como_feito()
    {
        await Dizer(_felipe, "supino 3x10 40kg", Hoje.AddDays(-1));

        var rascunho = await Fitness(_felipe).PrepararRepeticaoAsync(Hoje);

        Assert.NotNull(rascunho);
        Assert.Equal(Hoje, rascunho!.Data);
        var linha = Assert.Single(rascunho.Linhas);
        Assert.Equal(3, linha.Series);
        Assert.Equal(10, linha.Repeticoes);
        Assert.Equal(40m, linha.CargaKg);
        Assert.True(linha.Feito);
    }

    [Fact]
    public async Task Repetir_treino_sem_historico_ou_de_outra_pessoa_devolve_nulo()
    {
        Assert.Null(await Fitness(_felipe).PrepararRepeticaoAsync(Hoje));
        await Dizer(_felipe, "supino 3x10 40kg", Hoje.AddDays(-1));
        Assert.Null(await Fitness(_gab).PrepararRepeticaoAsync(Hoje));
    }

    [Fact]
    public void Colmeia_sem_nenhuma_tarefa_cadastrada_nao_conta_a_celula_como_cheia()
    {
        var vazio = CalculadoraColmeia.Calcular(new ResumoDoDia { SemTarefasCadastradas = true });
        var livre = CalculadoraColmeia.Calcular(new ResumoDoDia());

        Assert.Equal(0, vazio.Single(c => c.Chave == CelulaColmeia.Tarefas).Preenchimento);
        Assert.Equal(1, livre.Single(c => c.Chave == CelulaColmeia.Tarefas).Preenchimento);
    }
}
