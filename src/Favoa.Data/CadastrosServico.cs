using Favoa.Core;
using Favoa.Core.Captura;
using Favoa.Core.Financas;
using Microsoft.EntityFrameworkCore;

namespace Favoa.Data;

public sealed class CadastrosServico(IDbContextFactory<FavoaDbContext> fabrica, Favoa.Core.Pessoas.IPessoaAtual pessoa) : ICadastrosServico
{
    public async Task<QuadroCadastros> ObterAsync(DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirDaFamiliaAsync(pessoa, ct);
        var inicioMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var fimMes = inicioMes.AddMonths(1);

        var porCategoria = await db.Lancamentos.GroupBy(l => l.CategoriaId)
            .Select(g => new { g.Key, Qtd = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Qtd, ct);
        var gastoMes = await db.Lancamentos
            .Where(l => l.Tipo == TipoLancamento.Despesa && l.Data >= inicioMes && l.Data < fimMes)
            .GroupBy(l => l.CategoriaId).Select(g => new { g.Key, Total = g.Sum(l => l.ValorCentavos) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var categorias = (await db.Categorias.AsNoTracking().ToListAsync(ct))
            .OrderBy(c => c.Tipo).ThenBy(c => Texto.Normalizar(c.Nome) == "outros").ThenBy(c => c.Nome)
            .Select(c => new CategoriaCadastro(c.Id, c.Nome, c.Tipo, c.OrcamentoCentavos,
                porCategoria.GetValueOrDefault(c.Id), gastoMes.GetValueOrDefault(c.Id)))
            .ToList();

        // SQLite não soma long com sinal condicional direto no GroupBy de forma confiável: soma em memória.
        var movimentos = await db.Lancamentos.AsNoTracking().Select(l => new { l.ContaId, l.Tipo, l.ValorCentavos }).ToListAsync(ct);
        var contas = (await db.Contas.AsNoTracking().OrderBy(c => c.CriadoEm).ToListAsync(ct))
            .Select(c =>
            {
                var daConta = movimentos.Where(m => m.ContaId == c.Id).ToList();
                var saldo = daConta.Sum(m => m.Tipo == TipoLancamento.Receita ? m.ValorCentavos : -m.ValorCentavos);
                return new ContaCadastro(c.Id, c.Nome, c.Ativa, daConta.Count, saldo);
            })
            .OrderByDescending(c => c.Ativa)
            .ToList();

        var recorrencias = (await db.Recorrencias.AsNoTracking().Include(r => r.Categoria).Include(r => r.Conta).ToListAsync(ct))
            .Select(r => new RecorrenciaCadastro(r.Id, r.Tipo, r.Descricao, r.ValorCentavos, r.Categoria?.Nome ?? "", r.Conta?.Nome ?? "",
                r.DiaDoMes, r.Ativa, CalendarioRecorrencia.Proxima(r.DiaDoMes, r.Inicio, hoje), r.UltimaOcorrencia))
            .OrderByDescending(r => r.Ativa).ThenBy(r => r.Proxima)
            .ToList();

        return new QuadroCadastros(categorias, contas, recorrencias);
    }

    // ---------- Categorias ----------

    public async Task<ResultadoRegistro> SalvarCategoriaAsync(Guid? id, string nome, TipoLancamento tipo, long? orcamentoCentavos, CancellationToken ct = default)
    {
        nome = nome.Trim();
        if (nome.Length == 0) return new(false, "Dê um nome para a categoria.");
        if (nome.Length > 80) return new(false, "Nome muito longo (até 80 letras).");
        if (orcamentoCentavos is <= 0) orcamentoCentavos = null;

        await using var db = await fabrica.AbrirDaFamiliaAsync(pessoa, ct);
        var categoria = id is { } i ? await db.Categorias.FindAsync([i], ct) : null;
        if (id is not null && categoria is null) return new(false, "Essa categoria não existe mais.");
        tipo = categoria?.Tipo ?? tipo;

        var mesmoNome = (await db.Categorias.Where(c => c.Tipo == tipo && c.Id != id).Select(c => c.Nome).ToListAsync(ct))
            .Any(n => Texto.Normalizar(n) == Texto.Normalizar(nome));
        if (mesmoNome) return new(false, $"Já existe a categoria \"{nome}\". Para juntar as duas, exclua uma e mova os lançamentos.");

        if (categoria is null)
        {
            categoria = new Categoria { Tipo = tipo };
            db.Categorias.Add(categoria);
        }
        categoria.Nome = Texto.Capitalizar(nome);
        categoria.OrcamentoCentavos = tipo == TipoLancamento.Despesa ? orcamentoCentavos : null;
        await db.SaveChangesAsync(ct);
        return new(true, id is null ? $"Categoria {categoria.Nome} criada." : $"Categoria {categoria.Nome} salva.");
    }

    public async Task<ResultadoRegistro> ExcluirCategoriaAsync(Guid id, Guid? moverPara, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirDaFamiliaAsync(pessoa, ct);
        var categoria = await db.Categorias.FindAsync([id], ct);
        if (categoria is null) return new(false, "Essa categoria não existe mais.");

        var usada = await db.Lancamentos.AnyAsync(l => l.CategoriaId == id, ct) || await db.Recorrencias.AnyAsync(r => r.CategoriaId == id, ct);
        string? destinoNome = null;
        if (usada)
        {
            var destino = moverPara is { } m && m != id ? await db.Categorias.FindAsync([m], ct) : null;
            if (destino is null || destino.Tipo != categoria.Tipo)
                return new(false, "Essa categoria tem lançamentos. Escolha para qual categoria eles vão.");
            destinoNome = destino.Nome;
            await db.Lancamentos.Where(l => l.CategoriaId == id).ExecuteUpdateAsync(u => u.SetProperty(l => l.CategoriaId, destino.Id), ct);
            await db.Recorrencias.Where(r => r.CategoriaId == id).ExecuteUpdateAsync(u => u.SetProperty(r => r.CategoriaId, destino.Id), ct);
        }

        db.Categorias.Remove(categoria);
        await db.SaveChangesAsync(ct);
        return new(true, destinoNome is null ? $"Categoria {categoria.Nome} excluída." : $"{categoria.Nome} juntou-se a {destinoNome}.");
    }

    // ---------- Contas ----------

    public async Task<ResultadoRegistro> SalvarContaAsync(Guid? id, string nome, CancellationToken ct = default)
    {
        nome = nome.Trim();
        if (nome.Length == 0) return new(false, "Dê um nome para a conta.");
        if (nome.Length > 80) return new(false, "Nome muito longo (até 80 letras).");

        await using var db = await fabrica.AbrirDaFamiliaAsync(pessoa, ct);
        var conta = id is { } i ? await db.Contas.FindAsync([i], ct) : null;
        if (id is not null && conta is null) return new(false, "Essa conta não existe mais.");

        var mesmoNome = (await db.Contas.Where(c => c.Id != id).Select(c => c.Nome).ToListAsync(ct))
            .Any(n => Texto.Normalizar(n) == Texto.Normalizar(nome));
        if (mesmoNome) return new(false, $"Já existe a conta \"{nome}\".");

        if (conta is null)
        {
            conta = new Conta();
            db.Contas.Add(conta);
        }
        conta.Nome = Texto.Capitalizar(nome);
        await db.SaveChangesAsync(ct);
        return new(true, id is null ? $"Conta {conta.Nome} criada." : $"Conta {conta.Nome} salva.");
    }

    public async Task<ResultadoRegistro> DefinirContaAtivaAsync(Guid id, bool ativa, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirDaFamiliaAsync(pessoa, ct);
        var conta = await db.Contas.FindAsync([id], ct);
        if (conta is null) return new(false, "Essa conta não existe mais.");
        if (!ativa && !await db.Contas.AnyAsync(c => c.Ativa && c.Id != id, ct))
            return new(false, "Precisa ficar pelo menos uma conta ativa.");
        if (!ativa && await db.Recorrencias.AnyAsync(r => r.ContaId == id && r.Ativa, ct))
            return new(false, "Há recorrências usando essa conta. Troque a conta delas antes de arquivar.");

        conta.Ativa = ativa;
        await db.SaveChangesAsync(ct);
        return new(true, ativa ? $"Conta {conta.Nome} reativada." : $"Conta {conta.Nome} arquivada. O histórico continua no extrato.");
    }

    public async Task<ResultadoRegistro> ExcluirContaAsync(Guid id, Guid? moverPara, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirDaFamiliaAsync(pessoa, ct);
        var conta = await db.Contas.FindAsync([id], ct);
        if (conta is null) return new(false, "Essa conta não existe mais.");
        if (!await db.Contas.AnyAsync(c => c.Id != id, ct)) return new(false, "Precisa existir pelo menos uma conta.");

        var usada = await db.Lancamentos.AnyAsync(l => l.ContaId == id, ct) || await db.Recorrencias.AnyAsync(r => r.ContaId == id, ct);
        string? destinoNome = null;
        if (usada)
        {
            var destino = moverPara is { } m && m != id ? await db.Contas.FindAsync([m], ct) : null;
            if (destino is null) return new(false, "Essa conta tem lançamentos. Escolha para qual conta eles vão.");
            destinoNome = destino.Nome;
            await db.Lancamentos.Where(l => l.ContaId == id).ExecuteUpdateAsync(u => u.SetProperty(l => l.ContaId, destino.Id), ct);
            await db.Recorrencias.Where(r => r.ContaId == id).ExecuteUpdateAsync(u => u.SetProperty(r => r.ContaId, destino.Id), ct);
        }

        db.Contas.Remove(conta);
        await db.SaveChangesAsync(ct);
        if (!await db.Contas.AnyAsync(c => c.Ativa, ct))
        {
            var primeira = await db.Contas.OrderBy(c => c.CriadoEm).FirstAsync(ct);
            primeira.Ativa = true;
            await db.SaveChangesAsync(ct);
        }
        return new(true, destinoNome is null ? $"Conta {conta.Nome} excluída." : $"{conta.Nome} juntou-se a {destinoNome}.");
    }

    // ---------- Recorrências ----------

    public async Task<ResultadoRegistro> SalvarRecorrenciaAsync(DadosRecorrencia d, DateOnly hoje, CancellationToken ct = default)
    {
        var descricao = d.Descricao.Trim();
        if (d.ValorCentavos <= 0) return new(false, "Digite o valor.");
        if (d.DiaDoMes is < 1 or > 31) return new(false, "O dia precisa estar entre 1 e 31.");

        await using var db = await fabrica.AbrirDaFamiliaAsync(pessoa, ct);
        var r = d.Id is { } id ? await db.Recorrencias.FindAsync([id], ct) : null;
        if (d.Id is not null && r is null) return new(false, "Essa recorrência não existe mais.");

        // Só categorias cadastradas (criar é na aba Categorias). Sem nome, a recorrência leva o nome da categoria.
        var alvo = Texto.Normalizar(d.Categoria);
        var categoria = (await db.Categorias.Where(c => c.Tipo == d.Tipo).ToListAsync(ct)).FirstOrDefault(c => Texto.Normalizar(c.Nome) == alvo);
        if (categoria is null) return new(false, await FavoaServico.TextoEscolhaCategoria(db, d.Tipo, d.Categoria, ct));
        if (descricao.Length == 0) descricao = categoria.Nome;
        var conta = await ObterConta(db, d.Conta, ct);

        var nova = r is null;
        if (r is null)
        {
            r = new Recorrencia { Inicio = d.Inicio < hoje.AddYears(-1) ? hoje : d.Inicio };
            db.Recorrencias.Add(r);
        }
        else if (r.DiaDoMes != d.DiaDoMes)
        {
            // Mudou o dia: continua a partir de amanhã, sem lançar de novo o mês que já saiu.
            r.UltimaOcorrencia = r.UltimaOcorrencia is { } u && u >= hoje ? u : hoje;
        }

        r.Tipo = d.Tipo;
        r.Descricao = Texto.Capitalizar(descricao);
        r.ValorCentavos = d.ValorCentavos;
        r.CategoriaId = categoria.Id;
        r.ContaId = conta.Id;
        r.DiaDoMes = d.DiaDoMes;
        await db.SaveChangesAsync(ct);

        var criados = await ProcessarAsync(db, hoje, r.Id, ct);
        var quando = CalendarioRecorrencia.Proxima(r.DiaDoMes, r.Inicio, hoje);
        var texto = nova ? $"Recorrência {r.Descricao} criada" : $"Recorrência {r.Descricao} salva";
        return new(true, criados > 0 ? $"{texto}. Já lancei a de hoje." : $"{texto}. Próxima: {quando:dd/MM}.");
    }

    public async Task<ResultadoRegistro> DefinirRecorrenciaAtivaAsync(Guid id, bool ativa, DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirDaFamiliaAsync(pessoa, ct);
        var r = await db.Recorrencias.FindAsync([id], ct);
        if (r is null) return new(false, "Essa recorrência não existe mais.");
        if (ativa && !r.Ativa)
        {
            // Retomar não lança os meses que ficaram pausados; volta a valer a partir de amanhã.
            r.UltimaOcorrencia = r.UltimaOcorrencia is { } u && u > hoje ? u : hoje;
            if (!await db.Contas.AnyAsync(c => c.Id == r.ContaId && c.Ativa, ct))
                return new(false, "A conta dessa recorrência está arquivada. Reative a conta ou edite a recorrência.");
        }
        r.Ativa = ativa;
        await db.SaveChangesAsync(ct);
        return new(true, ativa
            ? $"{r.Descricao} retomada. Próxima: {CalendarioRecorrencia.Proxima(r.DiaDoMes, r.Inicio, hoje):dd/MM}."
            : $"{r.Descricao} pausada.");
    }

    public async Task<ResultadoRegistro> ExcluirRecorrenciaAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirDaFamiliaAsync(pessoa, ct);
        var r = await db.Recorrencias.FindAsync([id], ct);
        if (r is null) return new(false, "Essa recorrência não existe mais.");
        // Os lançamentos já feitos ficam; só perdem a ligação.
        await db.Lancamentos.Where(l => l.RecorrenciaId == id).ExecuteUpdateAsync(u => u.SetProperty(l => l.RecorrenciaId, (Guid?)null), ct);
        db.Recorrencias.Remove(r);
        await db.SaveChangesAsync(ct);
        return new(true, $"Recorrência {r.Descricao} excluída. Os lançamentos já feitos continuam.");
    }

    public async Task<int> ProcessarRecorrenciasAsync(DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirDaFamiliaAsync(pessoa, ct);
        return await ProcessarAsync(db, hoje, null, ct);
    }

    private static async Task<int> ProcessarAsync(FavoaDbContext db, DateOnly hoje, Guid? apenas, CancellationToken ct)
    {
        var ativas = await db.Recorrencias.Where(r => r.Ativa && r.Inicio <= hoje && (apenas == null || r.Id == apenas)).ToListAsync(ct);
        var criados = 0;
        foreach (var r in ativas)
        {
            foreach (var data in CalendarioRecorrencia.Pendentes(r.DiaDoMes, r.Inicio, r.UltimaOcorrencia, hoje))
            {
                db.Lancamentos.Add(new Lancamento
                {
                    ContaId = r.ContaId,
                    CategoriaId = r.CategoriaId,
                    Tipo = r.Tipo,
                    ValorCentavos = r.ValorCentavos,
                    Data = data,
                    Descricao = r.Descricao,
                    RecorrenciaId = r.Id,
                    FamiliaId = r.FamiliaId, // o agendador roda sem família atual: o lançamento herda a da recorrência
                });
                r.UltimaOcorrencia = data;
                criados++;
            }
        }
        if (criados > 0) await db.SaveChangesAsync(ct);
        return criados;
    }


    private static async Task<Conta> ObterConta(FavoaDbContext db, string? nome, CancellationToken ct)
    {
        var contas = await db.Contas.OrderBy(c => c.CriadoEm).ToListAsync(ct);
        return contas.FirstOrDefault(c => Texto.Normalizar(c.Nome) == Texto.Normalizar(nome))
               ?? contas.FirstOrDefault(c => c.Ativa)
               ?? db.Contas.Add(new Conta { Nome = "Carteira" }).Entity;
    }
}
