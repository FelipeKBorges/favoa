using Favoa.Core;
using Favoa.Core.Financas;
using Favoa.Core.Rotina;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Favoa.Data;

/// <summary>Onde o Fávoa guarda o banco e os backups. Tudo local.</summary>
public sealed class OpcoesFavoa
{
    private string? _pastaDados;

    /// <summary>Padrão (quando vazio): %LOCALAPPDATA%\Favoa no Windows.</summary>
    public string PastaDados
    {
        get => string.IsNullOrWhiteSpace(_pastaDados)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Favoa")
            : _pastaDados;
        set => _pastaDados = value;
    }

    /// <summary>Nome usado na saudação do painel.</summary>
    public string? Nome { get; set; }

    /// <summary>Se vazio, usa PastaDados\Backups. Aponte para uma pasta sincronizada (OneDrive, Drive) para ter cópia fora do PC.</summary>
    public string? PastaBackups { get; set; }

    public int BackupsMantidos { get; set; } = 14;

    /// <summary>
    /// Se o próprio PC acessar pelo IP da rede (ex.: http://192.168.0.15:5080), entra sem PIN.
    /// Desligue para exigir o PIN em tudo que não for localhost.
    /// </summary>
    public bool PcPeloIpDaRedeSemPin { get; set; } = true;

    /// <summary>
    /// Liga a tela "Criar uma conta" na entrada, para quem hospeda o Fávoa para várias casas. Desligado (padrão): só o primeiro acesso
    /// (no próprio computador) e os convites do dono criam logins.
    /// </summary>
    public bool CadastroAberto { get; set; }

    /// <summary>Contato que vai na assinatura das notificações (exigido pelos serviços de push). Não é usado para mais nada.</summary>
    public string PushContato { get; set; } = "mailto:favoa@example.com";

    public string CaminhoBanco => Path.Combine(PastaDados, "favoa.db");
    public string CaminhoBackups => string.IsNullOrWhiteSpace(PastaBackups) ? Path.Combine(PastaDados, "Backups") : PastaBackups;
}

