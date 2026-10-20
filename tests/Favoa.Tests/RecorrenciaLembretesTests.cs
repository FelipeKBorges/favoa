using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Favoa.Core;
using Favoa.Core.Captura;
using Favoa.Core.Edicao;
using Favoa.Core.Financas;
using Favoa.Core.Rotina;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Favoa.Tests;

public class CalendarioRecorrenciaTests
{
    [Theory]
    [InlineData(2026, 2, 31, 28)]
    [InlineData(2028, 2, 30, 29)]
    [InlineData(2026, 11, 31, 30)]
    [InlineData(2026, 10, 5, 5)]
    public void Dia_inexistente_cai_no_ultimo_dia(int ano, int mes, int dia, int esperado) =>
        Assert.Equal(esperado, CalendarioRecorrencia.DataNoMes(ano, mes, dia).Day);

    [Fact]
    public void Pendentes_lanca_meses_atrasados_uma_vez()
    {
        var datas = CalendarioRecorrencia.Pendentes(5, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 5), new DateOnly(2026, 10, 10)).ToList();
        Assert.Equal([new DateOnly(2026, 9, 5), new DateOnly(2026, 10, 5)], datas);
    }

    [Fact]
    public void Pendentes_respeita_o_inicio_e_o_dia_que_ainda_nao_chegou()
    {
        Assert.Empty(CalendarioRecorrencia.Pendentes(15, new DateOnly(2026, 10, 15), null, new DateOnly(2026, 10, 10)));
        Assert.Equal([new DateOnly(2026, 10, 10)], CalendarioRecorrencia.Pendentes(10, new DateOnly(2026, 10, 10), null, new DateOnly(2026, 10, 10)));
    }

    [Fact]
    public void Proxima_e_primeira_a_partir_de_hoje()
    {
        var hoje = new DateOnly(2026, 10, 10);
        Assert.Equal(new DateOnly(2026, 11, 5), CalendarioRecorrencia.Proxima(5, new DateOnly(2026, 1, 1), hoje));
        Assert.Equal(new DateOnly(2026, 10, 20), CalendarioRecorrencia.Proxima(20, new DateOnly(2026, 1, 1), hoje));
        Assert.Equal(new DateOnly(2026, 11, 10), CalendarioRecorrencia.Proxima(10, new DateOnly(2026, 1, 1), hoje)); // hoje já foi lançado
        Assert.Equal(new DateOnly(2026, 10, 10), CalendarioRecorrencia.PrimeiraAPartirDeHoje(10, hoje));
        Assert.Equal(new DateOnly(2026, 11, 5), CalendarioRecorrencia.PrimeiraAPartirDeHoje(5, hoje));
    }
}

public class AgendaLembretesTests
{
    // Sábado, 10/10/2026
    private static Lembrete L(int h, int m, DiasSemana dias = DiasSemana.Todos, DateOnly? ultimo = null) =>
        new() { Titulo = "x", Hora = new TimeOnly(h, m), Dias = dias, UltimoDisparo = ultimo };

    [Fact]
    public void Avisa_na_hora_e_nao_repete_no_mesmo_dia()
    {
        var agora = new DateTime(2026, 10, 10, 8, 0, 20);
        Assert.Single(AgendaLembretes.Devidos([L(8, 0)], agora), x => x.Avisar);
        Assert.Empty(AgendaLembretes.Devidos([L(8, 0, ultimo: new DateOnly(2026, 10, 10))], agora));
        Assert.Empty(AgendaLembretes.Devidos([L(8, 1)], agora));
    }

    [Fact]
    public void Respeita_os_dias_da_semana()
    {
        var sabado = new DateTime(2026, 10, 10, 9, 0, 0);
        Assert.Empty(AgendaLembretes.Devidos([L(8, 0, DiasSemana.Uteis)], sabado));
        Assert.Single(AgendaLembretes.Devidos([L(8, 0, DiasSemana.Sabado | DiasSemana.Domingo)], sabado));
    }

