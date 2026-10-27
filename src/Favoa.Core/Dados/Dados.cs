namespace Favoa.Core.Dados;

/// <summary>Módulos que dá para exportar em CSV.</summary>
public enum ModuloCsv
{
    Lancamentos,
    Tarefas,
    Habitos,
    Agua,
    Treinos,
    Refeicoes,
    Peso,
}

public sealed record ArquivoGerado(string Nome, string TipoConteudo, byte[] Conteudo);

public sealed record BackupInfo(string Nome, DateTime CriadoEm, long Bytes);

public sealed record InformacoesDados(
    string CaminhoBanco,
    long TamanhoBancoBytes,
    string PastaBackups,
    IReadOnlyList<BackupInfo> Backups,
    ContagemDados Contagem);

public sealed record ContagemDados(int Lancamentos, int Contas, int Categorias, int Tarefas, int Habitos, int RegistrosHabito, int RegistrosAgua)
{
    public int Total => Lancamentos + Contas + Categorias + Tarefas + Habitos + RegistrosHabito + RegistrosAgua;
}

/// <summary>Resultado de ler um arquivo de importação, antes de aplicar.</summary>
public sealed record AnaliseImportacao(bool Valido, string Mensagem, ContagemDados? NoArquivo, DateTime? ExportadoEm);

public sealed record ResultadoImportacao(bool Sucesso, string Mensagem, string? BackupAntes);

/// <summary>
/// Exportar, importar e cuidar do arquivo de dados. Separado de <see cref="IFavoaServico"/>
/// porque mexe com arquivos e com o banco inteiro, não com registros do dia a dia.
/// </summary>
public interface IDadosFavoa
{
    /// <summary>True quando a base tem uma família só (instalação doméstica). Importar e começar do zero só valem nesse caso.</summary>
    Task<bool> BancoDeUmaFamiliaAsync(CancellationToken ct = default);

    Task<InformacoesDados> ObterInformacoesAsync(CancellationToken ct = default);
    Task<string> FazerBackupAgoraAsync(CancellationToken ct = default);

    Task<ArquivoGerado> ExportarJsonAsync(CancellationToken ct = default);
    Task<ArquivoGerado> ExportarCsvAsync(ModuloCsv modulo, CancellationToken ct = default);

    Task<AnaliseImportacao> AnalisarImportacaoAsync(byte[] json, CancellationToken ct = default);

    /// <summary>Substitui todos os dados pelos do arquivo. Faz um backup antes.</summary>
    Task<ResultadoImportacao> ImportarJsonAsync(byte[] json, CancellationToken ct = default);

    /// <summary>
    /// Apaga os registros (lançamentos, recorrências, tarefas, hábitos, água, treinos feitos, refeições, peso, lembretes e projetos)
    /// e mantém pessoas, PINs, aparelhos, configurações, contas, categorias, fichas, plano e metas. Faz um backup antes.
    /// </summary>
    Task<ResultadoImportacao> ComecarDoZeroAsync(CancellationToken ct = default);

    /// <summary>Quanto há de histórico financeiro numa casa (para mostrar antes de apagar).</summary>
    Task<ContagemFinancas> ContarFinancasAsync(Guid familiaId, CancellationToken ct = default);

    /// <summary>
    /// Zera o histórico financeiro de UMA casa (funciona com várias famílias no mesmo Fávoa): apaga os lançamentos e, se pedido, as contas fixas.
    /// Ficam contas, categorias, limites e orçamento. Contas fixas mantidas só voltam a lançar a partir de amanhã. Faz um backup antes.
    /// </summary>
    Task<ResultadoImportacao> ZerarFinancasAsync(Guid familiaId, bool apagarContasFixas, CancellationToken ct = default);
}

public sealed record ContagemFinancas(int Lancamentos, int ContasFixas);
