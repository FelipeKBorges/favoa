using Favoa.Core;
using Favoa.Core.Captura;
using Favoa.Core.Diario;
using Favoa.Core.Pessoas;
using Microsoft.EntityFrameworkCore;

namespace Favoa.Data;

/// <summary>Diário pessoal: só enxerga as entradas de quem está logado (filtro de casa e de pessoa), com texto e tags cifrados.</summary>
public sealed class DiarioServico(IDbContextFactory<FavoaDbContext> fabrica, IPessoaAtual pessoa, ICifraDiario cifra) : IDiarioServico
{
    public const int MaximoLetras = 20_000;

    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Now);

    public async Task<EntradaDia?> ObterAsync(DateOnly data, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var e = await db.Diario.AsNoTracking().FirstOrDefaultAsync(x => x.Data == data, ct);
        return e is null ? null : Abrir(e);
    }

    public async Task<ResultadoRegistro> SalvarAsync(DateOnly data, int? humor, int? energia, string texto, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        texto = (texto ?? "").Trim();
        var etiquetas = TagsDiario.Normalizar(tags);
        if (humor is < 1 or > 5) return new(false, "O humor vai de 1 a 5.");
        if (energia is < 1 or > 5) return new(false, "A energia vai de 1 a 5.");
        if (texto.Length > MaximoLetras) return new(false, $"O texto passou de {MaximoLetras} letras. Divida em dois dias.");
        if (data > Hoje.AddDays(1)) return new(false, "Não dá para escrever num dia que ainda não chegou.");
        if (texto.Length == 0 && humor is null && energia is null && etiquetas.Count == 0)
            return new(false, "Escreva algo ou marque o humor.");

        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var e = await db.Diario.FirstOrDefaultAsync(x => x.Data == data, ct);
        var nova = e is null;
        if (e is null)
        {
            e = new EntradaDiario { Data = data };
            db.Diario.Add(e);
        }
        e.Humor = humor;
        e.Energia = energia;
        e.Texto = cifra.Cifrar(texto, e.Id);
        e.Tags = cifra.Cifrar(string.Join(',', etiquetas), e.Id);
        e.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new(true, nova ? "Entrada guardada." : "Entrada atualizada.");
    }

    public async Task<ResultadoRegistro> ExcluirAsync(DateOnly data, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var apagadas = await db.Diario.Where(x => x.Data == data).ExecuteDeleteAsync(ct);
        return apagadas == 0 ? new(false, "Não havia entrada nesse dia.") : new(true, "Entrada excluída.");
    }

    public async Task<IReadOnlyList<ResumoEntrada>> ListarAsync(string? busca, string? tag, int limite = 60, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var todas = await db.Diario.AsNoTracking().OrderByDescending(x => x.Data).ToListAsync(ct);
        var alvo = Texto.Normalizar(busca);
        var etiqueta = TagsDiario.Normalizar([tag ?? ""]).FirstOrDefault();

        var resumos = new List<ResumoEntrada>();
        foreach (var e in todas)
        {
            var dia = Abrir(e);
            if (etiqueta is not null && !dia.Tags.Contains(etiqueta)) continue;
            if (alvo.Length > 0 && !(Texto.Normalizar(dia.Texto).Contains(alvo) || dia.Tags.Any(t => t.Contains(alvo)))) continue;
            resumos.Add(new ResumoEntrada(e.Data, e.Humor, e.Energia, Previa(dia), dia.Tags, dia.Legivel));
            if (resumos.Count >= limite) break;
        }
        return resumos;
    }

    public async Task<IReadOnlyList<DiaDoDiario>> MesAsync(int ano, int mes, CancellationToken ct = default)
    {
        var inicio = new DateOnly(ano, mes, 1);
        var fim = inicio.AddMonths(1);
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        return await db.Diario.AsNoTracking().Where(x => x.Data >= inicio && x.Data < fim).OrderBy(x => x.Data)
            .Select(x => new DiaDoDiario(x.Data, x.Humor, x.Energia)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> TagsFrequentesAsync(int limite = 12, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var todas = await db.Diario.AsNoTracking().Select(x => new { x.Id, x.Tags }).ToListAsync(ct);
        return todas.SelectMany(x => Etiquetas(cifra.Decifrar(x.Tags, x.Id)))
            .GroupBy(t => t).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(limite).Select(g => g.Key).ToList();
    }

    private EntradaDia Abrir(EntradaDiario e)
    {
        var texto = cifra.Decifrar(e.Texto, e.Id);
        var tags = cifra.Decifrar(e.Tags, e.Id);
        return texto is null || tags is null
            ? new EntradaDia(e.Data, e.Humor, e.Energia, "", [], e.AtualizadoEm, Legivel: false)
            : new EntradaDia(e.Data, e.Humor, e.Energia, texto, Etiquetas(tags), e.AtualizadoEm);
    }

    private static List<string> Etiquetas(string? tags) =>
        string.IsNullOrEmpty(tags) ? [] : [.. tags.Split(',', StringSplitOptions.RemoveEmptyEntries)];

    private static string Previa(EntradaDia dia)
    {
        if (!dia.Legivel) return "Não foi possível abrir esta entrada (chave do diário ausente).";
        var linha = string.Join(' ', dia.Texto.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return linha.Length <= 140 ? linha : linha[..140].TrimEnd() + "…";
    }
}