    [Fact]
    public void Atrasado_demais_so_marca_o_dia()
    {
        var (_, avisar) = Assert.Single(AgendaLembretes.Devidos([L(6, 0)], new DateTime(2026, 10, 10, 9, 0, 0)));
        Assert.False(avisar);
    }

    [Fact]
    public void Marca_ao_salvar_so_quando_o_horario_ja_passou()
    {
        var agora = new DateTime(2026, 10, 10, 22, 10, 0);
        Assert.Equal(new DateOnly(2026, 10, 10), AgendaLembretes.MarcaAoSalvar(new TimeOnly(22, 5), agora));
        Assert.Null(AgendaLembretes.MarcaAoSalvar(new TimeOnly(22, 30), agora));
    }

    [Fact]
    public void Proximo_aviso_respeita_dias_e_o_que_ja_saiu_hoje()
    {
        var sabado = new DateTime(2026, 10, 10, 22, 10, 0);
        Assert.Equal(new DateTime(2026, 10, 10, 22, 30, 0), AgendaLembretes.Proximo(L(22, 30), sabado));
        Assert.Equal(new DateTime(2026, 10, 11, 22, 30, 0), AgendaLembretes.Proximo(L(22, 30, ultimo: new DateOnly(2026, 10, 10)), sabado));
        Assert.Equal(new DateTime(2026, 10, 12, 8, 0, 0), AgendaLembretes.Proximo(L(8, 0, DiasSemana.Uteis), sabado));
        Assert.Null(AgendaLembretes.Proximo(new Lembrete { Hora = new TimeOnly(8, 0), Ativo = false }, sabado));
        Assert.Equal("amanhã às 22:30", AgendaLembretes.Quando(new DateTime(2026, 10, 11, 22, 30, 0), sabado));
    }

    [Fact]
    public void Descreve_os_dias() =>
        Assert.Equal(["Todo dia", "Dias úteis", "Fim de semana", "Seg, Qua"], new[]
        {
            DiasSemana.Todos, DiasSemana.Uteis, DiasSemana.Sabado | DiasSemana.Domingo, DiasSemana.Segunda | DiasSemana.Quarta,
        }.Select(AgendaLembretes.DescreverDias));
}

public class WebPushTests
{
    [Fact]
    public void Mensagem_cifrada_abre_do_lado_do_aparelho()
    {
        // O "aparelho": par de chaves e segredo como o navegador cria.
        using var aparelho = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = aparelho.ExportParameters(false).Q;
        var publicaAparelho = new byte[] { 4 }.Concat(q.X!).Concat(q.Y!).ToArray();
        var auth = RandomNumberGenerator.GetBytes(16);
        var mensagem = Encoding.UTF8.GetBytes("{\"title\":\"Beber água\"}");

        var corpo = WebPush.Criptografar(mensagem, publicaAparelho, auth);

        // Decifra seguindo a RFC 8291, como o navegador faria.
        var salt = corpo[..16];
        Assert.Equal(4096u, BinaryPrimitives.ReadUInt32BigEndian(corpo.AsSpan(16)));
        Assert.Equal(65, corpo[20]);
        var publicaServidor = corpo[21..86];
        using var servidor = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = publicaServidor[1..33], Y = publicaServidor[33..65] },
        });
        var segredo = aparelho.DeriveRawSecretAgreement(servidor.PublicKey);
        var prkChave = HMACSHA256.HashData(auth, segredo);
        var info = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(publicaAparelho).Concat(publicaServidor).Append((byte)1).ToArray();
        var ikm = HMACSHA256.HashData(prkChave, info);
        var prk = HMACSHA256.HashData(salt, ikm);
        var cek = HMACSHA256.HashData(prk, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0").Append((byte)1).ToArray())[..16];
        var nonce = HMACSHA256.HashData(prk, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0").Append((byte)1).ToArray())[..12];

        var cifrado = corpo[86..^16];
        var tag = corpo[^16..];
        var claro = new byte[cifrado.Length];
        using (var aes = new AesGcm(cek, 16)) aes.Decrypt(nonce, cifrado, tag, claro);

        Assert.Equal(2, claro[^1]);
        Assert.Equal(mensagem, claro[..^1]);
    }

    [Fact]
    public void Vapid_tem_jwt_assinado_que_confere_com_a_chave()
    {
        var (publica, privada) = WebPush.GerarChavesVapid();
        var cabecalho = WebPush.CabecalhoVapid("https://web.push.apple.com/abc", publica, privada, "mailto:x@example.com",
            new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.StartsWith("vapid t=", cabecalho);
        Assert.EndsWith($", k={publica}", cabecalho);
        var jwt = cabecalho["vapid t=".Length..cabecalho.IndexOf(',')];
        var partes = jwt.Split('.');
        var corpo = Encoding.UTF8.GetString(WebPush.DeBase64Url(partes[1]));
        Assert.Contains("\"aud\":\"https://web.push.apple.com\"", corpo);
        Assert.Contains("\"sub\":\"mailto:x@example.com\"", corpo);

        var p = WebPush.DeBase64Url(publica);
        using var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = p[1..33], Y = p[33..65] } });
        Assert.True(ec.VerifyData(Encoding.ASCII.GetBytes($"{partes[0]}.{partes[1]}"), WebPush.DeBase64Url(partes[2]),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "iPhone · Safari")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 Edg/130.0", "Windows · Edge")]
    [InlineData(null, "Aparelho")]
    public void Nome_do_aparelho(string? agente, string esperado) => Assert.Equal(esperado, LembretesServico.NomeAparelho(agente));
}

