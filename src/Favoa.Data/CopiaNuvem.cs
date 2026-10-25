using System.Diagnostics;
using System.Text.Json;
using Favoa.Core;
using Favoa.Core.Dados;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Favoa.Data;

/// <summary>
/// Copia o Fávoa para uma pasta do OneDrive (ou outra sincronizada). A configuração e o estado ficam em
/// <c>PastaDados\nuvem.json</c>, fora do banco: são deste computador e não podem mudar o banco a cada cópia.
///
/// Estrutura na nuvem:
///   Backups\favoa-AAAA-MM-DD.db   uma cópia por dia (mantém 30)
///   Dados\favoa.db                o banco mais recente
///   Dados\favoa-dados.json        todas as tabelas em JSON (importável em Seus dados)
///   Dados\csv\*.csv               tabelas em planilha (Excel)
///   Codigo\favoa.bundle           o projeto com todo o histórico (git clone favoa.bundle)
/// </summary>
public sealed class CopiaNuvem(OpcoesFavoa opcoes, IDadosFavoa dados, ILogger<CopiaNuvem> log) : ICopiaNuvem
{
    public const int BackupsMantidos = 30;
    private static readonly SemaphoreSlim Trava = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private string ArquivoEstado => Path.Combine(opcoes.PastaDados, "nuvem.json");

    private sealed class Estado
    {
        public string? Pasta { get; set; }
        public string? PastaProjeto { get; set; }
        public DateTime? UltimaCopia { get; set; }
        public string? UltimoErro { get; set; }
        public string? Detalhe { get; set; }
        public DateTime? BancoCopiadoEm { get; set; }   // hora de modificação do banco na última cópia de dados
        public DateOnly? UltimoBackupDiario { get; set; }
        public string? UltimoCommit { get; set; }
        public DateOnly? UltimoCodigo { get; set; }
    }

    public async Task<EstadoNuvem> ObterAsync(CancellationToken ct = default)
    {
        var e = await LerAsync(ct);
        return new EstadoNuvem(e.Pasta, e.PastaProjeto, SugerirPasta(), SugerirProjeto(), e.UltimaCopia, e.UltimoErro, e.Detalhe);
    }

    public async Task<ResultadoRegistro> ConfigurarAsync(string? pasta, string? pastaProjeto, CancellationToken ct = default)
    {
        await Trava.WaitAsync(ct);
        try
        {
            var e = await LerAsync(ct);
            if (string.IsNullOrWhiteSpace(pasta))
            {
                e.Pasta = null;
                e.UltimoErro = null;
                await SalvarAsync(e, ct);
                return new(true, "Cópia no OneDrive desligada. Os arquivos que já estão lá continuam.");
            }

            pasta = pasta.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(pasta)) return new(false, "Use o caminho completo da pasta (ex.: C:\\Users\\voce\\OneDrive\\Fávoa).");
            if (IgualOuDentro(pasta, opcoes.PastaDados)) return new(false, "Escolha uma pasta fora da pasta de dados do Fávoa.");
            try { Directory.CreateDirectory(pasta); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return new(false, $"Não consegui usar essa pasta: {ex.Message}");
            }

            pastaProjeto = string.IsNullOrWhiteSpace(pastaProjeto) ? null : pastaProjeto.Trim().Trim('"');
            if (pastaProjeto is not null && !Directory.Exists(Path.Combine(pastaProjeto, ".git")))
                return new(false, "A pasta do projeto precisa ser a do Fávoa com o histórico do git (a que tem a pasta .git).");

            var mudouPasta = !string.Equals(e.Pasta, pasta, StringComparison.OrdinalIgnoreCase);
            e.Pasta = pasta;
            e.PastaProjeto = pastaProjeto;
            if (mudouPasta) { e.BancoCopiadoEm = null; e.UltimoBackupDiario = null; e.UltimoCommit = null; e.UltimoCodigo = null; }
            await SalvarAsync(e, ct);
        }
        finally { Trava.Release(); }

