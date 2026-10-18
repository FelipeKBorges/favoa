using Favoa.Core.Captura;
using Favoa.Core.Edicao;
using Favoa.Core.Financas;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

/// <summary>Testa o serviço contra um SQLite real (em memória), com migrações e dados iniciais.</summary>
public sealed class FavoaServicoTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = new(2026, 10, 10);

    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private FavoaServico _servico = null!;

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection()
            .AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao))
            .BuildServiceProvider();
        await _sp.PrepararBancoComCasaAsync();
        _servico = new FavoaServico(_sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>(), new Favoa.Core.Pessoas.PessoaFixa(null));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private async Task<Favoa.Core.ResultadoRegistro> Registrar(string frase)
    {
        var ctx = await _servico.ObterContextoAsync(Hoje);
        return await _servico.RegistrarAsync(InterpretadorFrase.Interpretar(frase, ctx));
    }

    private FavoaDbContext Db() => _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>().CreateDbContext();

    [Fact]
    public async Task Despesa_cria_conta_nova_e_aparece_no_painel()
    {
        var r = await Registrar("gastei 32 no mercado com o nubank");

        Assert.True(r.Sucesso);
        Assert.Equal(TipoReferencia.Lancamento, r.Criado!.Tipo);

        var painel = await _servico.ObterPainelAsync(Hoje);
        Assert.Equal(3200, painel.Resumo.DespesasDoMesCentavos);
        Assert.Contains(painel.Recentes, x => x.Descricao == "Mercado · Nubank");
    }

    [Fact]
    public async Task Desfazer_despesa_remove_tambem_a_conta_criada_junto()
    {
        var r = await Registrar("gastei 10 no mercado com o nubakn"); // erro de digitação

        var desfeito = await _servico.DesfazerAsync(r.Criado!);

        Assert.True(desfeito.Sucesso);
        await using var db = Db();
        Assert.Empty(db.Lancamentos);
        Assert.DoesNotContain(db.Contas, c => c.Nome == "Nubakn");
        Assert.Contains(db.Categorias, c => c.Nome == "Mercado"); // categoria inicial é preservada
    }

    [Fact]
    public async Task Desfazer_orcamento_restaura_o_valor_anterior()
    {
        await Registrar("orçamento 3000");
        var segundo = await Registrar("orçamento 5000");

        await _servico.DesfazerAsync(segundo.Criado!);

        var painel = await _servico.ObterPainelAsync(Hoje);
        Assert.Equal(300000, painel.Resumo.OrcamentoMensalCentavos);
    }

    [Fact]
    public async Task Editar_lancamento_troca_valor_tipo_e_categoria()
    {
        var r = await Registrar("gastei 50 no mercado");
        var editavel = await _servico.ObterParaEdicaoAsync(r.Criado!);

        var salvo = await _servico.SalvarEdicaoAsync(editavel! with
        {
            ValorCentavos = 7550,
            TipoLancamento = TipoLancamento.Receita,
            Categoria = "Extra",
            Data = Hoje.AddDays(-1),
        });

        Assert.True(salvo.Sucesso);
        await using var db = Db();
        var l = await db.Lancamentos.Include(x => x.Categoria).SingleAsync();
        Assert.Equal(7550, l.ValorCentavos);
        Assert.Equal(TipoLancamento.Receita, l.Tipo);
        Assert.Equal("Extra", l.Categoria!.Nome);
        Assert.Equal(Hoje.AddDays(-1), l.Data);
    }

    [Fact]
    public async Task Editar_e_excluir_tarefa()
    {
        var r = await Registrar("tarefa pagar luz sexta");
        var editavel = await _servico.ObterParaEdicaoAsync(r.Criado!);

        await _servico.SalvarEdicaoAsync(editavel! with { Titulo = "Pagar conta de luz", Prazo = null });
        var depois = await _servico.ObterParaEdicaoAsync(r.Criado!);
        Assert.Equal("Pagar conta de luz", depois!.Titulo);
        Assert.Null(depois.Prazo);

        var excluido = await _servico.ExcluirAsync(r.Criado!);
        Assert.True(excluido.Sucesso);
        Assert.Null(await _servico.ObterParaEdicaoAsync(r.Criado!));
    }

    [Fact]
    public async Task Habito_marcado_pela_captura_pode_ser_desfeito()
    {
        await Registrar("novo hábito ler 20 min");
        var feito = await Registrar("fiz ler 20 min");

        var painel = await _servico.ObterPainelAsync(Hoje);
        Assert.Equal(1, painel.Resumo.HabitosFeitosHoje);

        await _servico.DesfazerAsync(feito.Criado!);
        painel = await _servico.ObterPainelAsync(Hoje);
        Assert.Equal(0, painel.Resumo.HabitosFeitosHoje);
        Assert.Single(painel.HabitosPendentes);
    }

    [Fact]
    public async Task Quadro_de_tarefas_e_mudanca_de_prazo()
    {
        var r = await Registrar("tarefa pagar luz ontem");
        await Registrar("tarefa ler livro");

        var q = await _servico.ObterQuadroTarefasAsync(Hoje);
        Assert.Single(q.Atrasadas);
        Assert.Single(q.SemPrazo);

        var mudou = await _servico.DefinirPrazoTarefaAsync(r.Criado!.Id, Hoje.AddDays(1));
        Assert.True(mudou.Sucesso);

        q = await _servico.ObterQuadroTarefasAsync(Hoje);
        Assert.Empty(q.Atrasadas);
        Assert.Equal("Pagar luz", q.Proximas.Single().Titulo);

        await _servico.AlternarTarefaAsync(r.Criado.Id, Hoje);
        q = await _servico.ObterQuadroTarefasAsync(Hoje);
        Assert.Equal(1, q.FeitasUltimos7Dias);
        Assert.Single(q.Concluidas);
    }

    [Fact]
    public async Task Habitos_marcar_dia_passado_arquivar_e_excluir()
    {
        var criado = await Registrar("novo hábito meditar");
        var id = criado.Criado!.Id;

        await _servico.AlternarHabitoAsync(id, Hoje.AddDays(-1));
        await _servico.AlternarHabitoAsync(id, Hoje);
        var q = await _servico.ObterQuadroHabitosAsync(Hoje);
        Assert.Equal(2, q.Ativos.Single().Estatistica.SequenciaAtual);

        await _servico.DefinirHabitoAtivoAsync(id, false);
        var painel = await _servico.ObterPainelAsync(Hoje);
        Assert.Equal(0, painel.Resumo.HabitosAtivos);
        Assert.Single((await _servico.ObterQuadroHabitosAsync(Hoje)).Arquivados);

        var excluido = await _servico.ExcluirAsync(new ReferenciaRegistro(TipoReferencia.Habito, id));
        Assert.True(excluido.Sucesso);
        await using var db = Db();
        Assert.Empty(db.RegistrosHabito);
    }

    [Fact]
    public async Task Editar_valor_invalido_e_recusado()
    {
        var r = await Registrar("bebi 500 ml");
        var editavel = await _servico.ObterParaEdicaoAsync(r.Criado!);

        var resultado = await _servico.SalvarEdicaoAsync(editavel! with { Mililitros = 0 });

        Assert.False(resultado.Sucesso);
    }
}

