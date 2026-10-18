using Favoa.Core.Captura;
using Favoa.Core.Edicao;
using Favoa.Core.Pessoas;
using Favoa.Core.Rotina;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Favoa.Tests;

/// <summary>Tarefa com horário, lembrete antes e mais de um responsável.</summary>
public sealed class TarefaHorarioTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.Now);
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private IDbContextFactory<FavoaDbContext> _fabrica = null!;
    private AcessoServico _acesso = null!;
    private Guid _felipe, _gab;

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao)).BuildServiceProvider();
        await _sp.PrepararBancoComCasaAsync();
        _fabrica = _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        _acesso = new AcessoServico(_fabrica);
        await _acesso.SalvarPessoaAsync(await CasaDeTeste.FamiliaIdAsync(_fabrica), null, "Eva", "#B9A3E3");
        var pessoas = (await _acesso.ObterEstadoAsync(null)).Pessoas;
        _felipe = pessoas.Single(p => p.Nome == "Davi").Id;
        _gab = pessoas.Single(p => p.Nome == "Eva").Id;
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private FavoaServico Favoa(Guid pessoa) => new(_fabrica, new PessoaFixa(pessoa));

    /// <summary>O agendador roda sem pessoa: enxerga a casa toda.</summary>
    private LembretesServico Agendador() => new(_fabrica, new PessoaFixa(null), new OpcoesFavoa(), NullLogger<LembretesServico>.Instance);

    private async Task<Interpretacao> Frase(string frase, Guid? pessoa = null) =>
        InterpretadorFrase.Interpretar(frase, await Favoa(pessoa ?? _felipe).ObterContextoAsync(Hoje));

    private async Task<Guid> Registrar(string frase, Guid? pessoa = null)
    {
        var r = await Favoa(pessoa ?? _felipe).RegistrarAsync(await Frase(frase, pessoa));
        Assert.True(r.Sucesso, r.Mensagem);
        return r.Criado!.Id;
    }

    // ---------- Frase ----------

    [Fact]
    public async Task Frase_com_dia_hora_aviso_e_pessoa()
    {
        var i = await Frase("tarefa dentista amanhã às 15h avisar 1h antes @eva");
        Assert.True(i.PodeSalvar, i.Aviso);
        Assert.Equal("Dentista", i.Titulo);
        Assert.Equal(Hoje.AddDays(1), i.Prazo);
        Assert.Equal(new TimeOnly(15, 0), i.Hora);
        Assert.Equal(60, i.AvisoMinutos);
        Assert.Equal([_gab], i.Responsaveis);
    }

    [Theory]
    [InlineData("tarefa ligar no banco 9h30", 9, 30)]
    [InlineData("tarefa ligar no banco as 14:15", 14, 15)]
    [InlineData("tarefa ligar no banco às 8h", 8, 0)]
    public async Task Hora_sem_dia_vale_para_hoje(string frase, int h, int m)
    {
        var i = await Frase(frase);
        Assert.True(i.PodeSalvar, i.Aviso);
        Assert.Equal("Ligar no banco", i.Titulo);
        Assert.Equal(Hoje, i.Prazo);
        Assert.Equal(new TimeOnly(h, m), i.Hora);
        Assert.Null(i.AvisoMinutos);
    }

    [Theory]
    [InlineData("tarefa reunião sexta 10h lembrar 15 min antes", 15)]
    [InlineData("tarefa reunião sexta 10h 2 horas antes", 120)]
    [InlineData("tarefa reunião sexta 10h avisar 1 dia antes", 1440)]
    [InlineData("tarefa reunião sexta 10h avisar na hora", 0)]
    public async Task Antecedencia_do_aviso(string frase, int minutos)
    {
        var i = await Frase(frase);
        Assert.True(i.PodeSalvar, i.Aviso);
        Assert.Equal("Reunião", i.Titulo);
        Assert.Equal(new TimeOnly(10, 0), i.Hora);
        Assert.Equal(minutos, i.AvisoMinutos);
    }

    [Fact]
    public async Task Aviso_sem_horario_pede_o_horario()
    {
        var i = await Frase("tarefa pagar luz sexta avisar 30 min antes");
        Assert.False(i.PodeSalvar);
        Assert.Contains("horário", i.Aviso);
    }

    [Fact]
    public async Task Varias_pessoas_e_pessoa_desconhecida()
    {
        var duas = await Frase("tarefa limpar a casa sábado @davi @eva");
        Assert.True(duas.PodeSalvar, duas.Aviso);
        Assert.Equal("Limpar a casa", duas.Titulo);
        Assert.Equal([_felipe, _gab], duas.Responsaveis);

        var desconhecida = await Frase("tarefa limpar a casa @ze");
        Assert.False(desconhecida.PodeSalvar);
        Assert.Contains("@ze", desconhecida.Aviso);
        Assert.Contains("@Eva", desconhecida.Aviso);
    }

    [Fact]
    public async Task Frase_que_nao_e_tarefa_nao_perde_o_horario()
    {
        var i = await Frase("corrida 30 min 5 km");
        Assert.Equal(TipoCaptura.Cardio, i.Tipo);
        Assert.Null(i.Hora);
    }

    // ---------- Regra do aviso ----------

    [Fact]
    public void Aviso_sai_na_janela_e_nao_depois_da_hora()
    {
        var t = new Tarefa { Titulo = "Dentista", Prazo = Hoje, Hora = new TimeOnly(15, 0), AvisoMinutos = 30 };
        var dia = Hoje.ToDateTime(TimeOnly.MinValue);
        Assert.Equal(AvisoTarefa.Situacao.Nenhum, AvisoTarefa.Verificar(t, dia.AddHours(14).AddMinutes(29)));
        Assert.Equal(AvisoTarefa.Situacao.Avisar, AvisoTarefa.Verificar(t, dia.AddHours(14).AddMinutes(30)));
        Assert.Equal(AvisoTarefa.Situacao.Avisar, AvisoTarefa.Verificar(t, dia.AddHours(15).AddMinutes(10)));
        Assert.Equal(AvisoTarefa.Situacao.Perdido, AvisoTarefa.Verificar(t, dia.AddHours(16)));
        Assert.Equal("às 15:00 (em 30 min)", AvisoTarefa.Quando(t, dia.AddHours(14).AddMinutes(30)));

        t.AvisoEnviadoEm = DateTime.UtcNow;
        Assert.Equal(AvisoTarefa.Situacao.Nenhum, AvisoTarefa.Verificar(t, dia.AddHours(14).AddMinutes(45)));
        t.AvisoEnviadoEm = null;
        t.ConcluidaEm = Hoje;
        Assert.Equal(AvisoTarefa.Situacao.Nenhum, AvisoTarefa.Verificar(t, dia.AddHours(14).AddMinutes(45)));
    }

    [Fact]
    public void Rotulos_do_aviso()
    {
        Assert.Equal("Na hora", AvisoTarefa.Rotulo(0));
        Assert.Equal("15 min antes", AvisoTarefa.Rotulo(15));
        Assert.Equal("2 h antes", AvisoTarefa.Rotulo(120));
        Assert.Equal("1 dia antes", AvisoTarefa.Rotulo(1440));
        Assert.Equal("Para ter horário, escolha o dia da tarefa.", AvisoTarefa.Validar(null, new TimeOnly(9, 0), null));
    }

    // ---------- Serviço ----------

    [Fact]
    public async Task Quadro_mostra_horario_e_responsaveis_e_os_dois_veem()
    {
        await Registrar("tarefa mercado hoje às 18h @eva");
        await Registrar("tarefa academia hoje às 7h");
        await Registrar("tarefa pagar luz hoje @davi");

        var quadro = await Favoa(_gab).ObterQuadroTarefasAsync(Hoje);
        Assert.Equal(["Academia", "Mercado", "Pagar luz"], quadro.ParaHoje.Select(t => t.Titulo)); // com horário primeiro, por hora
        var mercado = quadro.ParaHoje.Single(t => t.Titulo == "Mercado");
        Assert.Equal(new TimeOnly(18, 0), mercado.Hora);
        Assert.Equal("Eva", Assert.Single(mercado.Responsaveis).Nome);

        // "Minhas e da casa" da Eva: a dela e a da casa, não a do Davi
        Assert.Equal(["Academia", "Mercado"], quadro.ParaHoje.Where(t => t.EhDe(_gab)).Select(t => t.Titulo));
    }

    [Fact]
    public async Task Editar_troca_responsaveis_e_reabre_o_aviso()
    {
        var id = await Registrar("tarefa dentista amanhã às 15h avisar 30 min antes @eva");
        await using (var db = await _fabrica.CreateDbContextAsync())
            await db.Tarefas.Where(t => t.Id == id).ExecuteUpdateAsync(u => u.SetProperty(t => t.AvisoEnviadoEm, DateTime.UtcNow));

        var s = Favoa(_felipe);
        var ref_ = new ReferenciaRegistro(TipoReferencia.Tarefa, id);
        var e = (await s.ObterParaEdicaoAsync(ref_))!;
        Assert.Equal(new TimeOnly(15, 0), e.Hora);
        Assert.Equal(30, e.AvisoMinutos);
        Assert.Equal([_gab], e.Responsaveis);
        Assert.Equal(2, e.PessoasDisponiveis.Count);

        var r = await s.SalvarEdicaoAsync(e with { Hora = new TimeOnly(16, 30), Responsaveis = [_felipe, _gab] });
        Assert.True(r.Sucesso, r.Mensagem);

        await using var db2 = await _fabrica.CreateDbContextAsync();
        var t = await db2.Tarefas.Include(x => x.Responsaveis).SingleAsync();
        Assert.Equal(new TimeOnly(16, 30), t.Hora);
        Assert.Null(t.AvisoEnviadoEm);
        Assert.Equal(2, t.Responsaveis.Count);

        // Aviso sem horário não salva
        Assert.False((await s.SalvarEdicaoAsync(e with { Hora = null, AvisoMinutos = 15 })).Sucesso);
        // Tirar o prazo tira também o horário
        await s.DefinirPrazoTarefaAsync(id, null);
        await using var db3 = await _fabrica.CreateDbContextAsync();
        Assert.Null((await db3.Tarefas.SingleAsync()).Hora);
    }

    [Fact]
    public async Task Agendador_marca_o_aviso_na_hora_certa_e_pula_o_atrasado()
    {
        var agora = DateTime.Now;
        var daqui20 = agora.AddMinutes(20);
        // Aviso 30 min antes de daqui a 20 min: já é hora de avisar
        var s = Favoa(_felipe);
        var ctx = await s.ObterContextoAsync(Hoje);
        async Task<Guid> Criar(string titulo, DateTime quando, int aviso)
        {
            var i = InterpretadorFrase.Interpretar("tarefa " + titulo, ctx) with
            {
                Prazo = DateOnly.FromDateTime(quando), Hora = new TimeOnly(quando.Hour, quando.Minute), AvisoMinutos = aviso,
            };
            var r = await s.RegistrarAsync(i);
            Assert.True(r.Sucesso, r.Mensagem);
            return r.Criado!.Id;
        }
        var agoraSim = await Criar("ligar", daqui20, 30);
        var depois = await Criar("buscar", agora.AddHours(3), 30);
        var passou = await Criar("passou", agora.AddHours(-2), 15);

        await Agendador().DispararDevidosAsync(agora);

        await using var db = await _fabrica.CreateDbContextAsync();
        var tarefas = await db.Tarefas.ToDictionaryAsync(t => t.Id);
        Assert.NotNull(tarefas[agoraSim].AvisoEnviadoEm);
        Assert.Null(tarefas[depois].AvisoEnviadoEm);
        Assert.NotNull(tarefas[passou].AvisoEnviadoEm); // marcado sem mandar, para não avisar fora de hora

        // Não repete no próximo ciclo
        var marca = tarefas[agoraSim].AvisoEnviadoEm;
        await Agendador().DispararDevidosAsync(agora.AddMinutes(1));
        await using var db2 = await _fabrica.CreateDbContextAsync();
        Assert.Equal(marca, (await db2.Tarefas.FindAsync(agoraSim))!.AvisoEnviadoEm);
    }

    [Fact]
    public async Task Pessoa_que_sai_da_casa_deixa_de_ser_responsavel()
    {
        await Registrar("tarefa lavar o carro sábado @eva @davi");
        Assert.True((await _acesso.ExcluirPessoaAsync(await CasaDeTeste.FamiliaIdAsync(_fabrica), _gab)).Sucesso);
        await using var db = await _fabrica.CreateDbContextAsync();
        var t = await db.Tarefas.Include(x => x.Responsaveis).SingleAsync();
        Assert.Equal(_felipe, Assert.Single(t.Responsaveis).PessoaId);
    }

    [Fact]
    public async Task Exportar_e_importar_leva_horario_aviso_e_responsaveis()
    {
        await Registrar("tarefa dentista amanhã às 15h avisar 1h antes @eva @davi");
        var dados = new DadosFavoa(_fabrica, new OpcoesFavoa { PastaDados = Directory.CreateTempSubdirectory("favoa-tar-").FullName });
        var arquivo = (await dados.ExportarJsonAsync()).Conteudo;
        Assert.True((await dados.ImportarJsonAsync(arquivo)).Sucesso);

        var t = Assert.Single((await Favoa(_gab).ObterQuadroTarefasAsync(Hoje)).Proximas);
        Assert.Equal(new TimeOnly(15, 0), t.Hora);
        Assert.Equal(60, t.AvisoMinutos);
        Assert.Equal(["Davi", "Eva"], t.Responsaveis.Select(p => p.Nome));

        // Começar do zero apaga as tarefas e os responsáveis junto
        Assert.True((await dados.ComecarDoZeroAsync()).Sucesso);
        await using var db = await _fabrica.CreateDbContextAsync();
        Assert.Equal(0, await db.ResponsaveisTarefa.CountAsync());
    }

    [Fact]
    public async Task Agendador_enxerga_aparelhos_e_lembretes_mesmo_com_a_pessoa_vazia_da_web()
    {
        // Na web, fora de um pedido, a pessoa atual é Guid.Empty (fecha tudo de propósito). O agendador não pode depender dela.
        var agora = DateTime.Now;
        await using (var db = await _fabrica.CreateDbContextAsync())
        {
            db.Lembretes.Add(new Lembrete { PessoaId = _gab, Titulo = "Beber água", Hora = new TimeOnly(agora.AddMinutes(-1).Hour, agora.AddMinutes(-1).Minute),
                Dias = DiasSemana.Todos, UltimoDisparo = Hoje.AddDays(-1) });
            // Aparelho que não responde: o resultado diz "falharam" (achou o aparelho), não "nenhum aparelho".
            db.AssinaturasPush.Add(new AssinaturaPush { PessoaId = _gab, Endpoint = "https://127.0.0.1:1/eva", P256dh = Chave65(), Auth = "AAAAAAAAAAAAAAAAAAAAAA", Aparelho = "Teste" });
            await db.SaveChangesAsync();
        }
        var sp = new ServiceCollection()
            .AddSingleton(_fabrica).AddSingleton(new OpcoesFavoa()).AddLogging()
            .AddScoped<IPessoaAtual>(_ => new PessoaFixa(Guid.Empty))
            .BuildServiceProvider();

        await AgendadorFavoa.DispararLembretesAsync(sp.GetRequiredService<IServiceScopeFactory>(), agora);

        await using var db2 = await _fabrica.CreateDbContextAsync();
        var l = await db2.Lembretes.SingleAsync();
        Assert.Equal(Hoje, l.UltimoDisparo);
        Assert.DoesNotContain("nenhum aparelho", l.UltimoResultado);
    }

    [Fact]
    public async Task Lembretes_mostra_os_proximos_avisos_de_tarefa_da_pessoa_e_da_casa()
    {
        await Registrar("tarefa aula de inglês amanhã às 18:30 avisar 30 min antes");
        await Registrar("tarefa dentista amanhã às 9h avisar 1h antes @eva");
        await Registrar("tarefa reunião amanhã às 10h avisar 15 min antes @davi");
        await Registrar("tarefa sem aviso amanhã às 11h");

        var daGab = (await new LembretesServico(_fabrica, new PessoaFixa(_gab), new OpcoesFavoa(), NullLogger<LembretesServico>.Instance).ObterAsync()).AvisosTarefas;
        Assert.Equal(["Dentista", "Aula de inglês"], daGab.Select(a => a.Titulo));
        Assert.Equal(Hoje.AddDays(1).ToDateTime(new TimeOnly(18, 0)), daGab[1].Quando);
        Assert.Equal("todos da casa", daGab[1].Para);
        Assert.Equal("Eva", daGab[0].Para);
    }

    /// <summary>Uma chave pública P-256 válida (65 bytes), como a que o navegador manda.</summary>
    private static string Chave65()
    {
        using var ec = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(false).Q;
        return WebPush.Base64Url([0x04, .. p.X!, .. p.Y!]);
    }
}