        return await CopiarAsync(forcar: true, ct);
    }

    public async Task<ResultadoRegistro> CopiarAsync(bool forcar, CancellationToken ct = default)
    {
        await Trava.WaitAsync(ct);
        try
        {
            var e = await LerAsync(ct);
            if (string.IsNullOrWhiteSpace(e.Pasta)) return new(false, "A cópia no OneDrive está desligada.");
            if (!File.Exists(opcoes.CaminhoBanco)) return new(false, "Ainda não há banco para copiar.");

            var feitos = new List<string>();
            var avisos = new List<string>();
            try
            {
                Directory.CreateDirectory(e.Pasta);
                var hoje = DateOnly.FromDateTime(DateTime.Now);
                var modificadoEm = File.GetLastWriteTimeUtc(opcoes.CaminhoBanco);

                // 1. Dados atuais: só quando o banco mudou desde a última cópia.
                if (forcar || e.BancoCopiadoEm != modificadoEm)
                {
                    await CopiarDadosAsync(Path.Combine(e.Pasta, "Dados"), ct);
                    e.BancoCopiadoEm = modificadoEm;
                    feitos.Add("dados");
                }

                // 2. Backup do dia.
                if (forcar || e.UltimoBackupDiario != hoje)
                {
                    var pastaBackups = Path.Combine(e.Pasta, "Backups");
                    Directory.CreateDirectory(pastaBackups);
                    CopiarBanco(Path.Combine(pastaBackups, $"favoa-{hoje:yyyy-MM-dd}.db"));
                    foreach (var antigo in Directory.GetFiles(pastaBackups, "favoa-*.db").OrderDescending().Skip(BackupsMantidos))
                        File.Delete(antigo);
                    e.UltimoBackupDiario = hoje;
                    feitos.Add("backup do dia");
                }

                // 3. Código: quando há commit novo (ou uma vez por dia).
                if (!string.IsNullOrWhiteSpace(e.PastaProjeto))
                {
                    var commit = await GitAsync(e.PastaProjeto, ["rev-parse", "HEAD"], ct);
                    if (commit.Codigo != 0)
                        avisos.Add(commit.Saida.Contains("não encontrado") ? "git não encontrado: o código não foi copiado" : "não consegui ler o git do projeto");
                    else if (forcar || e.UltimoCommit != commit.Saida.Trim() || e.UltimoCodigo != hoje)
                    {
                        var pastaCodigo = Path.Combine(e.Pasta, "Codigo");
                        Directory.CreateDirectory(pastaCodigo);
                        var temporario = Path.Combine(pastaCodigo, "favoa.bundle.tmp");
                        var bundle = await GitAsync(e.PastaProjeto, ["bundle", "create", temporario, "--all"], ct);
                        if (bundle.Codigo != 0) avisos.Add("git bundle falhou: " + Primeira(bundle.Saida));
                        else
                        {
                            File.Move(temporario, Path.Combine(pastaCodigo, "favoa.bundle"), overwrite: true);
                            e.UltimoCommit = commit.Saida.Trim();
                            e.UltimoCodigo = hoje;
                            feitos.Add("código");
                        }
                        if (File.Exists(temporario)) File.Delete(temporario);
                    }
                }

                if (feitos.Count > 0) e.UltimaCopia = DateTime.Now;
                e.UltimoErro = avisos.Count > 0 ? string.Join("; ", avisos) : null;
                e.Detalhe = feitos.Count > 0 ? "Copiado: " + string.Join(", ", feitos) + "." : e.Detalhe;
                await SalvarAsync(e, ct);

                if (feitos.Count == 0 && avisos.Count == 0) return new(true, "Nada mudou desde a última cópia.");
                var texto = feitos.Count > 0 ? $"Copiado para o OneDrive: {string.Join(", ", feitos)}." : "";
                return new(avisos.Count == 0, (texto + (avisos.Count > 0 ? " Atenção: " + e.UltimoErro : "")).Trim());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
            {
                log.LogWarning(ex, "Falha na cópia para {Pasta}", e.Pasta);
                e.UltimoErro = ex.Message;
                await SalvarAsync(e, ct);
                return new(false, $"Não deu para copiar: {ex.Message}");
            }
        }
        finally { Trava.Release(); }
    }

    private async Task CopiarDadosAsync(string pasta, CancellationToken ct)
    {
        var csv = Path.Combine(pasta, "csv");
        Directory.CreateDirectory(csv);
        CopiarBanco(Path.Combine(pasta, "favoa.db"));
        await GravarAsync(Path.Combine(pasta, "favoa-dados.json"), (await dados.ExportarJsonAsync(ct)).Conteudo, ct);
        foreach (var modulo in Enum.GetValues<ModuloCsv>())
        {
            var arquivo = await dados.ExportarCsvAsync(modulo, ct);
            // Sem a data no nome: a planilha é sempre a mais recente e o OneDrive guarda as versões antigas.
            var nome = string.Join('-', arquivo.Nome.Split('-').Take(2)) + ".csv";
            await GravarAsync(Path.Combine(csv, nome), arquivo.Conteudo, ct);
        }
    }

    /// <summary>Cópia consistente pela API de backup do SQLite (funciona com o app aberto), gravada por cima de forma segura.</summary>
    private void CopiarBanco(string destino)
    {
        var temporario = destino + ".tmp";
        if (File.Exists(temporario)) File.Delete(temporario);
        using (var origem = new SqliteConnection($"Data Source={opcoes.CaminhoBanco};Mode=ReadOnly;Pooling=False"))
        using (var copia = new SqliteConnection($"Data Source={temporario};Pooling=False"))
        {
            origem.Open();
            copia.Open();
            origem.BackupDatabase(copia);
        }
        SqliteConnection.ClearAllPools();
        if (VerificadorBackup.Verificar(temporario) is { Ok: false } falha)
        {
            File.Delete(temporario);
            throw new IOException($"A cópia do banco não passou na verificação ({falha.Detalhe}); a anterior foi mantida.");
        }
        File.Move(temporario, destino, overwrite: true);
    }

    private static async Task GravarAsync(string destino, byte[] conteudo, CancellationToken ct)
    {
        var temporario = destino + ".tmp";
        await File.WriteAllBytesAsync(temporario, conteudo, ct);
        File.Move(temporario, destino, overwrite: true);
    }

    private static async Task<(int Codigo, string Saida)> GitAsync(string pasta, string[] argumentos, CancellationToken ct)
    {
        var info = new ProcessStartInfo(CopiaNuvemAgendada.CaminhoGit()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        // safe.directory: o Fávoa roda em segundo plano e o git pode estranhar o dono da pasta.
        foreach (var a in (string[])["-c", "safe.directory=*", "-C", pasta, .. argumentos]) info.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(info)!;
            var saida = p.StandardOutput.ReadToEndAsync(ct);
            var erro = p.StandardError.ReadToEndAsync(ct);
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromMinutes(2));
            await p.WaitForExitAsync(limite.Token);
            return (p.ExitCode, p.ExitCode == 0 ? await saida : await erro + await saida);
        }
        catch (System.ComponentModel.Win32Exception) { return (-1, "git não encontrado"); }
    }

    private async Task<Estado> LerAsync(CancellationToken ct)
    {
        if (!File.Exists(ArquivoEstado)) return new Estado();
        try
        {
            await using var f = File.OpenRead(ArquivoEstado);
            return await JsonSerializer.DeserializeAsync<Estado>(f, Json, ct) ?? new Estado();
        }
        catch (JsonException) { return new Estado(); }
    }

    private async Task SalvarAsync(Estado e, CancellationToken ct)
    {
        Directory.CreateDirectory(opcoes.PastaDados);
        await GravarAsync(ArquivoEstado, JsonSerializer.SerializeToUtf8Bytes(e, Json), ct);
    }

    /// <summary>OneDrive pessoal ("Seu Nome – Pessoal" no Explorer) com uma subpasta Fávoa.</summary>
    public static string? SugerirPasta()
    {
        var onedrive = new[] { "OneDriveConsumer", "OneDrive" }
            .Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p));
        if (onedrive is null && OperatingSystem.IsWindows())
        {
            // Em segundo plano as variáveis podem não vir: o caminho também fica no registro do OneDrive.
            using var chave = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts\Personal");
            if (chave?.GetValue("UserFolder") is string pasta && Directory.Exists(pasta)) onedrive = pasta;
        }
        if (onedrive is null)
        {
            var padrao = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
            if (Directory.Exists(padrao)) onedrive = padrao;
        }
        return onedrive is null ? null : Path.Combine(onedrive, "Fávoa");
    }

    /// <summary>A pasta do projeto, se estiver no lugar de sempre (Documentos\favoa).</summary>
    public static string? SugerirProjeto()
    {
        var candidatos = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "favoa"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "favoa"),
        };
        return candidatos.FirstOrDefault(c => Directory.Exists(Path.Combine(c, ".git")));
    }

    private static bool IgualOuDentro(string pasta, string base_)
    {
        var a = Path.GetFullPath(pasta).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var b = Path.GetFullPath(base_).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }

    private static string Primeira(string texto) => texto.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
}

/// <summary>A cada 30 minutos copia para o OneDrive o que mudou (só se a cópia estiver ligada).</summary>
public sealed class CopiaNuvemAgendada(ICopiaNuvem copia, ILogger<CopiaNuvemAgendada> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        do
        {
            try
            {
                if ((await copia.ObterAsync(ct)).Ativa) await copia.CopiarAsync(forcar: false, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Falha na cópia para o OneDrive"); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>
    /// Em segundo plano (tarefa S4U) o PATH pode vir sem o git; procura nos lugares de instalação comuns do Windows.
    /// </summary>
    internal static string CaminhoGit()
    {
        if (!OperatingSystem.IsWindows()) return "git";
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidatos =
        [
            Path.Combine(pf, "Git", "cmd", "git.exe"),
            Path.Combine(pf, "Git", "bin", "git.exe"),
            Path.Combine(pf86, "Git", "cmd", "git.exe"),
            Path.Combine(local, "Programs", "Git", "cmd", "git.exe"),
        ];
        return candidatos.FirstOrDefault(File.Exists) ?? "git";
    }
}