/// <summary>Extrato da tela de Finanças.</summary>
public sealed class ExtratoTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = new(2026, 10, 10);

    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private FavoaServico _servico = null!;

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection()
            .AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao))
            .BuildServiceProvider();
        await _sp.PrepararBancoComCasaAsync();
        _servico = new FavoaServico(_sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>(), new Favoa.Core.Pessoas.PessoaFixa(null));

        await Registrar("orçamento 3100");
        await Registrar("gastei 100 no mercado com o nubank", Hoje);
        await Registrar("gastei 40 em transporte", Hoje);
        await Registrar("gastei 60 no mercado", Hoje.AddDays(-2));
        await Registrar("recebi 5000 de salário", Hoje.AddDays(-5));
        await Registrar("gastei 999 no mercado", new DateOnly(2026, 9, 5));   // mês anterior, até o dia 10
        await Registrar("gastei 111 no mercado", new DateOnly(2026, 9, 20));  // mês anterior, depois do dia 10
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private async Task Registrar(string frase, DateOnly? dia = null)
    {
        var ctx = await _servico.ObterContextoAsync(dia ?? Hoje);
        var r = await _servico.RegistrarAsync(InterpretadorFrase.Interpretar(frase, ctx));
        Assert.True(r.Sucesso, r.Mensagem);
    }

    private Task<ExtratoMes> Extrato(FiltroExtrato? f = null) =>
        _servico.ObterExtratoAsync(f ?? FiltroExtrato.DoMes(Hoje), Hoje);

    [Fact]
    public async Task Agrupa_por_dia_do_mais_recente_e_soma_totais()
    {
        var e = await Extrato();

        Assert.Equal([Hoje, Hoje.AddDays(-2), Hoje.AddDays(-5)], e.Dias.Select(d => d.Data));
        Assert.Equal(20000, e.DespesasCentavos);
        Assert.Equal(500000, e.ReceitasCentavos);
        Assert.Equal(480000, e.SaldoCentavos);
        Assert.Equal(-14000, e.Dias[0].SaldoCentavos);
    }

    [Fact]
    public async Task Fatias_por_categoria_ordenadas_e_proporcionais()
    {
        var e = await Extrato();

        Assert.Equal(["Mercado", "Transporte"], e.DespesasPorCategoria.Select(x => x.Nome));
        Assert.Equal(0.8, e.DespesasPorCategoria[0].Fracao, 3);
    }

    [Fact]
    public async Task Filtro_de_categoria_reduz_lista_mas_mantem_fatias()
    {
        var mercado = (await Extrato()).Categorias.Single(c => c.Nome == "Mercado").Id;

        var e = await Extrato(FiltroExtrato.DoMes(Hoje) with { CategoriaId = mercado });

        Assert.Equal(2, e.QuantidadeLancamentos);
        Assert.Equal(16000, e.DespesasCentavos);
        Assert.Equal(2, e.DespesasPorCategoria.Count);
    }

    [Fact]
    public async Task Filtros_de_tipo_conta_e_busca()
    {
        var nubank = (await Extrato()).Contas.Single(c => c.Nome == "Nubank").Id;

        Assert.Equal(1, (await Extrato(FiltroExtrato.DoMes(Hoje) with { Tipo = TipoLancamento.Receita })).QuantidadeLancamentos);
        Assert.Equal(1, (await Extrato(FiltroExtrato.DoMes(Hoje) with { ContaId = nubank })).QuantidadeLancamentos);
        Assert.Equal(1, (await Extrato(FiltroExtrato.DoMes(Hoje) with { Busca = "TRANSPÓRTE" })).QuantidadeLancamentos);
    }

    [Fact]
    public async Task Ritmo_usa_todas_as_despesas_mesmo_com_filtro()
    {
        var e = await Extrato(FiltroExtrato.DoMes(Hoje) with { Tipo = TipoLancamento.Receita });

        Assert.NotNull(e.Ritmo);
        Assert.Equal(20000, e.Ritmo!.GastoCentavos);
        Assert.Equal(SituacaoOrcamento.Folga, e.Ritmo.Situacao);
    }

    [Fact]
    public async Task Mes_anterior_compara_ate_o_mesmo_dia()
    {
        Assert.Equal(99900, (await Extrato()).DespesasMesAnteriorCentavos);

        // Para um mês fechado, compara com o anterior inteiro.
        var novembro = await _servico.ObterExtratoAsync(new FiltroExtrato(2026, 11), Hoje);
        Assert.Equal(20000, novembro.DespesasMesAnteriorCentavos);
    }

    [Fact]
    public async Task Mes_sem_lancamentos_vem_vazio()
    {
        var e = await _servico.ObterExtratoAsync(new FiltroExtrato(2026, 12), Hoje);

        Assert.Empty(e.Dias);
        Assert.Equal(0, e.SaldoCentavos);
    }
}

