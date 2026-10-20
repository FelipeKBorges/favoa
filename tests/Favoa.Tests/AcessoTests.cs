using Favoa.Core.Acesso;
using Favoa.Core.Pessoas;
using Favoa.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

public sealed class SenhaSeguraTests
{
    [Theory]
    [InlineData("uma frase longa e boa", "a@b.com", null)]
    [InlineData("S3nha-Forte!", "a@b.com", null)]
    [InlineData("curta1", "a@b.com", "pelo menos 8")]
    [InlineData("aaaaaaaaaa", "a@b.com", "repetir")]
    [InlineData("12345678", "a@b.com", "muito comum")]
    [InlineData("davi.silva2026", "davi.silva@email.com", "não pode conter o seu e-mail")]
    public void Validacao(string senha, string email, string? trecho)
    {
        var problema = SenhaSegura.Validar(senha, email);
        if (trecho is null) Assert.Null(problema);
        else Assert.Contains(trecho, problema);
    }

    [Fact]
    public void Hash_tem_sal_e_confere_so_a_senha_certa()
    {
        var a = SenhaSegura.Gerar("uma frase longa e boa");
        var b = SenhaSegura.Gerar("uma frase longa e boa");

        Assert.NotEqual(a, b); // sal diferente
        Assert.DoesNotContain("frase", a);
        Assert.True(SenhaSegura.Verificar("uma frase longa e boa", a));
        Assert.False(SenhaSegura.Verificar("uma frase longa e boa!", a));
        Assert.False(SenhaSegura.Verificar("uma frase longa e boa", "lixo"));
        Assert.False(SenhaSegura.Verificar("", a));
    }

    [Theory]
    [InlineData("  Davi@Email.COM ", "davi@email.com")]
    [InlineData("a@b.co", "a@b.co")]
    [InlineData("sem-arroba", null)]
    [InlineData("a@b", null)]
    [InlineData("", null)]
    [InlineData("Nome <a@b.com>", null)]
    public void Email_normalizado(string entrada, string? esperado) =>
        Assert.Equal(esperado, SenhaSegura.NormalizarEmail(entrada));

    [Fact]
    public void Codigo_de_convite_so_guarda_o_hash()
    {
        var (codigo, hash) = SenhaSegura.NovoCodigo();
        Assert.DoesNotContain(codigo, hash);
        Assert.Equal(hash, SenhaSegura.HashDoCodigo(codigo));
        Assert.NotEqual(SenhaSegura.NovoCodigo().Codigo, codigo);
    }
}

public sealed class ControleTentativasTests
{
    private DateTimeOffset _agora = new(2026, 10, 10, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cinco_erros_bloqueiam_e_o_bloqueio_dobra()
    {
        var c = new ControleTentativas(() => _agora);
        for (var i = 0; i < 4; i++) c.RegistrarErro("ip");
        Assert.Equal(TimeSpan.Zero, c.Espera("ip"));

        c.RegistrarErro("ip");
        Assert.Equal(TimeSpan.FromMinutes(1), c.Espera("ip"));
        Assert.Equal(TimeSpan.Zero, c.Espera("outro-ip"));

        _agora = _agora.AddMinutes(1);
        Assert.Equal(TimeSpan.Zero, c.Espera("ip"));

        for (var i = 0; i < 5; i++) c.RegistrarErro("ip");
        Assert.Equal(TimeSpan.FromMinutes(2), c.Espera("ip"));
    }

    [Fact]
    public void Bloqueio_tem_teto_e_acerto_zera()
    {
        var c = new ControleTentativas(() => _agora);
        for (var rodada = 0; rodada < 10; rodada++)
        {
            for (var i = 0; i < 5; i++) c.RegistrarErro("ip");
            _agora = _agora.AddHours(1);
        }
        for (var i = 0; i < 5; i++) c.RegistrarErro("ip");
        Assert.Equal(TimeSpan.FromMinutes(15), c.Espera("ip"));

        c.RegistrarAcerto("ip");
        Assert.Equal(TimeSpan.Zero, c.Espera("ip"));
    }
}

public sealed class AcessoServicoTests : IAsyncLifetime
{
    private const string Senha = "uma frase longa e boa";
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private DateTime _agora = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private ServiceProvider _sp = null!;
    private AcessoServico _acesso = null!;
    private IDbContextFactory<FavoaDbContext> _fabrica = null!;

