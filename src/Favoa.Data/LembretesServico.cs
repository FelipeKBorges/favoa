using System.Net;
using System.Text.Json;
using Favoa.Core;
using Favoa.Core.Rotina;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Favoa.Data;

public sealed class LembretesServico(IDbContextFactory<FavoaDbContext> fabrica, Favoa.Core.Pessoas.IPessoaAtual pessoa, OpcoesFavoa opcoes, ILogger<LembretesServico> log) : ILembretesServico
{
    private const string ChavePublica = "push_vapid_publica";
    private const string ChavePrivada = "push_vapid_privada";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim TravaChaves = new(1, 1);

    public async Task<QuadroLembretes> ObterAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var agora = DateTime.Now;
        var lembretes = (await db.Lembretes.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(l => l.Ativo).ThenBy(l => l.Hora)
            .Select(l => new LembreteCadastro(l.Id, l.Titulo, l.Tipo, l.Hora, l.Dias, l.Ativo)
            {
                Proximo = AgendaLembretes.Proximo(l, agora),
                UltimoEnvio = l.UltimoEnvioEm?.ToLocalTime(),
                UltimoResultado = l.UltimoResultado,
            }).ToList();
        var aparelhos = await db.AssinaturasPush.AsNoTracking().OrderBy(a => a.CriadoEm)
            .Select(a => new AparelhoPush(a.Id, a.Aparelho, a.CriadoEm)).ToListAsync(ct);
        var (publica, _) = await ObterChavesAsync(db, ct);
        return new QuadroLembretes(lembretes, aparelhos, publica) { AvisosTarefas = await AvisosPrevistosAsync(db, agora, ct) };
    }