/// <summary>Cadastros, recorrências e orçamento por categoria contra SQLite real em memória.</summary>
public sealed class CadastrosServicoTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = new(2026, 10, 10);
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private CadastrosServico _cadastros = null!;
    private FavoaServico _favoa = null!;

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao)).BuildServiceProvider();
        await _sp.PrepararBancoComCasaAsync();
        var fabrica = _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        _cadastros = new CadastrosServico(fabrica, new Favoa.Core.Pessoas.PessoaFixa(null));
        _favoa = new FavoaServico(fabrica, new Favoa.Core.Pessoas.PessoaFixa(null));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private FavoaDbContext Db() => _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>().CreateDbContext();

    private Task<ResultadoRegistro> Lancar(TipoCaptura tipo, long centavos, string categoria, DateOnly data, bool repetir = false, string? descricao = null) =>
        _favoa.RegistrarAsync(new Interpretacao(tipo, "") { ValorCentavos = centavos, Categoria = categoria, Data = data, RepetirTodoMes = repetir, Descricao = descricao });

    [Fact]
    public async Task Recorrencia_lanca_hoje_e_nos_meses_seguintes_sem_repetir()
    {
        var r = await _cadastros.SalvarRecorrenciaAsync(new DadosRecorrencia(null, TipoLancamento.Despesa, "aluguel", 180000, "Moradia", null, 10, Hoje), Hoje);
        Assert.True(r.Sucesso);
        Assert.Contains("Já lancei", r.Mensagem);

        Assert.Equal(0, await _cadastros.ProcessarRecorrenciasAsync(Hoje));
        Assert.Equal(1, await _cadastros.ProcessarRecorrenciasAsync(new DateOnly(2026, 11, 10)));
        Assert.Equal(2, await _cadastros.ProcessarRecorrenciasAsync(new DateOnly(2027, 1, 15))); // PC desligado por dois meses

        await using var db = Db();
        var datas = await db.Lancamentos.Where(l => l.RecorrenciaId != null).OrderBy(l => l.Data).Select(l => l.Data).ToListAsync();
        Assert.Equal([Hoje, new(2026, 11, 10), new(2026, 12, 10), new(2027, 1, 10)], datas);
        Assert.All(await db.Lancamentos.ToListAsync(), l => Assert.Equal("Aluguel", l.Descricao));
    }

    [Fact]
    public async Task Lancamento_apagado_nao_volta_e_pausa_nao_lanca_os_meses_parados()
    {
        await _cadastros.SalvarRecorrenciaAsync(new DadosRecorrencia(null, TipoLancamento.Despesa, "Internet", 12000, "Moradia", null, 5, new DateOnly(2026, 11, 5)), Hoje);
        await _cadastros.ProcessarRecorrenciasAsync(new DateOnly(2026, 11, 5));
        await using (var db = Db())
        {
            db.Lancamentos.RemoveRange(db.Lancamentos);
            await db.SaveChangesAsync();
        }
        Assert.Equal(0, await _cadastros.ProcessarRecorrenciasAsync(new DateOnly(2026, 11, 20)));

        var id = (await _cadastros.ObterAsync(Hoje)).Recorrencias.Single().Id;
        await _cadastros.DefinirRecorrenciaAtivaAsync(id, false, new DateOnly(2026, 11, 20));
        Assert.Equal(0, await _cadastros.ProcessarRecorrenciasAsync(new DateOnly(2027, 1, 20)));
        await _cadastros.DefinirRecorrenciaAtivaAsync(id, true, new DateOnly(2027, 1, 20));
        Assert.Equal(0, await _cadastros.ProcessarRecorrenciasAsync(new DateOnly(2027, 1, 20)));
        Assert.Equal(1, await _cadastros.ProcessarRecorrenciasAsync(new DateOnly(2027, 2, 5)));
    }

    [Fact]
    public async Task Repetir_todo_mes_no_formulario_cria_recorrencia_e_desfazer_tira_ela()
    {
        var r = await Lancar(TipoCaptura.Despesa, 5590, "Assinaturas", Hoje, repetir: true, descricao: "Netflix");
        Assert.Contains("todo dia 10", r.Mensagem);
        Assert.Equal(0, await _cadastros.ProcessarRecorrenciasAsync(Hoje)); // o de hoje é o próprio lançamento
        Assert.Equal(1, await _cadastros.ProcessarRecorrenciasAsync(new DateOnly(2026, 11, 10)));

        await using (var db0 = Db())
            Assert.Equal("Assinaturas", (await db0.Recorrencias.SingleAsync()).Descricao); // sem descrição livre: leva o nome da categoria

        var r2 = await Lancar(TipoCaptura.Despesa, 2000, "Mercado", Hoje, repetir: true);
        await _favoa.DesfazerAsync(r2.Criado!);
        await using var db = Db();
        Assert.Single(await db.Recorrencias.ToListAsync());
    }

    [Fact]
    public async Task Lancamento_so_entra_em_categoria_cadastrada()
    {
        var r = await Lancar(TipoCaptura.Despesa, 4500, "Ifood", Hoje);
        Assert.False(r.Sucesso);
        Assert.Contains("A categoria Ifood não existe", r.Mensagem);
        Assert.Contains("Mercado", r.Mensagem);
        await using (var db = Db())
        {
            Assert.False(await db.Categorias.AnyAsync(c => c.Nome == "Ifood"));
            Assert.Equal(0, await db.Lancamentos.CountAsync());
        }

        Assert.True((await _cadastros.SalvarCategoriaAsync(null, "Ifood", TipoLancamento.Despesa, null)).Sucesso);
        var ok = await Lancar(TipoCaptura.Despesa, 4500, "ifood", Hoje, descricao: "texto que não fica");
        Assert.True(ok.Sucesso, ok.Mensagem);
        await using (var db = Db())
        {
            var l = await db.Lancamentos.Include(x => x.Categoria).SingleAsync();
            Assert.Equal("Ifood", l.Categoria!.Nome);
            Assert.Equal("", l.Descricao);
        }

        // Receita não entra em categoria de despesa
        Assert.False((await Lancar(TipoCaptura.Receita, 1000, "Ifood", Hoje)).Sucesso);

        // Contexto traz as categorias por tipo, as mais usadas primeiro
        var ctx = await _favoa.ObterContextoAsync(Hoje);
        Assert.Equal("Ifood", ctx.CategoriasDespesa[0]);
        Assert.DoesNotContain("Ifood", ctx.CategoriasReceita);
        Assert.Equal("Ifood", ctx.CategoriaCadastrada(TipoCaptura.Despesa, "IFOOD"));
    }

    [Fact]
    public async Task Recorrencia_e_edicao_tambem_so_aceitam_categoria_cadastrada()
    {
        var rec = await _cadastros.SalvarRecorrenciaAsync(new DadosRecorrencia(null, TipoLancamento.Despesa, "", 12000, "Pets", null, 5, Hoje), Hoje);
        Assert.False(rec.Sucesso);
        rec = await _cadastros.SalvarRecorrenciaAsync(new DadosRecorrencia(null, TipoLancamento.Despesa, "", 12000, "Moradia", null, 5, Hoje), Hoje);
        Assert.True(rec.Sucesso, rec.Mensagem);
        await using (var db = Db())
            Assert.Equal("Moradia", (await db.Recorrencias.SingleAsync()).Descricao);

        var l = await Lancar(TipoCaptura.Despesa, 1000, "Mercado", Hoje);
        var edit = await _favoa.ObterParaEdicaoAsync(l.Criado!);
        Assert.Contains("Mercado", edit!.CategoriasDespesa);
        Assert.False((await _favoa.SalvarEdicaoAsync(edit with { Categoria = "Inventada" })).Sucesso);
        Assert.True((await _favoa.SalvarEdicaoAsync(edit with { Categoria = "Moradia" })).Sucesso);
        await using (var db = Db())
            Assert.False(await db.Categorias.AnyAsync(c => c.Nome == "Inventada"));
    }

    [Fact]
    public async Task Orcamento_por_categoria_avisa_ao_lancar_e_aparece_no_extrato()
    {
        var cat = (await _cadastros.ObterAsync(Hoje)).Categorias.Single(c => c.Nome == "Alimentação fora");
        Assert.True((await _cadastros.SalvarCategoriaAsync(cat.Id, cat.Nome, cat.Tipo, 50000, default)).Sucesso);

        var r1 = await Lancar(TipoCaptura.Despesa, 40000, "Alimentação fora", Hoje);
        Assert.Contains("80% do limite", r1.Mensagem);
        var r2 = await Lancar(TipoCaptura.Despesa, 14000, "Alimentação fora", Hoje);
        Assert.Contains("passou R$ 40,00 do limite", r2.Mensagem);

        var extrato = await _favoa.ObterExtratoAsync(FiltroExtrato.DoMes(Hoje), Hoje);
        var fatia = extrato.DespesasPorCategoria.Single(f => f.Nome == "Alimentação fora");
        Assert.Equal(50000, fatia.OrcamentoCentavos);
        Assert.Equal(1.08, fatia.FracaoOrcamento!.Value, 2);

        var quadro = await _cadastros.ObterAsync(Hoje);
        Assert.Equal(54000, quadro.Categorias.Single(c => c.Id == cat.Id).GastoMesCentavos);
    }

    [Fact]
    public async Task Excluir_categoria_usada_exige_destino_e_junta()
    {
        Assert.True((await _cadastros.SalvarCategoriaAsync(null, "Padaria", TipoLancamento.Despesa, null)).Sucesso);
        Assert.True((await Lancar(TipoCaptura.Despesa, 1000, "Padaria", Hoje)).Sucesso);
        var q = await _cadastros.ObterAsync(Hoje);
        var padaria = q.Categorias.Single(c => c.Nome == "Padaria");
        var fora = q.Categorias.Single(c => c.Nome == "Alimentação fora");
        var receita = q.Categorias.First(c => c.Tipo == TipoLancamento.Receita);

        Assert.False((await _cadastros.ExcluirCategoriaAsync(padaria.Id, null)).Sucesso);
        Assert.False((await _cadastros.ExcluirCategoriaAsync(padaria.Id, receita.Id)).Sucesso); // tipo diferente
        var r = await _cadastros.ExcluirCategoriaAsync(padaria.Id, fora.Id);
        Assert.True(r.Sucesso, r.Mensagem);

        await using var db = Db();
        Assert.Equal(fora.Id, (await db.Lancamentos.SingleAsync()).CategoriaId);
    }

    [Fact]
    public async Task Exportar_e_importar_mantem_recorrencias_lembretes_e_orcamento()
    {
        var fabrica = _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        await Lancar(TipoCaptura.Despesa, 5590, "Assinaturas", Hoje, repetir: true, descricao: "Netflix");
        var cat = (await _cadastros.ObterAsync(Hoje)).Categorias.Single(c => c.Nome == "Mercado");
        await _cadastros.SalvarCategoriaAsync(cat.Id, cat.Nome, cat.Tipo, 120000);
        await new LembretesServico(fabrica, new Favoa.Core.Pessoas.PessoaFixa(null), new OpcoesFavoa(), NullLogger<LembretesServico>.Instance)
            .SalvarAsync(new DadosLembrete(null, "Beber água", TipoLembrete.Livre, new TimeOnly(10, 0), DiasSemana.Uteis));

        var pasta = Directory.CreateTempSubdirectory("favoa-teste-").FullName;
        var dados = new DadosFavoa(fabrica, new OpcoesFavoa { PastaDados = pasta });
        var arquivo = (await dados.ExportarJsonAsync()).Conteudo;
        var resultado = await dados.ImportarJsonAsync(arquivo);
        Assert.True(resultado.Sucesso, resultado.Mensagem);

        await using var db = Db();
        var r = await db.Recorrencias.SingleAsync();
        Assert.Equal("Assinaturas", r.Descricao);
        Assert.Equal(r.Id, (await db.Lancamentos.SingleAsync()).RecorrenciaId);
        Assert.Equal(120000, (await db.Categorias.SingleAsync(c => c.Id == cat.Id)).OrcamentoCentavos);
        Assert.Equal(DiasSemana.Uteis, (await db.Lembretes.SingleAsync()).Dias);
    }

    [Fact]
    public async Task Contas_renomear_arquivar_e_juntar()
    {
        Assert.True((await _cadastros.SalvarContaAsync(null, "nubank")).Sucesso);
        Assert.False((await _cadastros.SalvarContaAsync(null, "Nubank")).Sucesso); // repetida
        var q = await _cadastros.ObterAsync(Hoje);
        var carteira = q.Contas.Single(c => c.Nome == "Carteira");
        var nubank = q.Contas.Single(c => c.Nome == "Nubank");
        foreach (var extra in q.Contas.Where(c => c.Nome is "Conta corrente" or "Cartão de crédito")) // contas iniciais da casa nova
            Assert.True((await _cadastros.ExcluirContaAsync(extra.Id, carteira.Id)).Sucesso);

        await _favoa.RegistrarAsync(new Interpretacao(TipoCaptura.Receita, "") { ValorCentavos = 10000, Categoria = "Salário", Conta = "Nubank", Data = Hoje });
        Assert.Equal(10000, (await _cadastros.ObterAsync(Hoje)).Contas.Single(c => c.Id == nubank.Id).SaldoCentavos);

        Assert.True((await _cadastros.DefinirContaAtivaAsync(carteira.Id, false)).Sucesso);
        Assert.False((await _cadastros.DefinirContaAtivaAsync(nubank.Id, false)).Sucesso); // a última ativa
        Assert.False((await _cadastros.ExcluirContaAsync(nubank.Id, null)).Sucesso);
        Assert.True((await _cadastros.ExcluirContaAsync(nubank.Id, carteira.Id)).Sucesso);

        var depois = await _cadastros.ObterAsync(Hoje);
        var unica = Assert.Single(depois.Contas);
        Assert.True(unica.Ativa); // a que sobrou volta a ficar ativa
        Assert.Equal(10000, unica.SaldoCentavos);
    }
}