public static class InfraestruturaExtensions
{
    public static IServiceCollection AddFavoaDados(this IServiceCollection services, OpcoesFavoa opcoes)
    {
        Directory.CreateDirectory(opcoes.PastaDados);

        services.AddSingleton(opcoes);
        services.AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite($"Data Source={opcoes.CaminhoBanco}"));
        // Por sessão (circuito do Blazor): dependem de quem está usando (IPessoaAtual).
        services.TryAddScoped<Favoa.Core.Pessoas.IPessoaAtual>(_ => new Favoa.Core.Pessoas.PessoaFixa(null));
        services.AddScoped<IFavoaServico, FavoaServico>();
        services.AddScoped<Favoa.Core.Fitness.IFitnessServico, FitnessServico>();
        services.AddScoped<Favoa.Core.Rotina.ILembretesServico, LembretesServico>();
        services.AddScoped<Favoa.Core.Projetos.IProjetosServico, ProjetosServico>();
        // Não dependem de pessoa (enxergam o banco todo ou cuidam do acesso).
        services.AddSingleton<Favoa.Core.Dados.IDadosFavoa, DadosFavoa>();
        services.AddSingleton<Favoa.Core.Acesso.IAcessoServico, AcessoServico>();
        services.AddScoped<Favoa.Core.Financas.ICadastrosServico, CadastrosServico>();
        services.AddHostedService<BackupDiario>();
        services.AddHostedService<AgendadorFavoa>();
        services.AddSingleton<Favoa.Core.Dados.ICopiaNuvem, CopiaNuvem>();
        services.AddSingleton<Favoa.Core.Fitness.IAlimentosOnline, OpenFoodFacts>();
        services.AddHostedService<CopiaNuvemAgendada>();
        return services;
    }

    /// <summary>Cria ou atualiza o banco e coloca os dados iniciais.</summary>
    public static async Task PrepararBancoAsync(this IServiceProvider sp)
    {
        var fabrica = sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        await using var db = await fabrica.CreateDbContextAsync();

        await AdotarBancoSemHistoricoAsync(db);
        await db.Database.MigrateAsync();

        if (!await db.Alimentos.AnyAsync(a => a.CodigoTaco != null))
            db.Alimentos.AddRange(LerTaco());
        // Tabela do IBGE (POF 2008-2009): completa a TACO com preparações (frito, ensopado...). Entra também em bancos antigos.
        if (!await db.Alimentos.AnyAsync(a => a.Fonte == Favoa.Core.Fitness.FontesAlimento.Ibge))
            db.Alimentos.AddRange(LerIbge());

        await db.SaveChangesAsync();
        await CarregarMedidasAsync(db);
        await PrepararPessoasAsync(db);
    }

    /// <summary>
    /// Na primeira vez com pessoas (bancos de antes delas), tudo o que existia vira da primeira pessoa: registros pessoais,
    /// lançamentos (quem lançou) e as metas. Base nova não tem o que migrar: o primeiro acesso (tela de entrada) cria a família e a pessoa.
    /// </summary>
    private static async Task PrepararPessoasAsync(FavoaDbContext db)
    {
        if (await db.Configuracoes.AnyAsync(c => c.Chave == AcessoServico.ChavePessoasMigradas)) return;

        var primeira = await db.Pessoas.OrderBy(p => p.Ordem).ThenBy(p => p.CriadoEm).FirstOrDefaultAsync();
        if (primeira is null)
        {
            db.Configuracoes.Add(new Configuracao { Chave = AcessoServico.ChavePessoasMigradas, Valor = DateTime.UtcNow.ToString("O") });
            await db.SaveChangesAsync();
            return;
        }

        await using var transacao = await db.Database.BeginTransactionAsync();

        foreach (var tabela in new[] { "rot_habito", "rot_agua", "rot_lembrete", "cfg_push_assinatura", "fit_ficha", "fit_sessao", "fit_plano_item", "fit_consumo", "fit_peso" })
            await db.Database.ExecuteSqlRawAsync($"UPDATE {tabela} SET PessoaId = {{0}} WHERE PessoaId IS NULL", primeira.Id);
        // Lançamentos antigos eram todos da primeira pessoa; os de recorrência ficam como "do Fávoa".
        await db.Database.ExecuteSqlRawAsync("UPDATE fin_lancamento SET PessoaId = {0} WHERE PessoaId IS NULL AND RecorrenciaId IS NULL", primeira.Id);
        db.Configuracoes.RemoveRange(db.Configuracoes.Where(c => c.Chave == "acesso_pin_hash" || c.Chave == "acesso_versao"));

        // Metas (calorias, macros, treinos, água) passam a ser de cada pessoa.
        string[] metas = [Favoa.Core.Fitness.MetasFitness.ChaveKcal, Favoa.Core.Fitness.MetasFitness.ChaveProteina, Favoa.Core.Fitness.MetasFitness.ChaveCarboidrato,
            Favoa.Core.Fitness.MetasFitness.ChaveGordura, Favoa.Core.Fitness.MetasFitness.ChaveTreinosSemana, Configuracao.MetaAguaMl];
        foreach (var cfg in await db.Configuracoes.Where(c => metas.Contains(c.Chave)).ToListAsync())
        {
            var nova = AbrirBanco.ChaveDaPessoa(cfg.Chave, primeira.Id);
            if (await db.Configuracoes.FindAsync(nova) is null) db.Configuracoes.Add(new Configuracao { Chave = nova, Valor = cfg.Valor });
            db.Configuracoes.Remove(cfg);
        }

        db.Configuracoes.Add(new Configuracao { Chave = AcessoServico.ChavePessoasMigradas, Valor = DateTime.UtcNow.ToString("O") });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    /// <summary>Os alimentos da TACO embutidos no app, para a dieta funcionar sem internet.</summary>
    private static IEnumerable<Favoa.Core.Fitness.Alimento> LerTaco()
    {
        using var stream = typeof(InfraestruturaExtensions).Assembly.GetManifestResourceStream("Favoa.Data.taco.json")
                           ?? throw new InvalidOperationException("taco.json não embutido");
        var itens = System.Text.Json.JsonSerializer.Deserialize<List<ItemTaco>>(stream,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? [];

        return itens
            .Where(t => t.Kcal is not null)
            .Select(t => new Favoa.Core.Fitness.Alimento
            {
                Nome = t.Nome,
                Grupo = t.Grupo,
                Kcal = t.Kcal ?? 0,
                Proteina = t.Proteina ?? 0,
                Carboidrato = t.Carboidrato ?? 0,
                Gordura = t.Gordura ?? 0,
                Fibra = t.Fibra ?? 0,
                CodigoTaco = t.Taco,
            })
            .ToList();
    }

    /// <summary>Tabela de Composição Nutricional dos Alimentos Consumidos no Brasil (IBGE, POF 2008-2009), por 100 g.</summary>
    public static IEnumerable<Favoa.Core.Fitness.Alimento> LerIbge()
    {
        using var stream = typeof(InfraestruturaExtensions).Assembly.GetManifestResourceStream("Favoa.Data.ibge.json")
                           ?? throw new InvalidOperationException("ibge.json não embutido");
        var itens = System.Text.Json.JsonSerializer.Deserialize<List<ItemIbge>>(stream,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? [];
        return itens.Select(t => new Favoa.Core.Fitness.Alimento
        {
            Nome = t.Nome,
            Grupo = "IBGE (POF 2008-2009)",
            Kcal = t.Kcal,
            Proteina = t.Proteina,
            Carboidrato = t.Carboidrato,
            Gordura = t.Gordura,
            Fibra = t.Fibra,
            Fonte = Favoa.Core.Fitness.FontesAlimento.Ibge,
            CodigoExterno = t.Codigo,
        }).ToList();
    }

    /// <summary>
    /// Medidas caseiras (unidade, fatia, colher...) dos alimentos do IBGE, da Tabela de Medidas Referidas da POF 2008-2009.
    /// Entra uma vez, também em bancos antigos; o que a pessoa cadastrar depois não é tocado.
    /// </summary>
    public static async Task CarregarMedidasAsync(FavoaDbContext db)
    {
        if (await db.Porcoes.AnyAsync(p => p.Origem == Favoa.Core.Fitness.OrigemPorcao.Ibge)) return;

        using var stream = typeof(InfraestruturaExtensions).Assembly.GetManifestResourceStream("Favoa.Data.medidas.json")
                           ?? throw new InvalidOperationException("medidas.json não embutido");
        var medidas = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<System.Text.Json.JsonElement>>>(stream) ?? [];

        // Alimentos da TACO que têm par no IBGE (ovo, pão francês, banana...) usam as medidas do par.
        using var streamTaco = typeof(InfraestruturaExtensions).Assembly.GetManifestResourceStream("Favoa.Data.medidas_taco.json")
                               ?? throw new InvalidOperationException("medidas_taco.json não embutido");
        var paresTaco = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(streamTaco) ?? [];

        var alimentos = await db.Alimentos.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.CodigoTaco != null || (a.Fonte == Favoa.Core.Fitness.FontesAlimento.Ibge && a.CodigoExterno != null))
            .Select(a => new { a.Id, a.CodigoTaco, a.CodigoExterno }).ToListAsync();

        foreach (var a in alimentos)
        {
            var codigo = a.CodigoTaco is { } taco ? paresTaco.GetValueOrDefault(taco.ToString()) : a.CodigoExterno;
            if (codigo is null || !medidas.TryGetValue(codigo, out var lista)) continue;
            var porcoes = lista.Select(x => (Nome: x[0].GetString()!, Gramas: x[1].GetDouble())).Where(x => x.Gramas > 0).ToList();
            // Ao abrir o alimento já vem "1 unidade" (ou "1 fatia"), se houver; senão fica em gramas.
            var padrao = porcoes.FirstOrDefault(x => x.Nome == "Unidade");
            if (padrao.Nome is null) padrao = porcoes.FirstOrDefault(x => x.Nome == "Fatia");
            foreach (var p in porcoes)
                db.Porcoes.Add(new Favoa.Core.Fitness.PorcaoAlimento
                {
                    AlimentoId = a.Id, Nome = p.Nome, Gramas = p.Gramas, Padrao = p.Nome == padrao.Nome, Origem = Favoa.Core.Fitness.OrigemPorcao.Ibge,
                });
        }
        await db.SaveChangesAsync();
    }

    private sealed record ItemIbge(string Codigo, string Nome, double Kcal, double Proteina, double Carboidrato, double Gordura, double Fibra);

    private sealed record ItemTaco(int Taco, string Nome, string Grupo, double? Kcal, double? Proteina, double? Carboidrato, double? Gordura, double? Fibra);

    /// <summary>
    /// As primeiras versões criavam o banco com EnsureCreated, sem histórico de migrações.
    /// Se encontrar um banco assim, registra a migração Inicial como já aplicada
    /// (o esquema é idêntico) para que as próximas migrações rodem normalmente.
    /// </summary>
    private static async Task AdotarBancoSemHistoricoAsync(FavoaDbContext db)
    {
        var conexao = db.Database.GetDbConnection();
        var abriAqui = conexao.State != System.Data.ConnectionState.Open;
        if (abriAqui) await conexao.OpenAsync();
        try
        {
            if (!await TabelaExiste(conexao, "fin_conta") || await TabelaExiste(conexao, "__EFMigrationsHistory"))
                return;

            var inicial = db.Database.GetMigrations().First();
            var historico = db.GetService<IHistoryRepository>();
            await db.Database.ExecuteSqlRawAsync(historico.GetCreateIfNotExistsScript());
            await db.Database.ExecuteSqlRawAsync(historico.GetInsertScript(new HistoryRow(inicial, ProductInfo.GetVersion())));
        }
        finally
        {
            if (abriAqui) await conexao.CloseAsync();
        }
    }

    private static async Task<bool> TabelaExiste(System.Data.Common.DbConnection conexao, string nome)
    {
        await using var cmd = conexao.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $nome";
        var p = cmd.CreateParameter();
        p.ParameterName = "$nome";
        p.Value = nome;
        cmd.Parameters.Add(p);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }
}

/// <summary>
/// Uma cópia consistente do banco por dia (API de backup do SQLite), mantendo as últimas N.
/// Roda ao iniciar e a cada hora confere se o backup do dia já existe.
/// </summary>
public sealed class BackupDiario(OpcoesFavoa opcoes, ILogger<BackupDiario> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { FazerSeNecessario(); }
            catch (Exception ex) { log.LogError(ex, "Falha no backup diário do Fávoa"); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    public string? FazerSeNecessario()
    {
        if (!File.Exists(opcoes.CaminhoBanco)) return null;

        Directory.CreateDirectory(opcoes.CaminhoBackups);
        var destino = Path.Combine(opcoes.CaminhoBackups, $"favoa-{DateTime.Now:yyyy-MM-dd}.db");
        if (File.Exists(destino)) return null;

        using (var origem = new SqliteConnection($"Data Source={opcoes.CaminhoBanco};Mode=ReadOnly"))
        using (var copia = new SqliteConnection($"Data Source={destino}"))
        {
            origem.Open();
            copia.Open();
            origem.BackupDatabase(copia);
        }
        SqliteConnection.ClearAllPools();

        foreach (var antigo in Directory.GetFiles(opcoes.CaminhoBackups, "favoa-*.db")
                     .OrderDescending()
                     .Skip(opcoes.BackupsMantidos))
        {
            File.Delete(antigo);
        }

        log.LogInformation("Backup do Fávoa salvo em {Destino}", destino);
        return destino;
    }
}

/// <summary>
/// Relógio do Fávoa: a cada 30 segundos dispara os lembretes que chegaram na hora;
/// ao iniciar e a cada 10 minutos lança as recorrências do dia.
/// </summary>
public sealed class AgendadorFavoa(IServiceScopeFactory escopos,
    ILogger<AgendadorFavoa> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        var ultimaRecorrencia = DateTime.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            var agora = DateTime.Now;
            if (agora - ultimaRecorrencia >= TimeSpan.FromMinutes(10))
            {
                try
                {
                    await using var escopo = escopos.CreateAsyncScope();
                    // "Nenhuma pessoa": lança as recorrências de todas as famílias (o IPessoaAtual do escopo, na web, esconderia tudo).
                    var cadastros = ActivatorUtilities.CreateInstance<CadastrosServico>(escopo.ServiceProvider, (Favoa.Core.Pessoas.IPessoaAtual)new Favoa.Core.Pessoas.PessoaFixa(null));
                    var criados = await cadastros.ProcessarRecorrenciasAsync(DateOnly.FromDateTime(agora), ct);
                    if (criados > 0) log.LogInformation("Recorrências: {Criados} lançamento(s) criado(s).", criados);
                    ultimaRecorrencia = agora;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Falha ao lançar recorrências"); }
            }

            try { await DispararLembretesAsync(escopos, agora, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Falha ao disparar lembretes"); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>
    /// Um ciclo de lembretes e avisos de tarefa. Usa sempre "nenhuma pessoa" (enxerga a casa toda e manda cada aviso
    /// para os aparelhos certos): o <see cref="Favoa.Core.Pessoas.IPessoaAtual"/> do escopo não serve aqui, porque na web,
    /// sem usuário logado, ele devolve um id vazio de propósito e esconderia todos os lembretes e aparelhos.
    /// </summary>
    public static async Task<int> DispararLembretesAsync(IServiceScopeFactory escopos, DateTime agora, CancellationToken ct = default)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var servico = ActivatorUtilities.CreateInstance<LembretesServico>(escopo.ServiceProvider, (Favoa.Core.Pessoas.IPessoaAtual)new Favoa.Core.Pessoas.PessoaFixa(null));
        return await servico.DispararDevidosAsync(agora, ct);
    }
}
