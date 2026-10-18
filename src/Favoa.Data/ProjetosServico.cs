using Favoa.Core;
using Favoa.Core.Captura;
using Favoa.Core.Edicao;
using Favoa.Core.Financas;
using Favoa.Core.Pessoas;
using Favoa.Core.Projetos;
using Favoa.Core.Rotina;
using Microsoft.EntityFrameworkCore;

namespace Favoa.Data;

/// <summary>Projetos (viagem, trabalho, casa). O filtro do contexto já mostra só os da casa e os da pessoa atual.</summary>
public sealed class ProjetosServico(IDbContextFactory<FavoaDbContext> fabrica, IPessoaAtual pessoa) : IProjetosServico
{
    public async Task<IReadOnlyList<ProjetoResumo>> ListarAsync(DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var projetos = await db.Projetos.AsNoTracking().ToListAsync(ct);
        if (projetos.Count == 0) return [];
        var ids = projetos.Select(p => p.Id).ToList();

        var tarefas = await db.Tarefas.AsNoTracking().Where(t => t.ProjetoId != null && ids.Contains(t.ProjetoId.Value)).ToListAsync(ct);
        var lancamentos = await db.Lancamentos.AsNoTracking().Where(l => l.ProjetoId != null && ids.Contains(l.ProjetoId.Value))
            .Select(l => new { l.ProjetoId, l.Tipo, l.ValorCentavos }).ToListAsync(ct);
        var itens = await db.ItensProjeto.AsNoTracking().Where(i => ids.Contains(i.ProjetoId)).ToListAsync(ct);

        return projetos
            .Select(p => Resumir(p, hoje,
                tarefas.Where(t => t.ProjetoId == p.Id).ToList(),
                lancamentos.Where(l => l.ProjetoId == p.Id).Sum(l => l.Tipo == TipoLancamento.Despesa ? l.ValorCentavos : 0),
                lancamentos.Where(l => l.ProjetoId == p.Id).Sum(l => l.Tipo == TipoLancamento.Receita ? l.ValorCentavos : 0),
                itens.Where(i => i.ProjetoId == p.Id).ToList()))
            .OrderBy(r => Ordem(r.Situacao))
            .ThenBy(r => r.DiasAte(hoje) ?? int.MaxValue)
            .ThenBy(r => r.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<ProjetoDetalhe?> ObterAsync(Guid id, DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var p = await db.Projetos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;

        var tarefas = await db.Tarefas.AsNoTracking().Where(t => t.ProjetoId == id).ToListAsync(ct);
        var lancamentos = await db.Lancamentos.AsNoTracking().Include(l => l.Categoria).Where(l => l.ProjetoId == id)
            .OrderByDescending(l => l.Data).ThenByDescending(l => l.CriadoEm).ToListAsync(ct);
        var itens = await db.ItensProjeto.AsNoTracking().Where(i => i.ProjetoId == id).OrderBy(i => i.Ordem).ThenBy(i => i.CriadoEm).ToListAsync(ct);

        var resumo = Resumir(p, hoje, tarefas,
            lancamentos.Where(l => l.Tipo == TipoLancamento.Despesa).Sum(l => l.ValorCentavos),
            lancamentos.Where(l => l.Tipo == TipoLancamento.Receita).Sum(l => l.ValorCentavos), itens);

        return new ProjetoDetalhe(
            resumo,
            p.Descricao,
            p.PessoaId,
            tarefas.OrderBy(t => t.ConcluidaEm is not null).ThenBy(t => t.Prazo is null).ThenBy(t => t.Prazo).ThenBy(t => t.CriadoEm)
                .Select(t => new TarefaProjeto(t.Id, t.Titulo, t.Prazo, t.ConcluidaEm is not null, t.EstaAtrasada(hoje))).ToList(),
            lancamentos.Select(l => new LancamentoProjeto(l.Id, l.Data, l.Categoria?.Nome ?? "", l.ValorCentavos, l.Tipo == TipoLancamento.Receita)).ToList(),
            itens.OrderBy(i => i.Tipo == TipoItemProjeto.Marco ? i.Data ?? DateOnly.MaxValue : DateOnly.MinValue).ThenBy(i => i.Ordem)
                .Select(i => new ItemProjetoInfo(i.Id, i.Tipo, i.Texto, i.Feito, i.Data, i.Hora)).ToList());
    }

    public async Task<ResultadoRegistro> SalvarAsync(DadosProjeto d, CancellationToken ct = default)
    {
        var nome = d.Nome.Trim();
        if (nome.Length == 0) return new(false, "Dê um nome ao projeto (ex.: Viagem Chile).");
        if (nome.Length > 80) return new(false, "Nome muito longo (até 80 letras).");
        if (d.Inicio is { } i && d.Fim is { } f && f < i) return new(false, "O fim não pode ser antes do início.");
        if (d.OrcamentoCentavos is < 0) return new(false, "O orçamento não pode ser negativo.");

        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var p = d.Id is { } id ? await db.Projetos.FirstOrDefaultAsync(x => x.Id == id, ct) : null;
        if (d.Id is not null && p is null) return new(false, "Esse projeto não existe mais.");

        var novo = p is null;
        if (p is null)
        {
            p = new Projeto();
            db.Projetos.Add(p);
            var ordem = 0;
            foreach (var texto in TiposProjeto.Modelo(d.Tipo))
                db.ItensProjeto.Add(new ItemProjeto { ProjetoId = p.Id, Tipo = TipoItemProjeto.Checklist, Texto = texto, Ordem = ordem++ });
        }

        p.Nome = Texto.Capitalizar(nome);
        p.Tipo = d.Tipo;
        p.Situacao = d.Situacao;
        p.Inicio = d.Inicio;
        p.Fim = d.Fim;
        p.OrcamentoCentavos = d.OrcamentoCentavos is > 0 ? d.OrcamentoCentavos : null;
        p.Descricao = (d.Descricao ?? "").Trim();
        // Da casa: sem dono. Pessoal: da pessoa atual (no PC sem pessoa, continua da casa).
        p.PessoaId = d.DaCasa ? null : db.PessoaAtual ?? p.PessoaId;
        await db.SaveChangesAsync(ct);

        return new(true, novo ? $"Projeto criado: {p.Nome}." : $"{p.Nome} salvo.", new ReferenciaRegistro(TipoReferencia.Projeto, p.Id));
    }

    public async Task<ResultadoRegistro> ExcluirAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var p = await db.Projetos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return new(false, "Esse projeto não existe mais.");

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        await db.Tarefas.Where(t => t.ProjetoId == id).ExecuteUpdateAsync(u => u.SetProperty(t => t.ProjetoId, (Guid?)null), ct);
        await db.Lancamentos.Where(l => l.ProjetoId == id).ExecuteUpdateAsync(u => u.SetProperty(l => l.ProjetoId, (Guid?)null), ct);
        await db.ItensProjeto.Where(i => i.ProjetoId == id).ExecuteDeleteAsync(ct);
        db.Projetos.Remove(p);
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new(true, $"{p.Nome} excluído. As tarefas e os lançamentos dele continuam no Fávoa.");
    }

    public async Task<ResultadoRegistro> SalvarItemAsync(DadosItemProjeto d, CancellationToken ct = default)
    {
        var texto = d.Texto.Trim();
        if (texto.Length == 0) return new(false, d.Tipo == TipoItemProjeto.Marco ? "Diga o que acontece nessa data." : "Escreva o item.");
        if (texto.Length > 1000) return new(false, "Texto muito longo.");
        if (d.Tipo == TipoItemProjeto.Marco && d.Data is null) return new(false, "Escolha a data do marco.");

        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        if (!await db.Projetos.AnyAsync(p => p.Id == d.ProjetoId, ct)) return new(false, "Esse projeto não existe mais.");

        var item = d.Id is { } id ? await db.ItensProjeto.FirstOrDefaultAsync(i => i.Id == id && i.ProjetoId == d.ProjetoId, ct) : null;
        if (item is null)
        {
            var ordem = await db.ItensProjeto.Where(i => i.ProjetoId == d.ProjetoId).MaxAsync(i => (int?)i.Ordem, ct) ?? -1;
            item = new ItemProjeto { ProjetoId = d.ProjetoId, Tipo = d.Tipo, Ordem = ordem + 1 };
            db.ItensProjeto.Add(item);
        }
        item.Texto = texto;
        item.Data = d.Tipo == TipoItemProjeto.Marco ? d.Data : null;
        item.Hora = d.Tipo == TipoItemProjeto.Marco ? d.Hora : null;
        await db.SaveChangesAsync(ct);
        return new(true, d.Tipo switch
        {
            TipoItemProjeto.Marco => $"Marco em {d.Data:dd/MM}: {texto}",
            TipoItemProjeto.Nota => "Nota salva.",
            _ => $"Na lista: {texto}",
        });
    }

    public async Task AlternarItemAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var item = await ItemVisivel(db, itemId, ct);
        if (item is null) return;
        item.Feito = !item.Feito;
        await db.SaveChangesAsync(ct);
    }