    private sealed class Relogio(Func<DateTime> agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(agora(), TimeSpan.Zero);
    }

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao)).BuildServiceProvider();
        await _sp.PrepararBancoAsync();
        _fabrica = _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        _acesso = new AcessoServico(_fabrica, new Relogio(() => _agora));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private async Task<SessaoUsuario> NovaFamilia(string nome = "Davi", string email = "davi@exemplo.com")
    {
        var r = await _acesso.CriarFamiliaAsync("", nome, email, Senha);
        Assert.True(r.Sucesso, r.Mensagem);
        return r.Sessao!;
    }

    [Fact]
    public async Task Base_nova_nao_tem_familia_e_o_primeiro_acesso_cria_dono_pessoa_e_cadastros_iniciais()
    {
        Assert.Equal(FaseAcesso.SemFamilia, await _acesso.ObterFaseAsync());

        var r = await _acesso.ConfigurarPrimeiroAcessoAsync("Casa dos dois", null, "Davi", "Davi@Exemplo.com", Senha);
        Assert.True(r.Sucesso, r.Mensagem);
        Assert.True(r.Sessao!.Dono);
        Assert.Equal(FaseAcesso.Pronto, await _acesso.ObterFaseAsync());

        await using var db = await _fabrica.CreateDbContextAsync();
        var familia = Assert.Single(db.Familias.ToList());
        Assert.Equal("Casa dos dois", familia.Nome);
        Assert.Equal(familia.Id, db.Pessoas.Single().FamiliaId);
        Assert.Equal(13, db.Categorias.Count(c => c.FamiliaId == familia.Id));
        var contas = db.Contas.Where(c => c.FamiliaId == familia.Id).OrderBy(c => c.CriadoEm).Select(c => c.Nome).ToList();
        Assert.Equal(["Carteira", "Conta corrente", "Cartão de crédito"], contas); // a primeira é a conta padrão

        // Só uma vez.
        Assert.False((await _acesso.ConfigurarPrimeiroAcessoAsync(null, null, "Outro", "outro@exemplo.com", Senha)).Sucesso);
    }

    [Fact]
    public async Task Dados_de_antes_dos_logins_viram_a_primeira_familia_e_o_dono_escolhe_quem_e()
    {
        // Simula a base de quem já usava: uma família e pessoas criadas pela migração, sem nenhum login.
        Guid eva;
        await using (var db = await _fabrica.CreateDbContextAsync())
        {
            var familia = db.Familias.Add(new Familia { Nome = "Casa de Davi" }).Entity;
            db.Pessoas.Add(new Pessoa { Nome = "Davi", FamiliaId = familia.Id });
            eva = db.Pessoas.Add(new Pessoa { Nome = "Eva", FamiliaId = familia.Id, Ordem = 1 }).Entity.Id;
            await db.SaveChangesAsync();
        }
        Assert.Equal(FaseAcesso.SemUsuarios, await _acesso.ObterFaseAsync());
        Assert.Equal(["Davi", "Eva"], (await _acesso.ListarPessoasSemLoginAsync()).Select(p => p.Nome).ToArray());

        var r = await _acesso.ConfigurarPrimeiroAcessoAsync(null, eva, null, "eva@exemplo.com", Senha);
        Assert.True(r.Sucesso, r.Mensagem);
        Assert.Equal("Eva", r.Sessao!.Nome);
        Assert.True(r.Sessao.Dono);
        // Depois do primeiro acesso, ninguém lista nomes sem estar logado.
        Assert.Empty(await _acesso.ListarPessoasSemLoginAsync());
    }

    [Fact]
    public async Task Entrar_confere_e_trata_email_sem_diferenciar_maiusculas()
    {
        var criada = await NovaFamilia();
        var ok = await _acesso.EntrarAsync("  DAVI@exemplo.com ", Senha);
        Assert.True(ok.Sucesso);
        Assert.Equal(criada.UsuarioId, ok.Sessao!.UsuarioId);

        var erro = await _acesso.EntrarAsync("davi@exemplo.com", "errada errada");
        Assert.False(erro.Sucesso);
        var semConta = await _acesso.EntrarAsync("ninguem@exemplo.com", Senha);
        Assert.False(semConta.Sucesso);
        Assert.Equal(erro.Mensagem, semConta.Mensagem); // não revela quais e-mails existem
    }

    [Fact]
    public async Task Cinco_erros_bloqueiam_a_conta_e_o_bloqueio_dobra()
    {
        await NovaFamilia();
        for (var i = 0; i < 5; i++) await _acesso.EntrarAsync("davi@exemplo.com", "errada errada");

        var bloqueada = await _acesso.EntrarAsync("davi@exemplo.com", Senha); // até a senha certa espera
        Assert.False(bloqueada.Sucesso);
        Assert.Contains("Muitas tentativas", bloqueada.Mensagem);

        _agora = _agora.AddMinutes(1).AddSeconds(1);
        Assert.True((await _acesso.EntrarAsync("davi@exemplo.com", Senha)).Sucesso);

        // Rodada nova de erros começa do zero depois de um acerto.
        for (var i = 0; i < 5; i++) await _acesso.EntrarAsync("davi@exemplo.com", "errada errada");
        _agora = _agora.AddSeconds(61);
        Assert.True((await _acesso.EntrarAsync("davi@exemplo.com", Senha)).Sucesso);
    }

    [Fact]
    public async Task Trocar_senha_exige_a_atual_e_derruba_os_outros_aparelhos()
    {
        var s = await NovaFamilia();
        Assert.False((await _acesso.TrocarSenhaAsync(s.UsuarioId, "errada errada", "outra frase bem longa")).Sucesso);
        Assert.False((await _acesso.TrocarSenhaAsync(s.UsuarioId, Senha, "curta")).Sucesso);

        var troca = await _acesso.TrocarSenhaAsync(s.UsuarioId, Senha, "outra frase bem longa");
        Assert.True(troca.Sucesso, troca.Mensagem);
        Assert.True(troca.Sessao!.Versao > s.Versao);
        Assert.True((await _acesso.ObterSessaoAsync(s.UsuarioId))!.Versao > s.Versao); // cookie antigo não confere mais
        Assert.False((await _acesso.EntrarAsync("davi@exemplo.com", Senha)).Sucesso);
        Assert.True((await _acesso.EntrarAsync("davi@exemplo.com", "outra frase bem longa")).Sucesso);
    }

    [Fact]
    public async Task Desconectar_aparelhos_sobe_a_versao()
    {
        var s = await NovaFamilia();
        Assert.True((await _acesso.DesconectarAparelhosAsync(s.UsuarioId)).Sucesso);
        Assert.True((await _acesso.ObterSessaoAsync(s.UsuarioId))!.Versao > s.Versao);
    }

    [Fact]
    public async Task Convite_cria_o_login_da_pessoa_uma_unica_vez()
    {
        var dono = await NovaFamilia();
        var eva = (await _acesso.SalvarPessoaAsync(dono.FamiliaId, null, "Eva", "#7FC8B4")).Sucesso;
        Assert.True(eva);
        var pessoaGab = (await _acesso.ObterEstadoAsync(dono.FamiliaId)).Pessoas.Single(p => p.Nome == "Eva");
        Assert.False(pessoaGab.TemAcesso);

        var convite = await _acesso.CriarConviteAsync(dono.UsuarioId, pessoaGab.Id);
        Assert.True(convite.Sucesso, convite.Mensagem);
        Assert.NotNull(await _acesso.ObterConviteAsync(convite.Codigo!));
        Assert.Null(await _acesso.ObterConviteAsync("codigo-invalido"));

        // O código não fica guardado, só o hash.
        await using (var db = await _fabrica.CreateDbContextAsync())
            Assert.DoesNotContain(db.Convites.Select(c => c.CodigoHash).ToList(), h => h == convite.Codigo);

        var aceito = await _acesso.AceitarConviteAsync(convite.Codigo!, "eva@exemplo.com", Senha);
        Assert.True(aceito.Sucesso, aceito.Mensagem);
        Assert.False(aceito.Sessao!.Dono);
        Assert.Equal(pessoaGab.Id, aceito.Sessao.PessoaId);
        Assert.Equal(dono.FamiliaId, aceito.Sessao.FamiliaId);

        // Uso único.
        Assert.Null(await _acesso.ObterConviteAsync(convite.Codigo!));
        Assert.False((await _acesso.AceitarConviteAsync(convite.Codigo!, "outra@exemplo.com", Senha)).Sucesso);
        Assert.True((await _acesso.ObterEstadoAsync(dono.FamiliaId)).Pessoas.Single(p => p.Nome == "Eva").TemAcesso);
    }

    [Fact]
    public async Task Convidado_escolhe_o_proprio_apelido_sem_repetir_o_de_outro_da_casa()
    {
        var dono = await NovaFamilia();
        await _acesso.SalvarPessoaAsync(dono.FamiliaId, null, "Eva", "#7FC8B4");
        await _acesso.SalvarPessoaAsync(dono.FamiliaId, null, "Teo", "#B9A3E3");
        var pessoas = (await _acesso.ObterEstadoAsync(dono.FamiliaId)).Pessoas;
        var eva = pessoas.Single(p => p.Nome == "Eva");
        var teo = pessoas.Single(p => p.Nome == "Teo");

        // Apelido de quem já está na casa (mesmo sem acento ou maiúscula): recusa e o convite continua valendo.
        var c1 = await _acesso.CriarConviteAsync(dono.UsuarioId, eva.Id);
        var recusa = await _acesso.AceitarConviteAsync(c1.Codigo!, "eva@exemplo.com", Senha, "teo");
        Assert.False(recusa.Sucesso);
        Assert.NotNull(await _acesso.ObterConviteAsync(c1.Codigo!));

        var ok = await _acesso.AceitarConviteAsync(c1.Codigo!, "eva@exemplo.com", Senha, "Evinha");
        Assert.True(ok.Sucesso, ok.Mensagem);
        Assert.Equal("Evinha", ok.Sessao!.Nome);
        Assert.Contains((await _acesso.ObterEstadoAsync(dono.FamiliaId)).Pessoas, p => p.Id == eva.Id && p.Nome == "Evinha");
        Assert.DoesNotContain(teo.Id, new Guid[] { ok.Sessao.PessoaId });
    }

    [Fact]
    public async Task Convite_expira_e_so_o_dono_convida()
    {
        var dono = await NovaFamilia();
        await _acesso.SalvarPessoaAsync(dono.FamiliaId, null, "Eva", "#7FC8B4");
        var eva = (await _acesso.ObterEstadoAsync(dono.FamiliaId)).Pessoas.Single(p => p.Nome == "Eva");

        var convite = await _acesso.CriarConviteAsync(dono.UsuarioId, eva.Id);
        _agora = _agora.AddDays(8);
        Assert.Null(await _acesso.ObterConviteAsync(convite.Codigo!));
        Assert.False((await _acesso.AceitarConviteAsync(convite.Codigo!, "eva@exemplo.com", Senha)).Sucesso);

        // Um membro não convida.
        var novo = await _acesso.CriarConviteAsync(dono.UsuarioId, eva.Id);
        var membro = (await _acesso.AceitarConviteAsync(novo.Codigo!, "eva@exemplo.com", Senha)).Sessao!;
        await _acesso.SalvarPessoaAsync(dono.FamiliaId, null, "Teo", "#B9A3E3");
        var teo = (await _acesso.ObterEstadoAsync(dono.FamiliaId)).Pessoas.Single(p => p.Nome == "Teo");
        Assert.False((await _acesso.CriarConviteAsync(membro.UsuarioId, teo.Id)).Sucesso);
    }

    [Fact]
    public async Task Convite_nao_vale_para_pessoa_de_outra_familia()
    {
        var a = await NovaFamilia("Ana", "ana@exemplo.com");
        var b = await NovaFamilia("Bia", "bia@exemplo.com");
        var pessoaDeB = b.PessoaId;
        // Ana tenta convidar a pessoa da família da Bia.
        var r = await _acesso.CriarConviteAsync(a.UsuarioId, pessoaDeB);
        Assert.False(r.Sucesso);
    }

    [Fact]
    public async Task Email_nao_se_repete_entre_logins()
    {
        await NovaFamilia();
        var repetido = await _acesso.CriarFamiliaAsync("", "Outro", "DAVI@exemplo.com", Senha);
        Assert.False(repetido.Sucesso);
        Assert.Contains("Já existe", repetido.Mensagem);
    }

    [Fact]
    public async Task Redefinir_senha_pela_linha_de_comando_derruba_aparelhos_e_zera_bloqueio()
    {
        var s = await NovaFamilia();
        for (var i = 0; i < 5; i++) await _acesso.EntrarAsync("davi@exemplo.com", "errada errada");

        Assert.False((await _acesso.RedefinirSenhaAsync("davi@exemplo.com", "curta")).Sucesso);
        Assert.False((await _acesso.RedefinirSenhaAsync("ninguem@exemplo.com", "uma senha bem longa")).Sucesso);
        Assert.True((await _acesso.RedefinirSenhaAsync("davi@exemplo.com", "uma senha bem longa")).Sucesso);

        Assert.True((await _acesso.ObterSessaoAsync(s.UsuarioId))!.Versao > s.Versao);
        Assert.True((await _acesso.EntrarAsync("davi@exemplo.com", "uma senha bem longa")).Sucesso); // sem esperar o bloqueio
    }

    [Fact]
    public async Task Pessoas_so_aparecem_na_propria_familia_e_quem_tem_login_nao_sai()
    {
        var a = await NovaFamilia("Ana", "ana@exemplo.com");
        var b = await NovaFamilia("Bia", "bia@exemplo.com");
        Assert.Equal(["Ana"], (await _acesso.ObterEstadoAsync(a.FamiliaId)).Pessoas.Select(p => p.Nome).ToArray());
        Assert.Equal(["Bia"], (await _acesso.ObterEstadoAsync(b.FamiliaId)).Pessoas.Select(p => p.Nome).ToArray());

        Assert.False((await _acesso.ExcluirPessoaAsync(a.FamiliaId, a.PessoaId)).Sucesso);   // tem login
        Assert.False((await _acesso.ExcluirPessoaAsync(a.FamiliaId, b.PessoaId)).Sucesso);   // é de outra família
        Assert.False((await _acesso.SalvarPessoaAsync(a.FamiliaId, b.PessoaId, "Invasora", "#F2A93B")).Sucesso);

        await _acesso.SalvarPessoaAsync(a.FamiliaId, null, "Visita", "#B9A3E3");
        var visita = (await _acesso.ObterEstadoAsync(a.FamiliaId)).Pessoas.Single(p => p.Nome == "Visita");
        Assert.True((await _acesso.ExcluirPessoaAsync(a.FamiliaId, visita.Id)).Sucesso);
        Assert.False((await _acesso.SalvarPessoaAsync(a.FamiliaId, null, "ana", "#F2A93B")).Sucesso); // nome repetido na casa
    }
}

