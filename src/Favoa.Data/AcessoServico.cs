using Favoa.Core;
using Favoa.Core.Acesso;
using Favoa.Core.Financas;
using Favoa.Core.Pessoas;
using Favoa.Core.Rotina;
using Microsoft.EntityFrameworkCore;

namespace Favoa.Data;

/// <summary>
/// Famílias, logins (e-mail e senha) e convites. Abre o banco sem filtro de família de propósito:
/// é aqui que se decide quem entra em qual casa. Senha só existe como hash; o convite, só como hash do código.
/// </summary>
public sealed class AcessoServico(IDbContextFactory<FavoaDbContext> fabrica, TimeProvider? relogio = null) : IAcessoServico
{
    /// <summary>Marca de que as pessoas desta base já foram migradas (ver PrepararBancoAsync). Fica fora da exportação.</summary>
    public const string ChavePessoasMigradas = "acesso_pessoas_migrado";

    /// <summary>Chaves que ficam fora de exportar/importar: segredo ou controle deste computador.</summary>
    public static bool EhChaveDeAcesso(string chave) => chave.StartsWith("acesso_", StringComparison.Ordinal);

    /// <summary>Configurações que pertencem a este computador (chaves de notificação): nunca exportadas nem importadas.</summary>
    public static bool EhChaveLocal(string chave) => EhChaveDeAcesso(chave) || chave.StartsWith("push_", StringComparison.Ordinal);

    public const int ErrosAteBloquear = 5;
    private static readonly TimeSpan BloqueioInicial = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan BloqueioMaximo = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ValidadeConvite = TimeSpan.FromDays(7);

    private const string ErroLogin = "E-mail ou senha incorretos.";
    private static readonly Lazy<string> HashFalso = new(() => SenhaSegura.Gerar("senha-que-nao-existe"));

    private DateTime Agora => (relogio ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task<FaseAcesso> ObterFaseAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        return await FaseAsync(db, ct);
    }

    private static async Task<FaseAcesso> FaseAsync(FavoaDbContext db, CancellationToken ct) =>
        await db.Usuarios.AnyAsync(ct) ? FaseAcesso.Pronto
        : await db.Familias.AnyAsync(ct) ? FaseAcesso.SemUsuarios
        : FaseAcesso.SemFamilia;