/// <summary>Bancos criados pela primeira versão (EnsureCreated) precisam continuar funcionando.</summary>
public sealed class AdocaoBancoAntigoTests
{
    [Fact]
    public async Task Banco_criado_sem_migracoes_e_adotado_sem_perder_dados()
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"favoa-teste-{Guid.NewGuid():N}.db");
        try
        {
            var opcoes = new DbContextOptionsBuilder<FavoaDbContext>().UseSqlite($"Data Source={arquivo}").Options;

            // Simula a v1: só as tabelas da migração Inicial, sem histórico (como o EnsureCreated deixava).
            await using (var antigo = new FavoaDbContext(opcoes))
            {
                var migrador = antigo.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
                await migrador.MigrateAsync(antigo.Database.GetMigrations().First());
                await antigo.Database.ExecuteSqlRawAsync("DROP TABLE __EFMigrationsHistory");
                var conta = new Conta { Nome = "Carteira" };
                var cat = new Categoria { Nome = "Mercado" };
                await antigo.Database.ExecuteSqlRawAsync(
                    "INSERT INTO fin_conta (Id, Nome, Ativa, CriadoEm) VALUES ({0}, 'Carteira', 1, '2026-10-10');" +
                    "INSERT INTO fin_categoria (Id, Nome, Tipo, CriadoEm) VALUES ({1}, 'Mercado', 0, '2026-10-10');" +
                    "INSERT INTO fin_lancamento (Id, ContaId, CategoriaId, Tipo, ValorCentavos, Data, Descricao, CriadoEm) VALUES ({2}, {0}, {1}, 0, 999, '2026-10-10', '', '2026-10-10');",
                    conta.Id, cat.Id, Guid.NewGuid());
            }

            var sp = new ServiceCollection()
                .AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite($"Data Source={arquivo}"))
                .BuildServiceProvider();
            await sp.PrepararBancoComCasaAsync();

            await using var db = await sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>().CreateDbContextAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal(999, (await db.Lancamentos.SingleAsync()).ValorCentavos);
            Assert.True(await db.Alimentos.CountAsync() > 500); // a migração Fitness rodou e a TACO foi carregada
            await sp.DisposeAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(arquivo);
        }
    }
}