public sealed class RegraLocalTests
{
    [Theory]
    [InlineData("127.0.0.1", "https", true)]
    [InlineData("::1", "HTTPS", true)]
    [InlineData("127.0.0.1", "http", false)]
    [InlineData("127.0.0.1", null, false)]
    [InlineData("100.64.0.5", "https", false)] // de fora do PC, o cabeçalho poderia ser forjado
    public void Https_encaminhado_so_vale_vindo_do_proprio_pc(string remoto, string? proto, bool esperado) =>
        Assert.Equal(esperado, RegraLocal.VeioPorHttps(System.Net.IPAddress.Parse(remoto), proto));

    private static readonly System.Net.IPAddress Loop = System.Net.IPAddress.Loopback;
    private static readonly System.Net.IPAddress Pc = System.Net.IPAddress.Parse("192.168.0.15");
    private static readonly System.Net.IPAddress Celular = System.Net.IPAddress.Parse("192.168.0.42");

    private static bool Local(System.Net.IPAddress remoto, string host, string[]? cabecalhos = null, bool pcPeloIp = true) =>
        RegraLocal.EhLocal(remoto, remoto.Equals(Loop) ? Loop : Pc, host, cabecalhos ?? ["Host", "Accept"], pcPeloIp);

    [Fact]
    public void Navegador_do_pc_em_localhost_entra_direto()
    {
        Assert.True(Local(Loop, "localhost:5080"));
        Assert.True(Local(Loop, "127.0.0.1:5080"));
        Assert.True(Local(System.Net.IPAddress.IPv6Loopback, "[::1]:5080"));
    }

    [Fact]
    public void Celular_na_wifi_pede_pin()
    {
        Assert.False(Local(Celular, "192.168.0.15:5080"));
    }

    [Fact]
    public void Pc_pelo_ip_da_rede_depende_da_opcao()
    {
        Assert.True(Local(Pc, "192.168.0.15:5080"));
        Assert.False(Local(Pc, "192.168.0.15:5080", pcPeloIp: false));
    }

    [Theory]
    [InlineData("meu-pc.tail1234.ts.net", new[] { "Host", "Tailscale-User-Login", "Tailscale-User-Name" })]
    [InlineData("meu-pc.tail1234.ts.net", new[] { "Host" })]          // dispositivo marcado: sem cabeçalhos de identidade
    [InlineData("localhost:5080", new[] { "Host", "X-Forwarded-For" })]
    [InlineData("localhost:5080", new[] { "Host", "Tailscale-User-Login" })]
    public void Pedido_repassado_por_proxy_local_pede_pin(string host, string[] cabecalhos)
    {
        Assert.False(Local(Loop, host, cabecalhos));
    }
}