public sealed class LembretesServicoTests : IAsyncLifetime
{
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private LembretesServico _servico = null!;

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao)).BuildServiceProvider();
        await _sp.PrepararBancoComCasaAsync();
        _servico = new LembretesServico(_sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>(), new Favoa.Core.Pessoas.PessoaFixa(null), new OpcoesFavoa(), NullLogger<LembretesServico>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    [Fact]
    public async Task Cria_chaves_uma_vez_e_valida_lembrete_e_inscricao()
    {
        var q1 = await _servico.ObterAsync();
        var q2 = await _servico.ObterAsync();
        Assert.Equal(65, WebPush.DeBase64Url(q1.ChavePublica).Length);
        Assert.Equal(q1.ChavePublica, q2.ChavePublica);

        Assert.False((await _servico.SalvarAsync(new DadosLembrete(null, "", TipoLembrete.Livre, new TimeOnly(8, 0), DiasSemana.Todos))).Sucesso);
        Assert.False((await _servico.SalvarAsync(new DadosLembrete(null, "Água", TipoLembrete.Livre, new TimeOnly(8, 0), DiasSemana.Nenhum))).Sucesso);
        Assert.True((await _servico.SalvarAsync(new DadosLembrete(null, "", TipoLembrete.Tarefas, new TimeOnly(8, 0), DiasSemana.Uteis))).Sucesso);
        Assert.Equal("Tarefas de hoje", (await _servico.ObterAsync()).Lembretes.Single().Titulo);

        Assert.False((await _servico.InscreverAparelhoAsync(new InscricaoPush("http://inseguro", "x", "y", null))).Sucesso);
        using var ec = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(false).Q;
        var chave = WebPush.Base64Url([4, .. p.X!, .. p.Y!]);
        var auth = WebPush.Base64Url(RandomNumberGenerator.GetBytes(16));
        Assert.True((await _servico.InscreverAparelhoAsync(new InscricaoPush("https://fcm.googleapis.com/x", chave, auth, "Android Chrome"))).Sucesso);
        Assert.True((await _servico.InscreverAparelhoAsync(new InscricaoPush("https://fcm.googleapis.com/x", chave, auth, "Android Chrome"))).Sucesso);
        Assert.Single((await _servico.ObterAsync()).Aparelhos);
    }

    [Fact]
    public async Task Editar_para_um_horario_que_ainda_vai_chegar_volta_a_disparar_hoje()
    {
        // Criado com um horário que já passou: fica marcado como "já foi hoje".
        var passado = TimeOnly.FromDateTime(DateTime.Now.AddMinutes(-5));
        await _servico.SalvarAsync(new DadosLembrete(null, "Amar", TipoLembrete.Livre, passado, DiasSemana.Todos));
        var l = (await _servico.ObterAsync()).Lembretes.Single();
        Assert.NotEqual(DateOnly.FromDateTime(DateTime.Now), DateOnly.FromDateTime(l.Proximo!.Value));

        // Editado para daqui a pouco: o próximo aviso é hoje (antes ficava preso no "já foi hoje").
        var futuro = TimeOnly.FromDateTime(DateTime.Now.AddMinutes(10));
        if (futuro < passado) return; // perto da meia-noite o teste não faz sentido
        await _servico.SalvarAsync(new DadosLembrete(l.Id, "Amar", TipoLembrete.Livre, futuro, DiasSemana.Todos));
        var depois = (await _servico.ObterAsync()).Lembretes.Single();
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), DateOnly.FromDateTime(depois.Proximo!.Value));
    }

    [Fact]
    public async Task Disparo_registra_o_resultado_mesmo_sem_tarefas_e_sem_aparelhos()
    {
        var hora = new TimeOnly(10, 0);
        await _servico.SalvarAsync(new DadosLembrete(null, "", TipoLembrete.Tarefas, hora, DiasSemana.Todos));
        await using (var db = await _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>().CreateDbContextAsync())
        {
            var l = await db.Lembretes.SingleAsync();
            l.UltimoDisparo = null;
            await db.SaveChangesAsync();
        }

        await _servico.DispararDevidosAsync(new DateTime(2026, 10, 10, 10, 0, 30));
        var r = (await _servico.ObterAsync()).Lembretes.Single();
        Assert.Equal("Não avisou: nenhum aparelho com notificações ativadas.", r.UltimoResultado);
        Assert.NotNull(r.UltimoEnvio);
    }

    [Fact]
    public async Task Chaves_de_notificacao_nao_saem_na_exportacao()
    {
        await _servico.ObterAsync(); // cria as chaves
        var dados = new DadosFavoa(_sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>(), new OpcoesFavoa { PastaDados = Path.GetTempPath() });
        var json = Encoding.UTF8.GetString((await dados.ExportarJsonAsync()).Conteudo);
        Assert.DoesNotContain("push_vapid", json);
    }
}
