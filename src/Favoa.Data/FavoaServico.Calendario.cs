using System.Globalization;
using Favoa.Core;
using Favoa.Core.Captura;
using Favoa.Core.Edicao;
using Favoa.Core.Financas;
using Favoa.Core.Painel;
using Favoa.Core.Rotina;
using Microsoft.EntityFrameworkCore;

namespace Favoa.Data;

/// <summary>Calendário e histórico de progresso da tela inicial.</summary>
public sealed partial class FavoaServico
{
    public const string ChaveTelaInicial = "tela_inicial";
    public const string ChaveVisaoCalendario = "calendario_visao";

    private static readonly CultureInfo PtBr = new("pt-BR");

    public async Task<IReadOnlyList<ProgressoDia?>> ObterProgressoAsync(DateOnly inicio, DateOnly fim, DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var dados = await CarregarHistorico(db, inicio, fim, ct);
        return HistoricoColmeia.Calcular(dados, inicio, fim, hoje);
    }

    public async Task<UsoRecente> ObterUsoRecenteAsync(DateOnly hoje, int dias = 14, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var desde = hoje.AddDays(-(dias - 1));
        var minha = db.PessoaAtual;
        var despesas = await db.Lancamentos.AsNoTracking()
            .Where(l => l.Tipo == TipoLancamento.Despesa && l.Data >= desde && l.Data <= hoje && (minha == null || l.PessoaId == minha))
            .Select(l => l.Data).Distinct().CountAsync(ct);
        var agua = await db.RegistrosAgua.AsNoTracking().Where(a => a.Data >= desde && a.Data <= hoje).Select(a => a.Data).Distinct().CountAsync(ct);
        var treinos = await db.Sessoes.AsNoTracking().Where(s => s.Data >= desde && s.Data <= hoje).Select(s => s.Data).Distinct().CountAsync(ct);
        var pesos = await db.Pesos.AsNoTracking().Where(p => p.Data >= desde && p.Data <= hoje).Select(p => p.Data).Distinct().CountAsync(ct);
        return new UsoRecente(despesas, agua, treinos, pesos);
    }

    public async Task<ResumoSemana> ObterResumoSemanaAsync(DateOnly fim, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var dados = await CarregarHistorico(db, fim.AddDays(-13), fim, ct);
        return ResumoSemana.Calcular(dados, fim);
    }