    /// <summary>Tarefas com lembrete que ainda vão avisar, da pessoa atual ou da casa, em ordem de envio.</summary>
    private static async Task<List<AvisoTarefaPrevisto>> AvisosPrevistosAsync(FavoaDbContext db, DateTime agora, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(agora);
        var tarefas = await db.Tarefas.AsNoTracking().Include(t => t.Responsaveis)
            .Where(t => t.ConcluidaEm == null && t.AvisoMinutos != null && t.AvisoEnviadoEm == null && t.Hora != null
                && t.Prazo != null && t.Prazo >= hoje && t.Prazo <= hoje.AddDays(21))
            .ToListAsync(ct);
        var nomes = await db.Pessoas.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Nome, ct);
        return tarefas
            .Where(t => db.PessoaAtual is not { } eu || t.Responsaveis.Count == 0 || t.Responsaveis.Any(r => r.PessoaId == eu))
            .Select(t =>
            {
                var hora = AvisoTarefa.Momento(t.Prazo, t.Hora)!.Value;
                var para = t.Responsaveis.Count == 0 ? "todos da casa"
                    : string.Join(" e ", t.Responsaveis.Select(r => nomes.GetValueOrDefault(r.PessoaId)).OfType<string>());
                return new AvisoTarefaPrevisto(t.Id, t.Titulo, hora.AddMinutes(-t.AvisoMinutos!.Value), hora, t.AvisoMinutos.Value, para);
            })
            .Where(a => a.HoraTarefa + AvisoTarefa.Tolerancia >= agora && a.Quando <= agora.AddDays(14))
            .OrderBy(a => a.Quando).ToList();
    }

    public async Task<ResultadoRegistro> SalvarAsync(DadosLembrete d, CancellationToken ct = default)
    {
        var titulo = d.Titulo.Trim();
        if (titulo.Length == 0) titulo = d.Tipo == TipoLembrete.Tarefas ? "Tarefas de hoje" : "";
        if (titulo.Length == 0) return new(false, "Escreva o que lembrar (ex.: Beber água).");
        if (titulo.Length > 120) return new(false, "Texto muito longo (até 120 letras).");
        if (d.Dias == DiasSemana.Nenhum) return new(false, "Escolha pelo menos um dia da semana.");

        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var l = d.Id is { } id ? await db.Lembretes.FindAsync([id], ct) : null;
        if (d.Id is not null && l is null) return new(false, "Esse lembrete não existe mais.");
        if (l is null)
        {
            l = new Lembrete();
            db.Lembretes.Add(l);
        }

        var hora = new TimeOnly(d.Hora.Hour, d.Hora.Minute);
        var agora = DateTime.Now;
        // Horário que já passou hoje começa amanhã; horário por vir dispara hoje, mesmo depois de editar.
        if (d.Id is null || l.Hora != hora || l.Dias != d.Dias)
            l.UltimoDisparo = AgendaLembretes.MarcaAoSalvar(hora, agora);

        l.Titulo = titulo;
        l.Tipo = d.Tipo;
        l.Hora = hora;
        l.Dias = d.Dias;
        await db.SaveChangesAsync(ct);

        var aparelhos = await db.AssinaturasPush.CountAsync(ct);
        var proximo = AgendaLembretes.Proximo(l, agora);
        var quando = proximo is { } p ? $" Próximo aviso: {AgendaLembretes.Quando(p, agora)}." : "";
        var aviso = aparelhos == 0 ? " Ative as notificações em um aparelho para receber." : "";
        return new(true, $"Lembrete salvo: {l.Titulo}.{quando}{aviso}");
    }

    public async Task<ResultadoRegistro> DefinirAtivoAsync(Guid id, bool ativo, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var l = await db.Lembretes.FindAsync([id], ct);
        if (l is null) return new(false, "Esse lembrete não existe mais.");
        if (ativo && !l.Ativo) l.UltimoDisparo = AgendaLembretes.MarcaAoSalvar(l.Hora, DateTime.Now);
        l.Ativo = ativo;
        await db.SaveChangesAsync(ct);
        return new(true, ativo ? $"{l.Titulo}: ligado." : $"{l.Titulo}: desligado.");
    }

    public async Task<ResultadoRegistro> ExcluirAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var l = await db.Lembretes.FindAsync([id], ct);
        if (l is null) return new(false, "Esse lembrete não existe mais.");
        db.Lembretes.Remove(l);
        await db.SaveChangesAsync(ct);
        return new(true, $"Lembrete {l.Titulo} excluído.");
    }

    // ---------- Aparelhos ----------

    public async Task<ResultadoRegistro> InscreverAparelhoAsync(InscricaoPush i, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(i.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return new(false, "O navegador devolveu uma inscrição inválida.");
        try
        {
            if (WebPush.DeBase64Url(i.P256dh).Length != 65 || WebPush.DeBase64Url(i.Auth).Length < 16)
                return new(false, "O navegador devolveu chaves inválidas.");
        }
        catch (FormatException) { return new(false, "O navegador devolveu chaves inválidas."); }

        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        // Procura em todas as pessoas: o mesmo aparelho pode ter sido de outra pessoa antes (passa a ser de quem ativou agora).
        var existente = await db.AssinaturasPush.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Endpoint == i.Endpoint, ct);
        if (existente is null)
        {
            existente = new AssinaturaPush { Endpoint = i.Endpoint };
            db.AssinaturasPush.Add(existente);
        }
        existente.PessoaId = pessoa.Id ?? existente.PessoaId;
        existente.P256dh = i.P256dh;
        existente.Auth = i.Auth;
        existente.Aparelho = NomeAparelho(i.Aparelho);
        await db.SaveChangesAsync(ct);
        return new(true, $"Notificações ativadas neste aparelho ({existente.Aparelho}).");
    }

    public async Task<ResultadoRegistro> RemoverAparelhoAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var a = await db.AssinaturasPush.FindAsync([id], ct);
        if (a is null) return new(false, "Esse aparelho já foi removido.");
        db.AssinaturasPush.Remove(a);
        await db.SaveChangesAsync(ct);
        return new(true, $"{a.Aparelho} não recebe mais notificações.");
    }

    public async Task<ResultadoRegistro> RemoverAparelhoPorEndpointAsync(string endpoint, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var removidos = await db.AssinaturasPush.Where(a => a.Endpoint == endpoint).ExecuteDeleteAsync(ct);
        return new(removidos > 0, removidos > 0 ? "Notificações desativadas neste aparelho." : "Este aparelho não estava recebendo.");
    }

    public async Task<ResultadoEnvio> EnviarTesteAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var (ok, falhas) = await EnviarParaTodosAsync(db, "Fávoa", "Notificações funcionando. Seus lembretes vão chegar assim.", "/cadastros?aba=lembretes", ct);
        if (ok + falhas == 0) return new(0, 0, "Nenhum aparelho ativado ainda.");
        return new(ok, falhas, falhas == 0
            ? $"Enviado para {ok} aparelho(s)."
            : $"Enviado para {ok}; {falhas} falharam (veja o log em {Path.Combine(opcoes.PastaDados, "logs")}).");
    }

    public async Task<ResultadoEnvio> EnviarAgoraAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var l = await db.Lembretes.FindAsync([id], ct);
        if (l is null) return new(0, 0, "Esse lembrete não existe mais.");
        var (titulo, corpo, url) = await MontarAsync(db, l, DateOnly.FromDateTime(DateTime.Now), ct);
        var (ok, falhas) = await EnviarParaTodosAsync(db, titulo, corpo, url, ct);
        if (ok + falhas == 0) return new(0, 0, "Nenhum aparelho ativado ainda.");
        return new(ok, falhas, falhas == 0 ? $"{l.Titulo}: enviado para {ok} aparelho(s)." : $"{l.Titulo}: enviado para {ok}; {falhas} falharam.");
    }


    // ---------- Disparo ----------

    public async Task<int> DispararDevidosAsync(DateTime agoraLocal, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var devidos = AgendaLembretes.Devidos(await db.Lembretes.Where(l => l.Ativo).ToListAsync(ct), agoraLocal).ToList();
        var enviados = await AvisarTarefasAsync(db, agoraLocal, ct);
        if (devidos.Count == 0) return enviados;

        var hoje = DateOnly.FromDateTime(agoraLocal);
        // Marca antes de enviar: se o envio demorar, o próximo ciclo não repete.
        foreach (var (l, _) in devidos) l.UltimoDisparo = hoje;
        await db.SaveChangesAsync(ct);

        foreach (var (l, avisar) in devidos)
        {
            if (!avisar)
            {
                l.UltimoResultado = "Não avisou: o Fávoa estava desligado na hora (mais de 2 h de atraso).";
                l.UltimoEnvioEm = DateTime.UtcNow;
                log.LogInformation("Lembrete {Titulo} pulado hoje: atraso maior que a tolerância.", l.Titulo);
                continue;
            }
            var (titulo, corpo, url) = await MontarAsync(db, l, hoje, ct);
            // Só os aparelhos de quem é o lembrete.
            var (ok, falhas) = await EnviarParaTodosAsync(db, titulo, corpo, url, ct, l.PessoaId);
            l.UltimoEnvioEm = DateTime.UtcNow;
            l.UltimoResultado = ok + falhas == 0 ? "Não avisou: nenhum aparelho com notificações ativadas."
                : falhas == 0 ? $"Enviado para {ok} aparelho(s)."
                : $"Enviado para {ok}; {falhas} falharam (veja o log).";
            log.LogInformation("Lembrete {Titulo}: {Resultado}", l.Titulo, l.UltimoResultado);
            if (ok > 0) enviados++;
        }
        await db.SaveChangesAsync(ct);
        return enviados;
    }

    /// <summary>
    /// Tarefas com horário e aviso: manda para os aparelhos dos responsáveis (ou de todos, se a tarefa é da casa).
    /// Marca antes de enviar, para não repetir.
    /// </summary>
    private async Task<int> AvisarTarefasAsync(FavoaDbContext db, DateTime agoraLocal, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(agoraLocal);
        var candidatas = await db.Tarefas.Include(t => t.Responsaveis)
            .Where(t => t.ConcluidaEm == null && t.AvisoMinutos != null && t.AvisoEnviadoEm == null && t.Hora != null
                && t.Prazo != null && t.Prazo >= hoje.AddDays(-1) && t.Prazo <= hoje.AddDays(8))
            .ToListAsync(ct);
        var agora = candidatas.Select(t => (Tarefa: t, Situacao: AvisoTarefa.Verificar(t, agoraLocal)))
            .Where(x => x.Situacao != AvisoTarefa.Situacao.Nenhum).ToList();
        if (agora.Count == 0) return 0;

        foreach (var (t, _) in agora) t.AvisoEnviadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var enviados = 0;
        foreach (var (t, situacao) in agora)
        {
            if (situacao == AvisoTarefa.Situacao.Perdido)
            {
                log.LogInformation("Aviso da tarefa {Titulo} pulado: o horário já tinha passado.", t.Titulo);
                continue;
            }
            var donos = t.Responsaveis.Select(r => (Guid?)r.PessoaId).ToList();
            var (ok, falhas) = await EnviarParaAsync(db, "Tarefa " + AvisoTarefa.Quando(t, agoraLocal), t.Titulo, "/tarefas", donos.Count == 0 ? null : donos, ct);
            log.LogInformation("Aviso da tarefa {Titulo}: {Ok} enviado(s), {Falhas} falha(s).", t.Titulo, ok, falhas);
            if (ok > 0) enviados++;
        }
        return enviados;
    }

    private static async Task<(string Titulo, string Corpo, string Url)> MontarAsync(FavoaDbContext db, Lembrete l, DateOnly hoje, CancellationToken ct)
    {
        if (l.Tipo != TipoLembrete.Tarefas) return (l.Titulo, "Toque para abrir o Fávoa.", "/");

        var pendentes = await db.Tarefas.AsNoTracking()
            .Where(t => t.ConcluidaEm == null && t.Prazo != null && t.Prazo <= hoje)
            .OrderBy(t => t.Prazo).Select(t => new { t.Titulo, t.Prazo }).ToListAsync(ct);
        if (pendentes.Count == 0) return (l.Titulo, "Nenhuma tarefa para hoje.", "/tarefas");

        var deHoje = pendentes.Where(t => t.Prazo == hoje).Select(t => t.Titulo).ToList();
        var atrasadas = pendentes.Count - deHoje.Count;
        var partes = new List<string>();
        if (deHoje.Count > 0) partes.Add(string.Join(", ", deHoje.Take(4)) + (deHoje.Count > 4 ? $" e mais {deHoje.Count - 4}" : ""));
        if (atrasadas > 0) partes.Add(atrasadas == 1 ? "1 atrasada" : $"{atrasadas} atrasadas");
        return (l.Titulo, string.Join(" · ", partes), "/tarefas");
    }

    /// <summary>
    /// Envia para os aparelhos da pessoa atual (telas) ou, no agendador (sem pessoa atual), para os de <paramref name="dono"/>.
    /// </summary>
    private Task<(int Ok, int Falhas)> EnviarParaTodosAsync(FavoaDbContext db, string titulo, string corpo, string url, CancellationToken ct, Guid? dono = null) =>
        // Nas telas o filtro da pessoa já limita os aparelhos; no agendador, limita ao dono.
        EnviarParaAsync(db, titulo, corpo, url, db.PessoaAtual is null ? [dono] : null, ct);

    /// <summary>Envia para os aparelhos dessas pessoas; <paramref name="donos"/> nulo = todos os aparelhos que a consulta enxerga.</summary>
    private async Task<(int Ok, int Falhas)> EnviarParaAsync(FavoaDbContext db, string titulo, string corpo, string url, IReadOnlyList<Guid?>? donos, CancellationToken ct)
    {
        var consulta = db.AssinaturasPush.AsQueryable();
        if (donos is not null) consulta = consulta.Where(a => donos.Contains(a.PessoaId));
        var assinaturas = await consulta.ToListAsync(ct);
        if (assinaturas.Count == 0) return (0, 0);

        var (publica, privada) = await ObterChavesAsync(db, ct);
        var conteudo = JsonSerializer.SerializeToUtf8Bytes(new { title = titulo, body = corpo, url });
        int ok = 0, falhas = 0;

        foreach (var a in assinaturas)
        {
            try
            {
                using var pedido = new HttpRequestMessage(HttpMethod.Post, a.Endpoint)
                {
                    Content = new ByteArrayContent(WebPush.Criptografar(conteudo, WebPush.DeBase64Url(a.P256dh), WebPush.DeBase64Url(a.Auth))),
                };
                pedido.Content.Headers.ContentType = new("application/octet-stream");
                pedido.Content.Headers.ContentEncoding.Add("aes128gcm");
                pedido.Headers.TryAddWithoutValidation("TTL", "43200"); // se o celular estiver sem rede, tenta por até 12 h
                pedido.Headers.TryAddWithoutValidation("Urgency", "high"); // entrega na hora, mesmo com o celular em economia de bateria
                pedido.Headers.TryAddWithoutValidation("Authorization", WebPush.CabecalhoVapid(a.Endpoint, publica, privada, opcoes.PushContato, DateTimeOffset.UtcNow));

                using var resposta = await Http.SendAsync(pedido, ct);
                if (resposta.IsSuccessStatusCode) { ok++; continue; }

                if (resposta.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    // O aparelho cancelou (desinstalou o app, limpou dados): para de tentar.
                    db.AssinaturasPush.Remove(a);
                    log.LogInformation("Aparelho {Aparelho} removido: inscrição expirada.", a.Aparelho);
                }
                else
                {
                    log.LogWarning("Push para {Aparelho} falhou: {Status} {Corpo}", a.Aparelho, (int)resposta.StatusCode,
                        await resposta.Content.ReadAsStringAsync(ct));
                }
                falhas++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or ArgumentException or System.Security.Cryptography.CryptographicException)
            {
                log.LogWarning(ex, "Push para {Aparelho} falhou.", a.Aparelho);
                falhas++;
            }
        }
        await db.SaveChangesAsync(ct);
        return (ok, falhas);
    }

    /// <summary>Chaves VAPID deste computador; criadas na primeira vez. Não saem na exportação.</summary>
    private static async Task<(string Publica, string Privada)> ObterChavesAsync(FavoaDbContext db, CancellationToken ct)
    {
        await TravaChaves.WaitAsync(ct);
        try
        {
            var pub = await db.Configuracoes.FindAsync([ChavePublica], ct);
            var priv = await db.Configuracoes.FindAsync([ChavePrivada], ct);
            if (pub is not null && priv is not null) return (pub.Valor, priv.Valor);

            var (publica, privada) = WebPush.GerarChavesVapid();
            if (pub is null) db.Configuracoes.Add(new Configuracao { Chave = ChavePublica, Valor = publica }); else pub.Valor = publica;
            if (priv is null) db.Configuracoes.Add(new Configuracao { Chave = ChavePrivada, Valor = privada }); else priv.Valor = privada;
            await db.SaveChangesAsync(ct);
            return (publica, privada);
        }
        finally { TravaChaves.Release(); }
    }

    /// <summary>"iPhone", "Android", "Windows"... a partir do user agent, para a lista de aparelhos.</summary>
    public static string NomeAparelho(string? agente)
    {
        var a = agente ?? "";
        var sistema = a.Contains("iPhone") ? "iPhone" : a.Contains("iPad") ? "iPad" : a.Contains("Android") ? "Android"
            : a.Contains("Windows") ? "Windows" : a.Contains("Mac OS") ? "Mac" : a.Contains("Linux") ? "Linux" : "Aparelho";
        var navegador = a.Contains("Edg/") ? "Edge" : a.Contains("Firefox") ? "Firefox" : a.Contains("Chrome") || a.Contains("CriOS") ? "Chrome"
            : a.Contains("Safari") ? "Safari" : "";
        return navegador.Length == 0 ? sistema : $"{sistema} · {navegador}";
    }
}