    public async Task<IReadOnlyList<PessoaInfo>> ListarPessoasSemLoginAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        // Só no primeiro acesso: depois disso, nomes de pessoas não aparecem para quem ainda não entrou.
        if (await FaseAsync(db, ct) != FaseAcesso.SemUsuarios) return [];
        var pessoas = await db.Pessoas.AsNoTracking().OrderBy(p => p.Ordem).ThenBy(p => p.CriadoEm).ToListAsync(ct);
        return pessoas.Select(p => new PessoaInfo(p.Id, p.Nome, p.Cor, p.Ordem, false)).ToList();
    }

    public async Task<ResultadoEntrada> ConfigurarPrimeiroAcessoAsync(string? nomeFamilia, Guid? pessoaId, string? nomePessoa, string email, string senha, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        if (await FaseAsync(db, ct) == FaseAcesso.Pronto) return new(false, "O primeiro acesso já foi feito. Entre com o seu e-mail e senha.");
        if (ValidarCredenciais(email, senha) is { } erro) return new(false, erro.Mensagem);
        var emailNormal = SenhaSegura.NormalizarEmail(email)!;

        Familia? familia;
        Pessoa? pessoa;
        if (pessoaId is { } id)
        {
            pessoa = await db.Pessoas.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (pessoa is null) return new(false, "Essa pessoa não existe mais. Recarregue a página.");
            familia = pessoa.FamiliaId is { } f ? await db.Familias.FirstOrDefaultAsync(x => x.Id == f, ct) : null;
            if (familia is null)
            {
                familia = new Familia { Nome = NomeDaFamilia(nomeFamilia, pessoa.Nome) };
                db.Familias.Add(familia);
                pessoa.FamiliaId = familia.Id;
            }
        }
        else
        {
            var nome = nomePessoa?.Trim() ?? "";
            if (nome.Length == 0) return new(false, "Escreva o seu nome.");
            if (nome.Length > 40) return new(false, "Nome muito longo (até 40 letras).");
            familia = await db.Familias.OrderBy(x => x.CriadoEm).FirstOrDefaultAsync(ct);
            var novaFamilia = familia is null;
            familia ??= db.Familias.Add(new Familia { Nome = NomeDaFamilia(nomeFamilia, nome) }).Entity;
            pessoa = db.Pessoas.Add(new Pessoa { Nome = nome, Cor = CoresPessoa.Padrao[0], FamiliaId = familia.Id }).Entity;
            if (novaFamilia) SemearFamilia(db, familia.Id);
        }

        var usuario = NovoUsuario(familia.Id, pessoa.Id, email, emailNormal, senha, dono: true);
        db.Usuarios.Add(usuario);
        // Base nova: não há o que migrar das pessoas (ver PrepararPessoasAsync).
        if (!await db.Configuracoes.AnyAsync(c => c.Chave == ChavePessoasMigradas, ct))
            db.Configuracoes.Add(new Configuracao { Chave = ChavePessoasMigradas, Valor = Agora.ToString("O") });
        await db.SaveChangesAsync(ct);
        await InfraestruturaExtensions.MoverOrcamentoParaFamiliaAsync(db); // base antiga: o orçamento de antes vira o desta primeira casa
        return new(true, null, Sessao(usuario, pessoa.Nome));
    }

    public async Task<ResultadoEntrada> CriarFamiliaAsync(string nomeFamilia, string nomePessoa, string email, string senha, CancellationToken ct = default)
    {
        if (ValidarCredenciais(email, senha) is { } erro) return new(false, erro.Mensagem);
        var nome = nomePessoa?.Trim() ?? "";
        if (nome.Length == 0) return new(false, "Escreva o seu nome.");
        if (nome.Length > 40) return new(false, "Nome muito longo (até 40 letras).");
        var emailNormal = SenhaSegura.NormalizarEmail(email)!;

        await using var db = await fabrica.CreateDbContextAsync(ct);
        if (await db.Usuarios.AnyAsync(u => u.EmailNormalizado == emailNormal, ct)) return new(false, "Já existe uma conta com esse e-mail.");

        var familia = db.Familias.Add(new Familia { Nome = NomeDaFamilia(nomeFamilia, nome) }).Entity;
        var pessoa = db.Pessoas.Add(new Pessoa { Nome = nome, Cor = CoresPessoa.Padrao[0], FamiliaId = familia.Id }).Entity;
        SemearFamilia(db, familia.Id);
        var usuario = NovoUsuario(familia.Id, pessoa.Id, email, emailNormal, senha, dono: true);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);
        return new(true, null, Sessao(usuario, pessoa.Nome));
    }

    public async Task<ResultadoEntrada> EntrarAsync(string email, string senha, CancellationToken ct = default)
    {
        var emailNormal = SenhaSegura.NormalizarEmail(email);
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var usuario = emailNormal is null ? null : await db.Usuarios.FirstOrDefaultAsync(u => u.EmailNormalizado == emailNormal, ct);
        if (usuario is null)
        {
            SenhaSegura.Verificar(senha, HashFalso.Value); // gasta o mesmo tempo: não revela quais e-mails existem
            return new(false, ErroLogin);
        }

        if (usuario.BloqueadoAte is { } ate && ate > Agora) return new(false, MensagemDeEspera(ate - Agora));

        if (!SenhaSegura.Verificar(senha, usuario.SenhaHash))
        {
            usuario.FalhasSeguidas++;
            if (usuario.FalhasSeguidas >= ErrosAteBloquear)
            {
                var duracao = TimeSpan.FromTicks(Math.Min(BloqueioMaximo.Ticks, BloqueioInicial.Ticks << Math.Min(usuario.Bloqueios, 10)));
                usuario.BloqueadoAte = Agora + duracao;
                usuario.Bloqueios++;
                usuario.FalhasSeguidas = 0;
            }
            await db.SaveChangesAsync(ct);
            return new(false, usuario.BloqueadoAte is { } b && b > Agora ? MensagemDeEspera(b - Agora) : ErroLogin);
        }

        usuario.FalhasSeguidas = 0;
        usuario.Bloqueios = 0;
        usuario.BloqueadoAte = null;
        usuario.UltimoAcessoEm = Agora;
        await db.SaveChangesAsync(ct);
        var nome = await db.Pessoas.Where(p => p.Id == usuario.PessoaId).Select(p => p.Nome).FirstOrDefaultAsync(ct) ?? "";
        return new(true, null, Sessao(usuario, nome));
    }

    public async Task<SessaoUsuario?> ObterSessaoAsync(Guid usuarioId, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var usuario = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null) return null;
        var nome = await db.Pessoas.Where(p => p.Id == usuario.PessoaId).Select(p => p.Nome).FirstOrDefaultAsync(ct);
        return nome is null ? null : Sessao(usuario, nome);
    }

    public async Task<DadosUsuario?> ObterUsuarioAsync(Guid usuarioId, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var usuario = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null) return null;
        var familia = await db.Familias.Where(f => f.Id == usuario.FamiliaId).Select(f => f.Nome).FirstOrDefaultAsync(ct) ?? "";
        return new DadosUsuario(usuario.Id, usuario.Email, usuario.Dono, familia);
    }

    public async Task<ResultadoEntrada> TrocarSenhaAsync(Guid usuarioId, string senhaAtual, string senhaNova, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null) return new(false, "Esse acesso não existe mais.");
        if (usuario.BloqueadoAte is { } ate && ate > Agora) return new(false, MensagemDeEspera(ate - Agora));
        if (!SenhaSegura.Verificar(senhaAtual, usuario.SenhaHash)) return new(false, "A senha atual está incorreta.");
        if (SenhaSegura.Validar(senhaNova, usuario.Email) is { } problema) return new(false, problema);

        usuario.SenhaHash = SenhaSegura.Gerar(senhaNova);
        usuario.VersaoAcesso++;
        await db.SaveChangesAsync(ct);
        var nome = await db.Pessoas.Where(p => p.Id == usuario.PessoaId).Select(p => p.Nome).FirstOrDefaultAsync(ct) ?? "";
        return new(true, "Senha trocada. Os outros aparelhos vão pedir a senha nova.", Sessao(usuario, nome));
    }

    public async Task<ResultadoRegistro> DesconectarAparelhosAsync(Guid usuarioId, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null) return new(false, "Esse acesso não existe mais.");
        usuario.VersaoAcesso++;
        await db.SaveChangesAsync(ct);
        return new(true, "Todos os aparelhos foram desconectados e vão pedir a senha de novo.");
    }

    public async Task<ResultadoConvite> CriarConviteAsync(Guid usuarioDonoId, Guid pessoaId, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var dono = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioDonoId, ct);
        if (dono is not { Dono: true }) return new(false, "Só quem criou a conta da família pode convidar.");

        var pessoa = await db.Pessoas.FirstOrDefaultAsync(p => p.Id == pessoaId && p.FamiliaId == dono.FamiliaId, ct);
        if (pessoa is null) return new(false, "Essa pessoa não existe mais.");
        if (await db.Usuarios.AnyAsync(u => u.PessoaId == pessoaId, ct)) return new(false, $"{pessoa.Nome} já tem acesso.");

        // Um convite vivo por pessoa: o novo cancela o anterior.
        db.Convites.RemoveRange(db.Convites.Where(c => c.PessoaId == pessoaId && c.UsadoEm == null));
        var (codigo, hash) = SenhaSegura.NovoCodigo();
        db.Convites.Add(new Convite { FamiliaId = dono.FamiliaId, PessoaId = pessoaId, CodigoHash = hash, ExpiraEm = Agora + ValidadeConvite });
        await db.SaveChangesAsync(ct);
        return new(true, $"Convite de {pessoa.Nome} criado. Vale por {ValidadeConvite.TotalDays:0} dias e só pode ser usado uma vez.", codigo);
    }

    public async Task<ConviteInfo?> ObterConviteAsync(string codigo, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var convite = await ConviteValidoAsync(db, codigo, ct);
        if (convite is null) return null;
        var pessoa = await db.Pessoas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == convite.PessoaId, ct);
        var familia = await db.Familias.AsNoTracking().FirstOrDefaultAsync(f => f.Id == convite.FamiliaId, ct);
        return pessoa is null || familia is null ? null : new ConviteInfo(pessoa.Nome, familia.Nome);
    }

    public async Task<ResultadoEntrada> AceitarConviteAsync(string codigo, string email, string senha, string? apelido = null, CancellationToken ct = default)
    {
        if (ValidarCredenciais(email, senha) is { } erro) return new(false, erro.Mensagem);
        var emailNormal = SenhaSegura.NormalizarEmail(email)!;

        await using var db = await fabrica.CreateDbContextAsync(ct);
        var convite = await ConviteValidoAsync(db, codigo, ct);
        if (convite is null) return new(false, "Esse convite não vale mais. Peça um novo a quem criou a conta.");
        var pessoa = await db.Pessoas.FirstOrDefaultAsync(p => p.Id == convite.PessoaId, ct);
        if (pessoa is null) return new(false, "Esse convite não vale mais. Peça um novo a quem criou a conta.");
        if (await db.Usuarios.AnyAsync(u => u.EmailNormalizado == emailNormal, ct)) return new(false, "Já existe uma conta com esse e-mail.");

        if (apelido?.Trim() is { Length: > 0 } novo && novo != pessoa.Nome)
        {
            if (novo.Length > 40) return new(false, "Apelido muito longo (até 40 letras).");
            var alvo = Favoa.Core.Captura.Texto.Normalizar(novo);
            var outros = await db.Pessoas.AsNoTracking().Where(p => p.FamiliaId == convite.FamiliaId && p.Id != pessoa.Id).Select(p => p.Nome).ToListAsync(ct);
            if (outros.Any(n => Favoa.Core.Captura.Texto.Normalizar(n) == alvo)) return new(false, $"Já existe {novo} na casa. Escolha outro apelido.");
            pessoa.Nome = novo;
        }

        var usuario = NovoUsuario(convite.FamiliaId, pessoa.Id, email, emailNormal, senha, dono: false);
        db.Usuarios.Add(usuario);
        convite.UsadoEm = Agora;
        await db.SaveChangesAsync(ct);
        return new(true, null, Sessao(usuario, pessoa.Nome));
    }

    public async Task<ResultadoRegistro> RedefinirSenhaAsync(string email, string senhaNova, CancellationToken ct = default)
    {
        var emailNormal = SenhaSegura.NormalizarEmail(email);
        if (emailNormal is null) return new(false, "E-mail inválido.");
        if (SenhaSegura.Validar(senhaNova, emailNormal) is { } problema) return new(false, problema);

        await using var db = await fabrica.CreateDbContextAsync(ct);
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.EmailNormalizado == emailNormal, ct);
        if (usuario is null) return new(false, "Não há conta com esse e-mail.");
        usuario.SenhaHash = SenhaSegura.Gerar(senhaNova);
        usuario.VersaoAcesso++;
        usuario.FalhasSeguidas = 0;
        usuario.Bloqueios = 0;
        usuario.BloqueadoAte = null;
        await db.SaveChangesAsync(ct);
        return new(true, "Senha redefinida. Todos os aparelhos vão pedir a senha nova.");
    }

    public async Task<IReadOnlyList<ContaDoComputador>> ListarContasAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var contas = await (from u in db.Usuarios.AsNoTracking()
                            join f in db.Familias.AsNoTracking() on u.FamiliaId equals f.Id
                            join p in db.Pessoas.AsNoTracking() on u.PessoaId equals p.Id
                            orderby f.CriadoEm, p.Ordem, u.CriadoEm
                            select new ContaDoComputador(u.Id, f.Nome, p.Nome, u.Email, u.Dono, u.UltimoAcessoEm)).ToListAsync(ct);
        return contas;
    }

    public async Task<ResultadoSenhaTemporaria> GerarSenhaTemporariaAsync(Guid usuarioId, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null) return new(false, "Essa conta não existe mais.");

        // 12 letras e números sem os que se confundem (0/o, 1/l/i), em grupos de 4: fácil de ditar por telefone.
        const string alfabeto = "abcdefghjkmnpqrstuvwxyz23456789";
        var letras = new char[12];
        for (var i = 0; i < letras.Length; i++) letras[i] = alfabeto[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alfabeto.Length)];
        var senha = $"{new string(letras, 0, 4)}-{new string(letras, 4, 4)}-{new string(letras, 8, 4)}";

        usuario.SenhaHash = SenhaSegura.Gerar(senha);
        usuario.VersaoAcesso++;
        usuario.FalhasSeguidas = 0;
        usuario.Bloqueios = 0;
        usuario.BloqueadoAte = null;
        await db.SaveChangesAsync(ct);
        return new(true, "Senha temporária gerada. Todos os aparelhos dessa conta vão pedir a senha nova.", senha);
    }

    /// <summary>Tira espaços, hífens e maiúsculas e devolve no formato guardado (xxxx-xxxx-xxxx-xxxx).</summary>
    internal static string NormalizarCodigo(string? codigo)
    {
        var s = new string((codigo ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return s.Length == 16 ? $"{s[..4]}-{s[4..8]}-{s[8..12]}-{s[12..]}" : s;
    }

    public async Task<ResultadoCodigoRecuperacao> GerarCodigoRecuperacaoAsync(Guid usuarioId, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null) return new(false, "Essa conta não existe mais.");

        // 16 letras e números sem os que se confundem: ~79 bits, inviável de adivinhar.
        const string alfabeto = "abcdefghjkmnpqrstuvwxyz23456789";
        var letras = new char[16];
        for (var i = 0; i < letras.Length; i++) letras[i] = alfabeto[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alfabeto.Length)];
        var codigo = NormalizarCodigo(new string(letras));

        usuario.CodigoRecuperacaoHash = SenhaSegura.Gerar(codigo);
        await db.SaveChangesAsync(ct);
        return new(true, "Código gerado. Guarde num lugar seguro: ele aparece só agora e vale uma vez.", codigo);
    }

    public async Task<ResultadoRegistro> RecuperarSenhaAsync(string email, string codigo, string senhaNova, CancellationToken ct = default)
    {
        const string falha = "E-mail ou código de recuperação inválido.";
        var emailNormal = SenhaSegura.NormalizarEmail(email);
        if (emailNormal is null) return new(false, falha);

        await using var db = await fabrica.CreateDbContextAsync(ct);
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.EmailNormalizado == emailNormal, ct);
        // Sempre confere um hash (o falso, se não houver conta ou código): o tempo da resposta não revela se o e-mail existe.
        var confere = SenhaSegura.Verificar(NormalizarCodigo(codigo), usuario?.CodigoRecuperacaoHash ?? HashFalso.Value);
        if (usuario is null || usuario.CodigoRecuperacaoHash is null || !confere) return new(false, falha);
        if (SenhaSegura.Validar(senhaNova, emailNormal) is { } problema) return new(false, problema);

        usuario.SenhaHash = SenhaSegura.Gerar(senhaNova);
        usuario.CodigoRecuperacaoHash = null; // uso único: gere outro em Seus dados
        usuario.VersaoAcesso++;
        usuario.FalhasSeguidas = 0;
        usuario.Bloqueios = 0;
        usuario.BloqueadoAte = null;
        await db.SaveChangesAsync(ct);
        return new(true, "Senha redefinida. Entre com a senha nova e gere outro código de recuperação em Seus dados.");
    }

    public async Task<EstadoAcesso> ObterEstadoAsync(Guid? familiaId, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var pessoas = await db.Pessoas.AsNoTracking().Where(p => familiaId == null || p.FamiliaId == familiaId)
            .OrderBy(p => p.Ordem).ThenBy(p => p.CriadoEm).ToListAsync(ct);
        var comLogin = (await db.Usuarios.AsNoTracking().Select(u => u.PessoaId).ToListAsync(ct)).ToHashSet();
        return new EstadoAcesso(pessoas.Select(p => new PessoaInfo(p.Id, p.Nome, p.Cor, p.Ordem, comLogin.Contains(p.Id))).ToList());
    }

    public async Task<ResultadoRegistro> SalvarPessoaAsync(Guid familiaId, Guid? id, string nome, string cor, CancellationToken ct = default)
    {
        nome = nome.Trim();
        if (nome.Length == 0) return new(false, "Escreva o nome.");
        if (nome.Length > 40) return new(false, "Nome muito longo (até 40 letras).");
        if (!CoresPessoa.Valida(cor)) cor = CoresPessoa.Padrao[0];

        await using var db = await fabrica.CreateDbContextAsync(ct);
        var todas = await db.Pessoas.Where(p => p.FamiliaId == familiaId).ToListAsync(ct);
        if (todas.Any(p => p.Id != id && string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase)))
            return new(false, $"Já existe {nome} na casa.");

        var pessoa = id is { } i ? todas.FirstOrDefault(p => p.Id == i) : null;
        if (id is not null && pessoa is null) return new(false, "Essa pessoa não existe mais.");
        if (pessoa is null)
        {
            pessoa = new Pessoa { FamiliaId = familiaId, Ordem = todas.Count == 0 ? 0 : todas.Max(p => p.Ordem) + 1 };
            db.Pessoas.Add(pessoa);
        }
        pessoa.Nome = nome;
        pessoa.Cor = cor;
        await db.SaveChangesAsync(ct);
        return new(true, id is null ? $"{nome} entrou na casa. Convide {nome} para criar o próprio acesso." : $"{nome} salvo.");
    }

    public async Task<ResultadoRegistro> ExcluirPessoaAsync(Guid familiaId, Guid id, CancellationToken ct = default)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var pessoa = await db.Pessoas.FirstOrDefaultAsync(p => p.Id == id && p.FamiliaId == familiaId, ct);
        if (pessoa is null) return new(false, "Essa pessoa não existe mais.");
        if (await db.Usuarios.AnyAsync(u => u.PessoaId == id, ct)) return new(false, $"{pessoa.Nome} tem acesso ao Fávoa e não pode ser excluída.");

        var temDados = await db.Habitos.AnyAsync(x => x.PessoaId == id, ct) || await db.RegistrosAgua.AnyAsync(x => x.PessoaId == id, ct)
            || await db.Lembretes.AnyAsync(x => x.PessoaId == id, ct) || await db.Fichas.AnyAsync(x => x.PessoaId == id, ct)
            || await db.Sessoes.AnyAsync(x => x.PessoaId == id, ct) || await db.ItensPlano.AnyAsync(x => x.PessoaId == id, ct)
            || await db.Consumos.AnyAsync(x => x.PessoaId == id, ct) || await db.Pesos.AnyAsync(x => x.PessoaId == id, ct);
        if (temDados) return new(false, $"{pessoa.Nome} já tem treinos, dieta, hábitos ou lembretes. Para não perder nada, exclua esses dados antes.");

        // Lançamentos da casa continuam; só deixam de dizer quem lançou.
        await db.Lancamentos.Where(l => l.PessoaId == id).ExecuteUpdateAsync(u => u.SetProperty(l => l.PessoaId, (Guid?)null), ct);
        await db.AssinaturasPush.Where(a => a.PessoaId == id).ExecuteDeleteAsync(ct);
        // Tarefas continuam; a pessoa só sai da lista de responsáveis.
        await db.ResponsaveisTarefa.Where(r => r.PessoaId == id).ExecuteDeleteAsync(ct);
        await db.Convites.Where(c => c.PessoaId == id).ExecuteDeleteAsync(ct);
        db.Pessoas.Remove(pessoa);
        await db.SaveChangesAsync(ct);
        return new(true, $"{pessoa.Nome} saiu da casa.");
    }

    // ---------- Apoio ----------

    /// <summary>Categorias e conta iniciais de uma família nova (a família antiga já tem as dela).</summary>
    internal static void SemearFamilia(FavoaDbContext db, Guid familiaId)
    {
        string[] despesas = ["Mercado", "Alimentação fora", "Moradia", "Transporte", "Saúde", "Lazer", "Educação", "Presentes", "Assinaturas", "Outros"];
        string[] receitas = ["Salário", "Extra", "Outros"];
        db.Categorias.AddRange(despesas.Select(n => new Categoria { Nome = n, Tipo = TipoLancamento.Despesa, FamiliaId = familiaId }));
        db.Categorias.AddRange(receitas.Select(n => new Categoria { Nome = n, Tipo = TipoLancamento.Receita, FamiliaId = familiaId }));
        // A primeira (mais antiga) é a conta padrão dos lançamentos sem conta: Carteira vem na frente de propósito.
        var agora = DateTime.UtcNow;
        string[] contas = ["Carteira", "Conta corrente", "Cartão de crédito"];
        db.Contas.AddRange(contas.Select((n, i) => new Conta { Nome = n, FamiliaId = familiaId, CriadoEm = agora.AddMilliseconds(i) }));
    }

    private static (string Mensagem, bool Erro)? ValidarCredenciais(string? email, string? senha)
    {
        var emailNormal = SenhaSegura.NormalizarEmail(email);
        if (emailNormal is null) return ("Digite um e-mail válido.", true);
        return SenhaSegura.Validar(senha, emailNormal) is { } problema ? (problema, true) : null;
    }

    private static string NomeDaFamilia(string? nomeFamilia, string nomePessoa) =>
        string.IsNullOrWhiteSpace(nomeFamilia) ? $"Casa de {nomePessoa.Trim()}" : nomeFamilia.Trim()[..Math.Min(60, nomeFamilia.Trim().Length)];

    private Usuario NovoUsuario(Guid familiaId, Guid pessoaId, string email, string emailNormal, string senha, bool dono) => new()
    {
        FamiliaId = familiaId,
        PessoaId = pessoaId,
        Email = email.Trim(),
        EmailNormalizado = emailNormal,
        SenhaHash = SenhaSegura.Gerar(senha),
        Dono = dono,
        CriadoEm = Agora,
        UltimoAcessoEm = Agora,
    };

    private static SessaoUsuario Sessao(Usuario u, string nome) => new(u.Id, u.FamiliaId, u.PessoaId, nome, u.Dono, u.VersaoAcesso);

    private async Task<Convite?> ConviteValidoAsync(FavoaDbContext db, string? codigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 100) return null;
        var hash = SenhaSegura.HashDoCodigo(codigo.Trim());
        var convite = await db.Convites.FirstOrDefaultAsync(c => c.CodigoHash == hash, ct);
        if (convite is null || convite.UsadoEm is not null || convite.ExpiraEm <= Agora) return null;
        return await db.Usuarios.AnyAsync(u => u.PessoaId == convite.PessoaId, ct) ? null : convite;
    }

    private static string MensagemDeEspera(TimeSpan espera) =>
        $"Muitas tentativas erradas. Tente de novo em {(espera.TotalSeconds < 60 ? $"{Math.Ceiling(espera.TotalSeconds)} segundos" : $"{Math.Ceiling(espera.TotalMinutes)} minuto(s)")}.";
}