    public async Task ExcluirItemAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var item = await ItemVisivel(db, itemId, ct);
        if (item is null) return;
        db.ItensProjeto.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ResultadoRegistro> CriarTarefaAsync(Guid projetoId, string titulo, DateOnly? prazo, CancellationToken ct = default)
    {
        titulo = titulo.Trim();
        if (titulo.Length == 0) return new(false, "Escreva o que precisa ser feito.");
        if (titulo.Length > 200) return new(false, "Título muito longo.");

        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var p = await db.Projetos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == projetoId, ct);
        if (p is null) return new(false, "Esse projeto não existe mais.");

        var tarefa = new Tarefa { Titulo = Texto.Capitalizar(titulo), Prazo = prazo, ProjetoId = projetoId };
        db.Tarefas.Add(tarefa);
        await db.SaveChangesAsync(ct);
        return new(true, prazo is { } d ? $"Tarefa de {p.Nome} para {d:dd/MM}: {tarefa.Titulo}" : $"Tarefa de {p.Nome}: {tarefa.Titulo}",
            new ReferenciaRegistro(TipoReferencia.Tarefa, tarefa.Id));
    }

    /// <summary>
    /// Acha o projeto pelo que a pessoa escreveu depois do # ("#chile" acha "Viagem Chile").
    /// Primeiro nome igual, depois uma palavra igual, depois começo de palavra. Concluídos ficam por último.
    /// </summary>
    internal static async Task<Projeto?> AcharPorApelido(FavoaDbContext db, string? apelido, CancellationToken ct) =>
        (await db.Projetos.ToListAsync(ct))
            .Select(p => (Projeto: p, Nota: TiposProjeto.Combina(p.Nome, apelido)))
            .Where(x => x.Nota > 0)
            .OrderByDescending(x => x.Nota)
            .ThenBy(x => x.Projeto.Situacao == SituacaoProjeto.Concluido)
            .Select(x => x.Projeto)
            .FirstOrDefault();

    private static async Task<ItemProjeto?> ItemVisivel(FavoaDbContext db, Guid itemId, CancellationToken ct)
    {
        var item = await db.ItensProjeto.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        // O item só vale se o projeto dele está visível para a pessoa (filtro do contexto).
        return item is not null && await db.Projetos.AnyAsync(p => p.Id == item.ProjetoId, ct) ? item : null;
    }

    private static int Ordem(SituacaoProjeto s) => s switch
    {
        SituacaoProjeto.EmAndamento => 0,
        SituacaoProjeto.Planejando => 1,
        SituacaoProjeto.Ideia => 2,
        SituacaoProjeto.Pausado => 3,
        _ => 4,
    };

    internal static ProjetoResumo Resumir(Projeto p, DateOnly hoje, List<Tarefa> tarefas, long gasto, long recebido, List<ItemProjeto> itens)
    {
        var checklist = itens.Where(i => i.Tipo == TipoItemProjeto.Checklist).ToList();
        return new ProjetoResumo(p.Id, p.Nome, p.Tipo, p.Situacao, p.Inicio, p.Fim, p.OrcamentoCentavos, gasto, recebido,
            tarefas.Count(t => t.ConcluidaEm is not null), tarefas.Count,
            checklist.Count(i => i.Feito), checklist.Count,
            p.PessoaId is null, Proximo(hoje, tarefas, itens));
    }

    /// <summary>O que vem a seguir: o próximo marco, senão a tarefa com prazo mais perto, senão o primeiro item da lista.</summary>
    private static string? Proximo(DateOnly hoje, List<Tarefa> tarefas, List<ItemProjeto> itens)
    {
        var marco = itens.Where(i => i.Tipo == TipoItemProjeto.Marco && i.Data >= hoje).OrderBy(i => i.Data).ThenBy(i => i.Hora).FirstOrDefault();
        if (marco is not null) return $"{marco.Texto} · {Quando(marco.Data!.Value, hoje)}";

        var tarefa = tarefas.Where(t => t.ConcluidaEm is null).OrderBy(t => t.Prazo is null).ThenBy(t => t.Prazo).ThenBy(t => t.CriadoEm).FirstOrDefault();
        if (tarefa is not null) return tarefa.Prazo is { } d ? $"{tarefa.Titulo} · {(d < hoje ? "atrasada" : "até " + Quando(d, hoje))}" : tarefa.Titulo;

        return itens.Where(i => i.Tipo == TipoItemProjeto.Checklist && !i.Feito).OrderBy(i => i.Ordem).FirstOrDefault()?.Texto;
    }

    private static string Quando(DateOnly d, DateOnly hoje) =>
        d == hoje ? "hoje" : d == hoje.AddDays(1) ? "amanhã" : d.ToString("dd/MM");
}
