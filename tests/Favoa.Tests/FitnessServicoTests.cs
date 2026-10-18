using Favoa.Core.Captura;
using Favoa.Core.Edicao;
using Favoa.Core.Fitness;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

public sealed class FitnessServicoTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = new(2026, 10, 10);

    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private FavoaServico _favoa = null!;
    private FitnessServico _fit = null!;

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao)).BuildServiceProvider();
        await _sp.PrepararBancoComCasaAsync();
        var fabrica = _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        _favoa = new FavoaServico(fabrica, new Favoa.Core.Pessoas.PessoaFixa(null));
        _fit = new FitnessServico(fabrica, new Favoa.Core.Pessoas.PessoaFixa(null));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private async Task<Favoa.Core.ResultadoRegistro> Dizer(string frase, DateOnly? dia = null)
    {
        var ctx = await _favoa.ObterContextoAsync(dia ?? Hoje);
        return await _favoa.RegistrarAsync(InterpretadorFrase.Interpretar(frase, ctx));
    }

    private FavoaDbContext Db() => _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>().CreateDbContext();

    // ---------- Treino ----------

    [Fact]
    public async Task Ficha_vira_treino_do_dia_e_a_proxima_vez_comeca_pelo_que_foi_feito()
    {
        await _fit.SalvarFichaAsync(new FichaEdicao(null, "Treino A", [
            new("Supino reto", TipoExercicio.Forca, 4, 10, 40, null),
            new("Esteira", TipoExercicio.Cardio, null, null, null, 15),
        ]));
        var painel = await _fit.ObterPainelTreinoAsync(Hoje);
        var fichaA = painel.Fichas.Single();
        Assert.Equal("Peito", painel.Exercicios.Single(e => e.Nome == "Supino reto").Grupo);

        var rascunho = await _fit.PrepararSessaoAsync(fichaA.Id, Hoje.AddDays(-2));
        Assert.Equal(40, rascunho.Linhas[0].CargaKg);
        rascunho.Linhas[0].CargaKg = 42.5m;
        rascunho.Linhas.ForEach(l => l.Feito = true);
        Assert.True((await _fit.SalvarSessaoAsync(rascunho)).Sucesso);

        var deNovo = await _fit.PrepararSessaoAsync(fichaA.Id, Hoje);
        Assert.Equal(42.5m, deNovo.Linhas[0].CargaKg);
        Assert.Contains("42,5 kg", deNovo.Linhas[0].UltimaVez);

        painel = await _fit.ObterPainelTreinoAsync(Hoje);
        Assert.Equal(1, painel.TreinosUltimos7Dias);
        Assert.Equal("Treino A", painel.Recentes.Single().Titulo);
    }

    [Fact]
    public async Task Nome_curto_na_captura_usa_o_exercicio_da_ficha_quando_nao_ha_duvida()
    {
        await _fit.SalvarFichaAsync(new FichaEdicao(null, "A", [new("Supino reto", TipoExercicio.Forca, 4, 10, 40, null)]));
        await Dizer("supino 3x8 45kg");
        Assert.Single((await _fit.ObterPainelTreinoAsync(Hoje)).Exercicios);

        await _fit.SalvarFichaAsync(new FichaEdicao(null, "B", [new("Supino inclinado", TipoExercicio.Forca, 4, 10, 30, null)]));
        await Dizer("supino 3x8 45kg"); // agora é ambíguo
        Assert.Equal(3, (await _fit.ObterPainelTreinoAsync(Hoje)).Exercicios.Count);
    }

    [Fact]
    public async Task Sessao_sem_nada_marcado_e_recusada()
    {
        var r = await _fit.SalvarSessaoAsync(new SessaoRascunho { Data = Hoje, Linhas = [new LinhaSessao { Exercicio = "Supino" }] });
        Assert.False(r.Sucesso);
    }

    [Fact]
    public async Task Proxima_ficha_gira_na_ordem()
    {
        await _fit.SalvarFichaAsync(new FichaEdicao(null, "A", [new("Supino", TipoExercicio.Forca, 3, 10, null, null)]));
        await _fit.SalvarFichaAsync(new FichaEdicao(null, "B", [new("Remada", TipoExercicio.Forca, 3, 10, null, null)]));
        var painel = await _fit.ObterPainelTreinoAsync(Hoje);
        Assert.Equal("A", painel.ProximaFicha!.Nome);

        var sessao = await _fit.PrepararSessaoAsync(painel.ProximaFicha.Id, Hoje);
        sessao.Linhas.ForEach(l => l.Feito = true);
        await _fit.SalvarSessaoAsync(sessao);

        Assert.Equal("B", (await _fit.ObterPainelTreinoAsync(Hoje)).ProximaFicha!.Nome);
    }

    [Fact]
    public async Task Captura_de_exercicios_agrupa_no_mesmo_treino_e_desfazer_limpa()
    {
        var r1 = await Dizer("supino 3x10 40kg");
        var r2 = await Dizer("corri 5 km em 30 min");
        Assert.True(r1.Sucesso, r1.Mensagem);
        Assert.True(r2.Sucesso, r2.Mensagem);

        await using (var db = Db())
        {
            Assert.Equal(1, await db.Sessoes.CountAsync());
            Assert.Equal(2, await db.ExerciciosRealizados.CountAsync());
        }

        await _favoa.DesfazerAsync(r2.Criado!);
        await _favoa.DesfazerAsync(r1.Criado!);

        await using var depois = Db();
        Assert.Equal(0, await depois.Sessoes.CountAsync());
        Assert.Equal(0, await depois.Exercicios.CountAsync()); // criados pela captura e sem uso: saem juntos
    }

    [Fact]
    public async Task Evolucao_mostra_a_maior_carga_por_dia()
    {
        await Dizer("supino 3x10 40kg", Hoje.AddDays(-7));
        await Dizer("supino 3x8 45kg", Hoje.AddDays(-3));
        await Dizer("supino 3x10 30kg", Hoje.AddDays(-3));

        var supino = (await _fit.ObterPainelTreinoAsync(Hoje)).Exercicios.Single();
        var evolucao = await _fit.ObterEvolucaoAsync(supino.Id);

        Assert.Equal([40m, 45m], evolucao!.Pontos.Select(p => p.Valor));
        Assert.Equal(45m, supino.MelhorCargaKg);
        Assert.Equal(2, supino.VezesFeito);
    }

    [Fact]
    public async Task Colmeia_conta_treinos_da_semana()
    {
        await _fit.SalvarMetasAsync(new MetasFitness(0, 0, 0, 0, 4));
        await Dizer("supino 3x10 40kg", Hoje.AddDays(-1));
        await Dizer("corri 5km", Hoje);

        var painel = await _favoa.ObterPainelAsync(Hoje);
        Assert.Equal(2, painel.Resumo.TreinosUltimos7Dias);
        Assert.True(painel.Resumo.TreinouHoje);
        Assert.Equal("2 de 4", painel.Celulas.Single(c => c.Chave == Favoa.Core.Painel.CelulaColmeia.Treino).Valor);
    }

    // ---------- Dieta ----------

    [Fact]
    public async Task Taco_carregada_e_busca_sem_acento()
    {
        var feijao = await _fit.BuscarAlimentosAsync("feijao carioca cozido");
        Assert.NotEmpty(feijao);
        Assert.Contains("Feijão", feijao[0].Nome);
        Assert.True(feijao[0].Kcal > 50);

        Assert.NotEmpty(await _fit.BuscarAlimentosAsync("ovos")); // plural acha "Ovo"
    }

    [Fact]
    public async Task Comi_pela_captura_soma_as_calorias_do_dia()
    {
        var arroz = (await _fit.BuscarAlimentosAsync("arroz tipo 1 cozido"))[0];

        var r = await Dizer("comi 200g de arroz tipo 1 cozido no almoço");
        Assert.True(r.Sucesso, r.Mensagem);

        var dia = await _fit.ObterDiaDietaAsync(Hoje);
        var almoco = dia.Refeicoes.Single(x => x.Refeicao == Refeicao.Almoco);
        Assert.Equal(arroz.Id, almoco.Consumido.Single().AlimentoId);
        Assert.Equal(arroz.Kcal * 2, dia.Total.Kcal, 1);

        var painel = await _favoa.ObterPainelAsync(Hoje);
        Assert.Equal((int)Math.Round(arroz.Kcal * 2), painel.Resumo.KcalHoje);
    }

    [Fact]
    public async Task Alimento_desconhecido_ou_sem_gramas_explica_o_que_falta()
    {
        Assert.False((await Dizer("comi 100g de xpto inexistente")).Sucesso);
        Assert.Contains("gramas", (await Dizer("comi 2 arroz")).Mensagem);
    }

    [Fact]
    public async Task Alimento_proprio_com_porcao_aceita_unidades()
    {
        await _fit.SalvarAlimentoAsync(new AlimentoEdicao(null, "pão de queijo da padaria", 330, 6, 34, 18, "unidade", 40));

        var r = await Dizer("comi 3 pão de queijo da padaria");

        Assert.True(r.Sucesso, r.Mensagem);
        Assert.Equal(120, (await _fit.ObterDiaDietaAsync(Hoje)).Refeicoes.SelectMany(x => x.Consumido).Single().Gramas);
    }

    [Fact]
    public async Task Seguir_o_plano_copia_os_itens_e_nao_duplica()
    {
        var ovo = (await _fit.BuscarAlimentosAsync("ovo galinha inteiro cozido"))[0];
        var pao = (await _fit.BuscarAlimentosAsync("pão francês"))[0];
        await _fit.AdicionarItemPlanoAsync(Refeicao.CafeDaManha, ovo.Id, 100);
        await _fit.AdicionarItemPlanoAsync(Refeicao.CafeDaManha, pao.Id, 50);

        await _fit.SeguirPlanoAsync(Hoje, Refeicao.CafeDaManha);
        await _fit.SeguirPlanoAsync(Hoje, Refeicao.CafeDaManha);

        var cafe = (await _fit.ObterDiaDietaAsync(Hoje)).Refeicoes.Single(r => r.Refeicao == Refeicao.CafeDaManha);
        Assert.Equal(2, cafe.Consumido.Count);
        Assert.True(cafe.SeguiuPlano);
        Assert.Equal(cafe.TotalPlano.Kcal, cafe.TotalConsumido.Kcal, 3);
    }

    // ---------- Corpo ----------

    [Fact]
    public async Task Peso_um_por_dia_e_desfazer_volta_o_anterior()
    {
        await Dizer("peso 82,5", Hoje.AddDays(-30));
        await Dizer("peso 81", Hoje);
        var corrigido = await Dizer("peso 80,6", Hoje); // mesmo dia: substitui

        var historico = await _fit.ObterPesosAsync(90, Hoje);
        Assert.Equal(2, historico.Pontos.Count);
        Assert.Equal(80.6m, historico.Atual!.Kg);
        Assert.Equal(-1.9m, historico.Variacao30Dias);

        await _favoa.DesfazerAsync(corrigido.Criado!);
        Assert.Equal(81m, (await _fit.ObterPesosAsync(90, Hoje)).Atual!.Kg);
    }

    [Fact]
    public async Task Excluir_consumo_e_exercicio_pela_referencia()
    {
        var comi = await Dizer("comi 100g de banana prata");
        var fiz = await Dizer("remada 3x12 30kg");

        Assert.True((await _favoa.ExcluirAsync(comi.Criado!)).Sucesso);
        Assert.True((await _favoa.ExcluirAsync(fiz.Criado!)).Sucesso);

        await using var db = Db();
        Assert.Empty(db.Consumos);
        Assert.Empty(db.ExerciciosRealizados);
    }
}