    public async Task<CalendarioPeriodo> ObterCalendarioAsync(DateOnly inicio, DateOnly fim, DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var dados = await CarregarHistorico(db, inicio, fim, ct);
        var progresso = HistoricoColmeia.Calcular(dados, inicio, fim, hoje);

        var nomesPessoas = (await PessoasDaCasa(db, ct)).ToDictionary(p => p.Id, p => p.Nome);
        var tarefas = await db.Tarefas.AsNoTracking().Include(t => t.Responsaveis)
            .Where(t => (t.Prazo >= inicio && t.Prazo <= fim) || (t.Prazo == null && t.ConcluidaEm >= inicio && t.ConcluidaEm <= fim))
            .ToListAsync(ct);
        var lancamentos = await db.Lancamentos.AsNoTracking().Include(l => l.Categoria).Where(l => l.Data >= inicio && l.Data <= fim).ToListAsync(ct);
        var sessoes = await db.Sessoes.AsNoTracking().Include(s => s.Ficha).Include(s => s.Exercicios).ThenInclude(e => e.Exercicio)
            .Where(s => s.Data >= inicio && s.Data <= fim).ToListAsync(ct);
        var pesos = await db.Pesos.AsNoTracking().Where(p => p.Data >= inicio && p.Data <= fim).ToListAsync(ct);
        var habitos = await db.Habitos.AsNoTracking().Where(h => h.Ativo).OrderBy(h => h.Nome).ToListAsync(ct);
        var feitos = dados.RegistrosHabito.ToHashSet();
        var lembretes = fim >= hoje ? await db.Lembretes.AsNoTracking().Where(l => l.Ativo).OrderBy(l => l.Hora).ToListAsync(ct) : [];
        var recorrencias = fim >= hoje ? await db.Recorrencias.AsNoTracking().Where(r => r.Ativa).ToListAsync(ct) : [];
        var projetos = await db.Projetos.AsNoTracking().ToListAsync(ct);
        var idsProjetos = projetos.Select(p => p.Id).ToList();
        var marcos = await db.ItensProjeto.AsNoTracking()
            .Where(i => i.Tipo == Favoa.Core.Projetos.TipoItemProjeto.Marco && i.Data >= inicio && i.Data <= fim && idsProjetos.Contains(i.ProjetoId))
            .ToListAsync(ct);

        var dias = new List<DiaCalendario>();
        var i = 0;
        for (var d = inicio; d <= fim; d = d.AddDays(1), i++)
        {
            var itens = new List<ItemDia>();

            foreach (var t in tarefas.Where(t => (t.Prazo ?? t.ConcluidaEm) == d).OrderBy(t => t.ConcluidaEm is not null)
                         .ThenBy(t => t.Hora is null).ThenBy(t => t.Hora).ThenBy(t => t.CriadoEm))
            {
                var detalhe = string.Join(" · ", new[]
                {
                    t.Hora is { } h ? h.ToString("HH:mm") : null,
                    t.Responsaveis.Count > 0 ? string.Join(", ", t.Responsaveis.Select(r => nomesPessoas.GetValueOrDefault(r.PessoaId)).OfType<string>()) : null,
                    t.ConcluidaEm is { } c && c != d ? $"feita em {c:dd/MM}" : null,
                }.Where(x => !string.IsNullOrEmpty(x)));
                itens.Add(new ItemDia(TipoItemDia.Tarefa, t.Titulo, detalhe.Length == 0 ? null : detalhe,
                    t.ConcluidaEm is not null, t.EstaAtrasada(hoje), t.Id));
            }

            if (d >= hoje)
                foreach (var r in recorrencias)
                {
                    var data = CalendarioRecorrencia.DataNoMes(d.Year, d.Month, r.DiaDoMes);
                    if (data == d && d >= r.Inicio && (r.UltimaOcorrencia is null || d > r.UltimaOcorrencia))
                        itens.Add(new ItemDia(TipoItemDia.ContaPrevista, r.Descricao,
                            (r.Tipo == TipoLancamento.Receita ? "+ " : "") + Texto.FormatarMoeda(r.ValorCentavos), Id: r.Id));
                }

            foreach (var m in marcos.Where(m => m.Data == d).OrderBy(m => m.Hora))
            {
                var p = projetos.First(x => x.Id == m.ProjetoId);
                itens.Add(new ItemDia(TipoItemDia.Projeto, m.Texto, m.Hora is { } h ? $"{p.Nome} · {h.ToString("HH:mm")}" : p.Nome, Feito: d < hoje, Id: p.Id));
            }
            foreach (var p in projetos.Where(p => p.Inicio == d && p.Situacao != Favoa.Core.Projetos.SituacaoProjeto.Ideia))
                itens.Add(new ItemDia(TipoItemDia.Projeto, p.Tipo == Favoa.Core.Projetos.TipoProjeto.Viagem ? $"Ida: {p.Nome}" : $"Começa: {p.Nome}", null, d < hoje, Id: p.Id));
            foreach (var p in projetos.Where(p => p.Fim == d && p.Fim != p.Inicio && p.Situacao != Favoa.Core.Projetos.SituacaoProjeto.Ideia))
                itens.Add(new ItemDia(TipoItemDia.Projeto, p.Tipo switch
                {
                    Favoa.Core.Projetos.TipoProjeto.Viagem => $"Volta: {p.Nome}",
                    Favoa.Core.Projetos.TipoProjeto.Trabalho => $"Entrega: {p.Nome}",
                    _ => $"Termina: {p.Nome}",
                }, null, p.Situacao == Favoa.Core.Projetos.SituacaoProjeto.Concluido, Id: p.Id));

            if (d >= hoje)
                foreach (var l in lembretes.Where(l => l.Dias.HasFlag(AgendaLembretes.Dia(d.DayOfWeek))))
                    itens.Add(new ItemDia(TipoItemDia.Lembrete, l.Titulo, l.Hora.ToString("HH:mm"), Id: l.Id));

            foreach (var s in sessoes.Where(s => s.Data == d))
            {
                var nomes = s.Exercicios.OrderBy(e => e.Ordem).Select(e => e.Exercicio?.Nome).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
                itens.Add(new ItemDia(TipoItemDia.Treino, s.Ficha?.Nome is { Length: > 0 } f ? $"Treino {f}" : "Treino",
                    nomes.Count == 0 ? null : string.Join(", ", nomes.Take(4)) + (nomes.Count > 4 ? "…" : ""), true, Id: s.Id));
            }

            foreach (var p in pesos.Where(p => p.Data == d))
                itens.Add(new ItemDia(TipoItemDia.Peso, $"Peso {p.Kg.ToString("0.#", PtBr)} kg", Feito: true, Id: p.Id));

            foreach (var l in lancamentos.Where(l => l.Data == d).OrderBy(l => l.CriadoEm))
                itens.Add(new ItemDia(TipoItemDia.Lancamento, string.IsNullOrWhiteSpace(l.Descricao) ? l.Categoria?.Nome ?? (l.Tipo == TipoLancamento.Receita ? "Receita" : "Despesa") : l.Descricao,
                    (l.Tipo == TipoLancamento.Receita ? "+ " : "") + Texto.FormatarMoeda(l.ValorCentavos), true, Id: l.Id));

            if (d <= hoje)
                foreach (var h in habitos.Where(h => DataLocal(h.CriadoEm) <= d))
                    itens.Add(new ItemDia(TipoItemDia.Habito, h.Nome, null, feitos.Contains((h.Id, d)), Id: h.Id));

            var doDia = lancamentos.Where(l => l.Data == d).ToList();
            dias.Add(new DiaCalendario(d, progresso[i],
                doDia.Where(l => l.Tipo == TipoLancamento.Despesa).Sum(l => l.ValorCentavos),
                doDia.Where(l => l.Tipo == TipoLancamento.Receita).Sum(l => l.ValorCentavos),
                dados.AguaPorDia.TryGetValue(d, out var agua) ? agua : 0,
                dados.KcalPorDia.TryGetValue(d, out var kcal) ? kcal : 0,
                itens));
        }

        return new CalendarioPeriodo(hoje, dias);
    }

    public async Task<ResultadoRegistro> CriarTarefaAsync(string titulo, DateOnly? prazo, CancellationToken ct = default)
    {
        titulo = titulo.Trim();
        if (titulo.Length == 0) return new(false, "Escreva o que precisa ser feito.");
        if (titulo.Length > 200) return new(false, "Título muito longo.");

        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var tarefa = new Tarefa { Titulo = Texto.Capitalizar(titulo), Prazo = prazo };
        db.Tarefas.Add(tarefa);
        await db.SaveChangesAsync(ct);
        return new(true, prazo is { } p ? $"Tarefa para {p:dd/MM}: {tarefa.Titulo}" : $"Tarefa: {tarefa.Titulo}",
            new ReferenciaRegistro(TipoReferencia.Tarefa, tarefa.Id));
    }

    public async Task<PreferenciasInicio> ObterPreferenciasInicioAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var chaveTela = AbrirBanco.ChaveDaPessoa(ChaveTelaInicial, db.PessoaAtual);
        var chaveVisao = AbrirBanco.ChaveDaPessoa(ChaveVisaoCalendario, db.PessoaAtual);
        var valores = await db.Configuracoes.AsNoTracking().Where(c => c.Chave == chaveTela || c.Chave == chaveVisao).ToDictionaryAsync(c => c.Chave, c => c.Valor, ct);

        var padrao = PreferenciasInicio.Padrao;
        return new PreferenciasInicio(
            valores.TryGetValue(chaveTela, out var t) && Enum.TryParse<TelaInicial>(t, out var tela) ? tela : padrao.Tela,
            valores.TryGetValue(chaveVisao, out var v) && Enum.TryParse<VisaoCalendario>(v, out var visao) ? visao : padrao.Visao);
    }

    public async Task SalvarPreferenciasInicioAsync(PreferenciasInicio preferencias, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        await Gravar(db, AbrirBanco.ChaveDaPessoa(ChaveTelaInicial, db.PessoaAtual), preferencias.Tela.ToString(), ct);
        await Gravar(db, AbrirBanco.ChaveDaPessoa(ChaveVisaoCalendario, db.PessoaAtual), preferencias.Visao.ToString(), ct);
        await db.SaveChangesAsync(ct);

        static async Task Gravar(FavoaDbContext db, string chave, string valor, CancellationToken ct)
        {
            var cfg = await db.Configuracoes.FindAsync([chave], ct);
            if (cfg is null) db.Configuracoes.Add(new Configuracao { Chave = chave, Valor = valor });
            else cfg.Valor = valor;
        }
    }

    private static DateOnly DataLocal(DateTime utc) =>
        DateOnly.FromDateTime(utc.Kind == DateTimeKind.Local ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime());

    /// <summary>Lê só o necessário para refazer a colmeia de <paramref name="inicio"/> a <paramref name="fim"/>.</summary>
    private async Task<DadosHistorico> CarregarHistorico(FavoaDbContext db, DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        var inicioMes = new DateOnly(inicio.Year, inicio.Month, 1);
        var despesas = await db.Lancamentos.AsNoTracking()
            .Where(l => l.Tipo == TipoLancamento.Despesa && l.Data >= inicioMes && l.Data <= fim)
            .Select(l => new { l.Data, l.ValorCentavos }).ToListAsync(ct);

        var tarefas = await db.Tarefas.AsNoTracking()
            .Where(t => t.ConcluidaEm == null || t.ConcluidaEm >= inicio)
            .Select(t => new { t.CriadoEm, t.Prazo, t.ConcluidaEm }).ToListAsync(ct);

        var habitos = await db.Habitos.AsNoTracking().Where(h => h.Ativo).Select(h => new { h.Id, h.CriadoEm }).ToListAsync(ct);
        var registros = await db.RegistrosHabito.AsNoTracking().Where(r => r.Data >= inicio && r.Data <= fim)
            .Select(r => new { r.HabitoId, r.Data }).ToListAsync(ct);

        var agua = await db.RegistrosAgua.AsNoTracking().Where(a => a.Data >= inicio && a.Data <= fim)
            .GroupBy(a => a.Data).Select(g => new { Data = g.Key, Ml = g.Sum(a => a.Mililitros) }).ToListAsync(ct);

        var consumos = await db.Consumos.AsNoTracking().Include(c => c.Alimento).Where(c => c.Data >= inicio && c.Data <= fim).ToListAsync(ct);
        var kcal = consumos.GroupBy(c => c.Data).ToDictionary(g => g.Key,
            g => (int)Math.Round(g.Sum(c => Favoa.Core.Fitness.Macros.De(c.Alimento!, c.Gramas).Kcal), MidpointRounding.AwayFromZero));

        var desdeTreino = inicio.AddDays(-7);
        var treinos = await db.Sessoes.AsNoTracking().Where(s => s.Data >= desdeTreino && s.Data <= fim).Select(s => s.Data).ToListAsync(ct);
        var primeiroTreino = await db.Sessoes.MinAsync(s => (DateOnly?)s.Data, ct);
        if (primeiroTreino is { } pt && pt < desdeTreino) treinos.Add(pt); // só para saber desde quando há treinos

        var metas = await FitnessServico.LerMetas(db, ct);

        return new DadosHistorico
        {
            OrcamentoMensalCentavos = await LerConfigLong(db, Configuracao.OrcamentoMensalCentavos, 0, ct),
            Despesas = despesas.Select(x => (x.Data, x.ValorCentavos)).ToList(),
            Tarefas = tarefas.Select(t => (DataLocal(t.CriadoEm), t.Prazo, t.ConcluidaEm)).ToList(),
            Habitos = habitos.Select(h => (h.Id, DataLocal(h.CriadoEm))).ToList(),
            RegistrosHabito = registros.Select(r => (r.HabitoId, r.Data)).ToList(),
            AguaPorDia = agua.ToDictionary(a => a.Data, a => a.Ml),
            KcalPorDia = kcal,
            Treinos = treinos,
            MetaAguaMl = (int)await LerConfigLong(db, AbrirBanco.ChaveDaPessoa(Configuracao.MetaAguaMl, db.PessoaAtual), MetaAguaPadraoMl, ct),
            MetaKcal = metas.Kcal,
            GastoTreinoPorDia = await FitnessServico.GastoPorDiaAsync(db, inicio, fim, ct),
            CompensacaoTreinoPct = metas.CompensacaoTreinoPct,
            MetaTreinosSemana = metas.TreinosSemana,
            InicioDeUso = await InicioDeUso(db, ct),
        };
    }

    /// <summary>Primeiro dia com algum registro da casa ou da pessoa.</summary>
    private static async Task<DateOnly?> InicioDeUso(FavoaDbContext db, CancellationToken ct)
    {
        var datas = new List<DateOnly?>
        {
            await db.Lancamentos.MinAsync(l => (DateOnly?)l.Data, ct),
            await db.RegistrosAgua.MinAsync(a => (DateOnly?)a.Data, ct),
            await db.Consumos.MinAsync(c => (DateOnly?)c.Data, ct),
            await db.Sessoes.MinAsync(s => (DateOnly?)s.Data, ct),
            await db.Pesos.MinAsync(p => (DateOnly?)p.Data, ct),
            await db.RegistrosHabito.MinAsync(r => (DateOnly?)r.Data, ct),
        };
        var tarefa = await db.Tarefas.MinAsync(t => (DateTime?)t.CriadoEm, ct);
        if (tarefa is { } t) datas.Add(DataLocal(t));
        var habito = await db.Habitos.MinAsync(h => (DateTime?)h.CriadoEm, ct);
        if (habito is { } h) datas.Add(DataLocal(h));
        return datas.Where(d => d is not null).Min();
    }
}
