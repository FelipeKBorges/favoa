using Favoa.Core;
using Favoa.Core.Captura;
using Favoa.Core.Edicao;
using Favoa.Core.Financas;
using Favoa.Core.Fitness;
using Favoa.Core.Painel;
using Favoa.Core.Rotina;
using Microsoft.EntityFrameworkCore;

namespace Favoa.Data;

public sealed partial class FavoaServico(IDbContextFactory<FavoaDbContext> fabrica, Favoa.Core.Pessoas.IPessoaAtual pessoa) : IFavoaServico
{
    private const int MetaAguaPadraoMl = 2500;

    public async Task<ContextoCaptura> ObterContextoAsync(DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var (despesa, receita) = await CategoriasPorUso(db, ct);
        return new ContextoCaptura(
            hoje,
            await db.Categorias.Select(c => c.Nome).Distinct().ToListAsync(ct),
            await db.Contas.Where(c => c.Ativa).Select(c => c.Nome).ToListAsync(ct),
            await db.Habitos.Where(h => h.Ativo).Select(h => h.Nome).ToListAsync(ct))
        {
            CategoriasDespesa = despesa,
            CategoriasReceita = receita,
            Projetos = await db.Projetos.OrderBy(p => p.Nome).Select(p => p.Nome).ToListAsync(ct),
            Pessoas = await PessoasDaCasa(db, ct),
        };
    }

    /// <summary>Pessoas da casa na ordem das telas (para escolher responsáveis).</summary>
    internal static async Task<List<Favoa.Core.Pessoas.PessoaOpcao>> PessoasDaCasa(FavoaDbContext db, CancellationToken ct) =>
        await db.Pessoas.AsNoTracking().OrderBy(p => p.Ordem).ThenBy(p => p.CriadoEm)
            .Select(p => new Favoa.Core.Pessoas.PessoaOpcao(p.Id, p.Nome, p.Cor)).ToListAsync(ct);

    /// <summary>Confere horário, aviso e responsáveis de uma tarefa. Devolve o erro ou nulo.</summary>
    private static async Task<string?> ValidarDetalhesTarefa(FavoaDbContext db, DateOnly? prazo, TimeOnly? hora, int? aviso, IReadOnlyList<Guid> responsaveis, CancellationToken ct)
    {
        if (AvisoTarefa.Validar(prazo, hora, aviso) is { } erro) return erro;
        if (responsaveis.Count == 0) return null;
        var ids = responsaveis.Distinct().ToList();
        var existem = await db.Pessoas.CountAsync(p => ids.Contains(p.Id), ct);
        return existem == ids.Count ? null : "Uma das pessoas escolhidas não existe mais.";
    }

    /// <summary>Troca os responsáveis da tarefa (vazio = a casa).</summary>
    private static void DefinirResponsaveis(Tarefa t, IReadOnlyList<Guid> responsaveis)
    {
        var novos = responsaveis.Distinct().ToList();
        t.Responsaveis.RemoveAll(r => !novos.Contains(r.PessoaId));
        foreach (var id in novos.Where(id => t.Responsaveis.All(r => r.PessoaId != id)))
            t.Responsaveis.Add(new TarefaResponsavel { TarefaId = t.Id, PessoaId = id });
    }

    public async Task<ResultadoRegistro> RegistrarAsync(Interpretacao i, CancellationToken ct = default)
    {
        if (!i.PodeSalvar)
            return new(false, i.Aviso ?? "Ainda não sei registrar isso.");

        await using var db = await fabrica.AbrirAsync(pessoa, ct);

        // Projeto: só tarefas e lançamentos entram em projeto.
        Favoa.Core.Projetos.Projeto? projeto = null;
        if (i.Tipo is TipoCaptura.Tarefa or TipoCaptura.Despesa or TipoCaptura.Receita && (i.ProjetoId is not null || i.Projeto is not null))
        {
            projeto = i.ProjetoId is { } pid
                ? await db.Projetos.FirstOrDefaultAsync(p => p.Id == pid, ct)
                : await ProjetosServico.AcharPorApelido(db, i.Projeto, ct);
            if (projeto is null) return new(false, $"Não achei o projeto {(i.Projeto is null ? "escolhido" : "#" + i.Projeto)}. Veja os nomes em Projetos.");
        }

        if (i.Tipo == TipoCaptura.Tarefa && await ValidarDetalhesTarefa(db, i.Prazo, i.Hora, i.AvisoMinutos, i.Responsaveis, ct) is { } erroTarefa)
            return new(false, erroTarefa);

        var resultado = i.Tipo switch
        {
            TipoCaptura.Despesa or TipoCaptura.Receita => await RegistrarLancamento(db, i, ct),
            TipoCaptura.Agua => RegistrarAgua(db, i),
            TipoCaptura.Habito => await MarcarHabito(db, i, ct),
            TipoCaptura.Tarefa => RegistrarTarefa(db, i),
            TipoCaptura.NovoHabito => await CriarHabito(db, i, ct),
            TipoCaptura.NovaConta => await CriarConta(db, i, ct),
            TipoCaptura.Orcamento => await DefinirOrcamento(db, i, ct),
            TipoCaptura.Treino or TipoCaptura.Cardio => await RegistrarExercicio(db, i, ct),
            TipoCaptura.Peso => await FitnessServico.RegistrarPeso(db, i.Data, i.PesoKg!.Value, ct),
            TipoCaptura.Refeicao => await RegistrarRefeicao(db, i, ct),
            _ => new ResultadoRegistro(false, "Ainda não sei registrar isso."),
        };

        if (resultado.Sucesso && projeto is not null)
        {
            foreach (var t in db.ChangeTracker.Entries<Tarefa>().Where(e => e.State == EntityState.Added)) t.Entity.ProjetoId = projeto.Id;
            foreach (var l in db.ChangeTracker.Entries<Lancamento>().Where(e => e.State == EntityState.Added)) l.Entity.ProjetoId = projeto.Id;
            resultado = resultado with { Mensagem = $"{resultado.Mensagem} · {projeto.Nome}" };
        }

        if (resultado.Sucesso) await db.SaveChangesAsync(ct);
        return resultado;
    }

    public async Task<PainelDoDia> ObterPainelAsync(DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);

        var inicioMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var fimMes = inicioMes.AddMonths(1);

        var despesasMes = await db.Lancamentos
            .Where(l => l.Tipo == TipoLancamento.Despesa && l.Data >= inicioMes && l.Data < fimMes)
            .SumAsync(l => l.ValorCentavos, ct);
        var receitasMes = await db.Lancamentos
            .Where(l => l.Tipo == TipoLancamento.Receita && l.Data >= inicioMes && l.Data < fimMes)
            .SumAsync(l => l.ValorCentavos, ct);

        var pendentes = await db.Tarefas.Where(t => t.ConcluidaEm == null).ToListAsync(ct);
        var concluidasHoje = await db.Tarefas.CountAsync(t => t.ConcluidaEm == hoje, ct);

        var habitos = await db.Habitos.Where(h => h.Ativo).OrderBy(h => h.Nome).ToListAsync(ct);
        var desde = hoje.AddDays(-366);
        var registros = await db.RegistrosHabito.Where(r => r.Data >= desde).ToListAsync(ct);
        var feitosHoje = registros.Where(r => r.Data == hoje).Select(r => r.HabitoId).ToHashSet();

        var aguaHoje = await db.RegistrosAgua.Where(a => a.Data == hoje).SumAsync(a => (int?)a.Mililitros, ct) ?? 0;

        var consumosHoje = await db.Consumos.AsNoTracking().Include(c => c.Alimento).Where(c => c.Data == hoje).ToListAsync(ct);
        var kcalHoje = consumosHoje.Sum(c => Macros.De(c.Alimento!, c.Gramas).Kcal);
        var metasFitness = await FitnessServico.LerMetas(db, ct);
        var datasTreino = await db.Sessoes.Where(x => x.Data > hoje.AddDays(-7) && x.Data <= hoje).Select(x => x.Data).ToListAsync(ct);

        var resumo = new ResumoDoDia
        {
            OrcamentoMensalCentavos = await LerConfigLong(db, ChaveOrcamento(db), 0, ct),
            DespesasDoMesCentavos = despesasMes,
            ReceitasDoMesCentavos = receitasMes,
            TarefasConcluidasHoje = concluidasHoje,
            TarefasParaHoje = pendentes.Count(t => t.Prazo == hoje),
            TarefasAtrasadas = pendentes.Count(t => t.EstaAtrasada(hoje)),
            SemTarefasCadastradas = !await db.Tarefas.AnyAsync(ct),
            HabitosAtivos = habitos.Count,
            HabitosFeitosHoje = habitos.Count(h => feitosHoje.Contains(h.Id)),
            MelhorSequenciaDias = habitos.Select(h => Sequencia(h.Id, registros, hoje)).DefaultIfEmpty(0).Max(),
            AguaHojeMl = aguaHoje,
            MetaAguaMl = (int)await LerConfigLong(db, AbrirBanco.ChaveDaPessoa(Configuracao.MetaAguaMl, db.PessoaAtual), MetaAguaPadraoMl, ct),
            KcalHoje = (int)Math.Round(kcalHoje, MidpointRounding.AwayFromZero),
            MetaKcal = metasFitness.MetaKcalDoDia((await FitnessServico.GastoPorDiaAsync(db, hoje, hoje, ct)).GetValueOrDefault(hoje)),
            TemConsumoHoje = consumosHoje.Count > 0,
            TemTreinos = await db.Sessoes.AnyAsync(ct),
            TreinouHoje = datasTreino.Contains(hoje),
            TreinosUltimos7Dias = EvolucaoTreino.DiasTreinadosUltimos7(datasTreino, hoje),
            MetaTreinosSemana = metasFitness.TreinosSemana,
        };
        resumo = await ComProjetos(db, resumo, hoje, ct);

        var proximas = pendentes
            .OrderBy(t => t.Prazo is null)
            .ThenBy(t => t.Prazo)
            .ThenBy(t => t.CriadoEm)
            .Take(5)
            .Select(t => new TarefaResumo(t.Id, t.Titulo, t.Prazo, t.EstaAtrasada(hoje)))
            .ToList();

        var habitosPendentes = habitos
            .Where(h => !feitosHoje.Contains(h.Id))
            .Select(h => new HabitoResumo(h.Id, h.Nome))
            .ToList();

        return new PainelDoDia(
            hoje,
            resumo,
            CalculadoraColmeia.Calcular(resumo),
            proximas,
            habitosPendentes,
            await Recentes(db, hoje, habitos, ct));
    }

    /// <summary>Projetos não concluídos (e o atual, mesmo concluído) para a lista da edição.</summary>
    private static async Task<IReadOnlyList<ProjetoOpcao>> ProjetosParaEscolher(FavoaDbContext db, Guid? atual, CancellationToken ct) =>
        await db.Projetos.AsNoTracking()
            .Where(p => p.Situacao != Favoa.Core.Projetos.SituacaoProjeto.Concluido || p.Id == atual)
            .OrderBy(p => p.Nome).Select(p => new ProjetoOpcao(p.Id, p.Nome)).ToListAsync(ct);

    private static async Task<Guid?> ProjetoValido(FavoaDbContext db, Guid? id, CancellationToken ct) =>
        id is { } p && await db.Projetos.AnyAsync(x => x.Id == p, ct) ? p : null;

    /// <summary>Andamento dos projetos ativos para a célula Projetos da colmeia.</summary>
    private static async Task<ResumoDoDia> ComProjetos(FavoaDbContext db, ResumoDoDia resumo, DateOnly hoje, CancellationToken ct)
    {
        var ativos = (await db.Projetos.AsNoTracking().ToListAsync(ct)).Where(p => Favoa.Core.Projetos.TiposProjeto.Ativo(p.Situacao)).ToList();
        if (ativos.Count == 0) return resumo;
        var ids = ativos.Select(p => p.Id).ToList();
        var tarefas = await db.Tarefas.AsNoTracking().Where(t => t.ProjetoId != null && ids.Contains(t.ProjetoId.Value)).ToListAsync(ct);
        var itens = await db.ItensProjeto.AsNoTracking().Where(i => ids.Contains(i.ProjetoId)).ToListAsync(ct);
        var resumos = ativos.Select(p => ProjetosServico.Resumir(p, hoje, tarefas.Where(t => t.ProjetoId == p.Id).ToList(), 0, 0,
            itens.Where(i => i.ProjetoId == p.Id).ToList())).ToList();
        var proximo = resumos.Where(r => r.DiasAte(hoje) is not null).OrderBy(r => r.DiasAte(hoje)).FirstOrDefault();
        return resumo with
        {
            ProjetosAtivos = resumos.Count,
            ProgressoProjetos = resumos.Average(r => r.Progresso),
            ProjetoMaisProximo = proximo?.Nome ?? (resumos.Count == 1 ? resumos[0].Nome : null),
            DiasProjetoMaisProximo = proximo?.DiasAte(hoje),
        };
    }

    public async Task AlternarTarefaAsync(Guid tarefaId, DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var tarefa = await db.Tarefas.FindAsync([tarefaId], ct);
        if (tarefa is null) return;

        tarefa.ConcluidaEm = tarefa.ConcluidaEm is null ? hoje : null;
        await db.SaveChangesAsync(ct);
    }

    public async Task AlternarHabitoAsync(Guid habitoId, DateOnly dia, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var existente = await db.RegistrosHabito.FirstOrDefaultAsync(r => r.HabitoId == habitoId && r.Data == dia, ct);

        if (existente is null)
            db.RegistrosHabito.Add(new RegistroHabito { HabitoId = habitoId, Data = dia });
        else
            db.RegistrosHabito.Remove(existente);

        await db.SaveChangesAsync(ct);
    }

    // ---------- Desfazer, editar, excluir ----------

    public async Task<ResultadoRegistro> DesfazerAsync(ReferenciaRegistro r, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);

        switch (r.Tipo)
        {
            case TipoReferencia.Lancamento:
                var lancamento = await db.Lancamentos.FindAsync([r.Id], ct);
                if (lancamento is null) return NaoEncontrado();
                db.Lancamentos.Remove(lancamento);
                await db.SaveChangesAsync(ct);
                await RemoverCriadosJuntoSemUsoAsync(db, r.CriadosJunto, ct);
                return new(true, "Lançamento desfeito.");

            case TipoReferencia.Habito:
                var habito = await db.Habitos.FindAsync([r.Id], ct);
                if (habito is null) return NaoEncontrado();
                db.Habitos.Remove(habito);
                break;

            case TipoReferencia.Conta:
                if (await db.Lancamentos.AnyAsync(l => l.ContaId == r.Id, ct))
                    return new(false, "Essa conta já tem lançamentos; não dá para desfazer.");
                var conta = await db.Contas.FindAsync([r.Id], ct);
                if (conta is null) return NaoEncontrado();
                db.Contas.Remove(conta);
                break;

            case TipoReferencia.ExercicioRealizado:
                var feito = await db.ExerciciosRealizados.FindAsync([r.Id], ct);
                if (feito is null) return NaoEncontrado();
                db.ExerciciosRealizados.Remove(feito);
                await db.SaveChangesAsync(ct);
                await RemoverCriadosJuntoSemUsoAsync(db, r.CriadosJunto, ct);
                return new(true, "Exercício desfeito.");

            case TipoReferencia.Peso:
                var peso = await db.Pesos.FindAsync([r.Id], ct);
                if (peso is null) return NaoEncontrado();
                if (string.IsNullOrEmpty(r.ValorAnterior)) db.Pesos.Remove(peso);
                else peso.Kg = decimal.Parse(r.ValorAnterior, System.Globalization.CultureInfo.InvariantCulture);
                break;

            case TipoReferencia.Orcamento:
                var cfg = await db.Configuracoes.FindAsync([ChaveOrcamento(db)], ct);
                if (cfg is null) return NaoEncontrado();
                if (string.IsNullOrEmpty(r.ValorAnterior)) db.Configuracoes.Remove(cfg);
                else cfg.Valor = r.ValorAnterior;
                break;

            default:
                var excluido = await ExcluirAsync(r, ct);
                return excluido.Sucesso ? new(true, "Desfeito.") : excluido;
        }

        await db.SaveChangesAsync(ct);
        return new(true, "Desfeito.");
    }

    public async Task<RegistroEditavel?> ObterParaEdicaoAsync(ReferenciaRegistro r, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);

        switch (r.Tipo)
        {
            case TipoReferencia.Lancamento:
                var l = await db.Lancamentos.Include(x => x.Categoria).Include(x => x.Conta)
                    .FirstOrDefaultAsync(x => x.Id == r.Id, ct);
                if (l is null) return null;
                return new RegistroEditavel(r)
                {
                    TipoLancamento = l.Tipo,
                    ValorCentavos = l.ValorCentavos,
                    Categoria = l.Categoria?.Nome ?? "",
                    Conta = l.Conta?.Nome ?? "",
                    Descricao = l.Descricao,
                    Data = l.Data,
                    CategoriasDisponiveis = await db.Categorias.Where(c => c.Tipo == l.Tipo)
                        .OrderBy(c => c.Nome).Select(c => c.Nome).ToListAsync(ct),
                    ProjetoId = l.ProjetoId,
                    ProjetosDisponiveis = await ProjetosParaEscolher(db, l.ProjetoId, ct),
                    CategoriasDespesa = (await CategoriasPorUso(db, ct)).Despesa,
                    CategoriasReceita = (await CategoriasPorUso(db, ct)).Receita,
                    ContasDisponiveis = await db.Contas.Where(c => c.Ativa)
                        .OrderBy(c => c.Nome).Select(c => c.Nome).ToListAsync(ct),
                };

            case TipoReferencia.Agua:
                var a = await db.RegistrosAgua.FindAsync([r.Id], ct);
                return a is null ? null : new RegistroEditavel(r) { Mililitros = a.Mililitros, Data = a.Data };

            case TipoReferencia.Tarefa:
                var t = await db.Tarefas.Include(x => x.Responsaveis).FirstOrDefaultAsync(x => x.Id == r.Id, ct);
                return t is null ? null : new RegistroEditavel(r)
                {
                    Titulo = t.Titulo,
                    Prazo = t.Prazo,
                    Hora = t.Hora,
                    AvisoMinutos = t.AvisoMinutos,
                    Responsaveis = t.Responsaveis.Select(x => x.PessoaId).ToList(),
                    PessoasDisponiveis = await PessoasDaCasa(db, ct),
                    Concluida = t.ConcluidaEm is not null,
                    ProjetoId = t.ProjetoId,
                    ProjetosDisponiveis = await ProjetosParaEscolher(db, t.ProjetoId, ct),
                };

            case TipoReferencia.RegistroHabito:
                var h = await db.RegistrosHabito.Include(x => x.Habito).FirstOrDefaultAsync(x => x.Id == r.Id, ct);
                return h is null ? null : new RegistroEditavel(r) { NomeHabito = h.Habito?.Nome ?? "", Data = h.Data };

            // Fitness: o modal mostra o resumo e permite excluir; o ajuste fino é nas telas de Treino e Dieta.
            case TipoReferencia.ExercicioRealizado:
                var e = await db.ExerciciosRealizados.Include(x => x.Exercicio).Include(x => x.Sessao).FirstOrDefaultAsync(x => x.Id == r.Id, ct);
                return e is null ? null : new RegistroEditavel(r) { Descricao = $"{e.Exercicio?.Nome} · {FitnessServico.Descrever(e)}", Data = e.Sessao!.Data };

            case TipoReferencia.Consumo:
                var c = await db.Consumos.Include(x => x.Alimento).FirstOrDefaultAsync(x => x.Id == r.Id, ct);
                return c is null ? null : new RegistroEditavel(r)
                {
                    Descricao = $"{Refeicoes.Nome(c.Refeicao)}: {c.Gramas} g de {c.Alimento?.Nome} · {Macros.De(c.Alimento!, c.Gramas).Kcal:0} kcal",
                    Data = c.Data,
                };

            case TipoReferencia.Peso:
                var p = await db.Pesos.FindAsync([r.Id], ct);
                return p is null ? null : new RegistroEditavel(r) { Descricao = $"{p.Kg:0.0} kg".Replace('.', ','), Data = p.Data };

            default:
                return null;
        }
    }

    public async Task<ResultadoRegistro> SalvarEdicaoAsync(RegistroEditavel e, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);

        switch (e.Referencia.Tipo)
        {
            case TipoReferencia.Lancamento:
                if (e.ValorCentavos <= 0) return new(false, "O valor precisa ser maior que zero.");
                var l = await db.Lancamentos.FindAsync([e.Referencia.Id], ct);
                if (l is null) return NaoEncontrado();
                l.Tipo = e.TipoLancamento;
                l.ValorCentavos = e.ValorCentavos;
                l.Data = e.Data;
                var catEditada = await AcharPorNome(db.Categorias.Where(c => c.Tipo == e.TipoLancamento), c => c.Nome, e.Categoria, ct);
                if (catEditada is null) return new(false, await TextoEscolhaCategoria(db, e.TipoLancamento, e.Categoria, ct));
                l.Descricao = e.Descricao.Trim();
                l.ContaId = (await ObterOuCriarConta(db, e.Conta, ct)).Id;
                l.CategoriaId = catEditada.Id;
                l.ProjetoId = await ProjetoValido(db, e.ProjetoId, ct);
                await db.SaveChangesAsync(ct);
                return new(true, "Lançamento atualizado.");

            case TipoReferencia.Agua:
                if (e.Mililitros <= 0) return new(false, "A quantidade precisa ser maior que zero.");
                var a = await db.RegistrosAgua.FindAsync([e.Referencia.Id], ct);
                if (a is null) return NaoEncontrado();
                a.Mililitros = e.Mililitros;
                a.Data = e.Data;
                break;

            case TipoReferencia.Tarefa:
                if (string.IsNullOrWhiteSpace(e.Titulo)) return new(false, "A tarefa precisa de um título.");
                var t = await db.Tarefas.Include(x => x.Responsaveis).FirstOrDefaultAsync(x => x.Id == e.Referencia.Id, ct);
                if (t is null) return NaoEncontrado();
                if (await ValidarDetalhesTarefa(db, e.Prazo, e.Hora, e.AvisoMinutos, e.Responsaveis, ct) is { } erroTarefa)
                    return new(false, erroTarefa);
                var hora = e.Hora is { } h ? new TimeOnly(h.Hour, h.Minute) : (TimeOnly?)null;
                if (t.Prazo != e.Prazo || t.Hora != hora || t.AvisoMinutos != e.AvisoMinutos) t.AvisoEnviadoEm = null;
                t.Titulo = e.Titulo.Trim();
                t.Prazo = e.Prazo;
                t.Hora = hora;
                t.AvisoMinutos = e.AvisoMinutos;
                DefinirResponsaveis(t, e.Responsaveis);
                t.ProjetoId = await ProjetoValido(db, e.ProjetoId, ct);
                if (e.Concluida && t.ConcluidaEm is null) t.ConcluidaEm = DateOnly.FromDateTime(DateTime.Now);
                if (!e.Concluida) t.ConcluidaEm = null;
                break;

            default:
                return new(false, "Esse registro não tem campos para editar.");
        }

        await db.SaveChangesAsync(ct);
        return new(true, "Alterações salvas.");
    }

    public async Task<ResultadoRegistro> ExcluirAsync(ReferenciaRegistro r, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);

        object? alvo = r.Tipo switch
        {
            TipoReferencia.Lancamento => await db.Lancamentos.FindAsync([r.Id], ct),
            TipoReferencia.Agua => await db.RegistrosAgua.FindAsync([r.Id], ct),
            TipoReferencia.RegistroHabito => await db.RegistrosHabito.FindAsync([r.Id], ct),
            TipoReferencia.Tarefa => await db.Tarefas.FindAsync([r.Id], ct),
            TipoReferencia.Habito => await db.Habitos.FindAsync([r.Id], ct), // os registros vão junto (cascata)
            TipoReferencia.Consumo => await db.Consumos.FindAsync([r.Id], ct),
            TipoReferencia.ExercicioRealizado => await db.ExerciciosRealizados.FindAsync([r.Id], ct),
            TipoReferencia.SessaoTreino => await db.Sessoes.FindAsync([r.Id], ct),
            TipoReferencia.Peso => await db.Pesos.FindAsync([r.Id], ct),
            _ => null,
        };
        if (alvo is null) return NaoEncontrado();

        db.Remove(alvo);
        await db.SaveChangesAsync(ct);

        return new(true, "Registro excluído.");
    }

    /// <summary>
    /// Remove contas e categorias que a captura criou no mesmo passo do registro desfeito,
    /// desde que tenham ficado sem nenhum lançamento.
    /// </summary>
    private static async Task RemoverCriadosJuntoSemUsoAsync(
        FavoaDbContext db, IReadOnlyList<ReferenciaRegistro>? criados, CancellationToken ct)
    {
        if (criados is null or { Count: 0 }) return;

        // Recorrência criada junto ("repetir todo mês") sai primeiro: ela segura conta e categoria.
        foreach (var c in criados.Where(x => x.Tipo == TipoReferencia.Recorrencia))
            if (await db.Recorrencias.FindAsync([c.Id], ct) is { } recorrencia)
            {
                await db.Lancamentos.Where(l => l.RecorrenciaId == c.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.RecorrenciaId, (Guid?)null), ct);
                db.Recorrencias.Remove(recorrencia);
            }
        await db.SaveChangesAsync(ct);

        foreach (var c in criados)
        {
            if (c.Tipo == TipoReferencia.Conta
                && !await db.Lancamentos.AnyAsync(x => x.ContaId == c.Id, ct)
                && !await db.Recorrencias.AnyAsync(x => x.ContaId == c.Id, ct)
                && await db.Contas.FindAsync([c.Id], ct) is { } conta)
                db.Contas.Remove(conta);

            if (c.Tipo == TipoReferencia.Categoria
                && !await db.Lancamentos.AnyAsync(x => x.CategoriaId == c.Id, ct)
                && !await db.Recorrencias.AnyAsync(x => x.CategoriaId == c.Id, ct)
                && await db.Categorias.FindAsync([c.Id], ct) is { } categoria)
                db.Categorias.Remove(categoria);

            if (c.Tipo == TipoReferencia.SessaoTreino
                && !await db.ExerciciosRealizados.AnyAsync(x => x.SessaoId == c.Id, ct)
                && await db.Sessoes.FindAsync([c.Id], ct) is { } sessao)
                db.Sessoes.Remove(sessao);
        }
        await db.SaveChangesAsync(ct);

        // Exercício por último: só depois de a sessão vazia sair.
        foreach (var c in criados.Where(x => x.Tipo == TipoReferencia.Exercicio))
        {
            if (!await db.ExerciciosRealizados.AnyAsync(x => x.ExercicioId == c.Id, ct)
                && !await db.ItensFicha.AnyAsync(x => x.ExercicioId == c.Id, ct)
                && await db.Exercicios.FindAsync([c.Id], ct) is { } exercicio)
                db.Exercicios.Remove(exercicio);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<ExtratoMes> ObterExtratoAsync(FiltroExtrato f, DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);

        var doMes = await db.Lancamentos.AsNoTracking()
            .Include(l => l.Categoria)
            .Include(l => l.Conta)
            .Where(l => l.Data >= f.Inicio && l.Data < f.Fim)
            .ToListAsync(ct);

        // Tipo, conta e busca valem para tudo; a categoria não filtra as fatias,
        // assim dá para trocar de categoria clicando nelas.
        var busca = Texto.Normalizar(f.Busca);
        var semCategoria = doMes.Where(l =>
            (f.Tipo is null || l.Tipo == f.Tipo) &&
            (f.ContaId is null || l.ContaId == f.ContaId) &&
            (f.PessoaId is null || l.PessoaId == f.PessoaId) &&
            (busca.Length == 0 ||
             Texto.Normalizar(l.Descricao).Contains(busca) ||
             Texto.Normalizar(l.Categoria?.Nome).Contains(busca) ||
             Texto.Normalizar(l.Conta?.Nome).Contains(busca)))
            .ToList();
        var filtrados = semCategoria.Where(l => f.CategoriaId is null || l.CategoriaId == f.CategoriaId).ToList();

        var dias = filtrados
            .OrderByDescending(l => l.Data).ThenByDescending(l => l.CriadoEm)
            .GroupBy(l => l.Data)
            .Select(g => new DiaExtrato(g.Key, g.Select(Linha).ToList()))
            .ToList();

        var despesasSemCategoria = semCategoria.Where(l => l.Tipo == TipoLancamento.Despesa).ToList();
        var totalDespesas = despesasSemCategoria.Sum(l => l.ValorCentavos);
        var fatias = despesasSemCategoria
            .GroupBy(l => (l.CategoriaId, Nome: l.Categoria?.Nome ?? "Sem categoria"))
            .Select(g => new FatiaCategoria(g.Key.CategoriaId, g.Key.Nome, g.Sum(l => l.ValorCentavos),
                totalDespesas == 0 ? 0 : (double)g.Sum(l => l.ValorCentavos) / totalDespesas)
            {
                // Com filtro de conta ou busca, o total não é o do mês: comparar com o limite enganaria.
                OrcamentoCentavos = f.ContaId is null && f.PessoaId is null && string.IsNullOrWhiteSpace(f.Busca) ? g.First().Categoria?.OrcamentoCentavos : null,
            })
            .OrderByDescending(x => x.Centavos)
            .ToList();

        // O orçamento é do mês inteiro: compara com todas as despesas, sem filtros.
        var gastoMes = doMes.Where(l => l.Tipo == TipoLancamento.Despesa).Sum(l => l.ValorCentavos);
        var orcamento = await LerConfigLong(db, ChaveOrcamento(db), 0, ct);
        var ritmo = RitmoOrcamento.Calcular(hoje, f.Ano, f.Mes, orcamento, gastoMes);

        // Comparação justa: no mês corrente, o mês anterior só até o mesmo dia.
        var inicioAnterior = f.Inicio.AddMonths(-1);
        var fimAnterior = hoje >= f.Inicio && hoje < f.Fim
            ? inicioAnterior.AddDays(Math.Min(hoje.Day, DateTime.DaysInMonth(inicioAnterior.Year, inicioAnterior.Month)))
            : f.Inicio;
        var anterior = await db.Lancamentos
            .Where(l => l.Tipo == TipoLancamento.Despesa && l.Data >= inicioAnterior && l.Data < fimAnterior)
            .SumAsync(l => l.ValorCentavos, ct);

        var categorias = await db.Categorias.AsNoTracking().OrderBy(c => c.Nome)
            .Select(c => new OpcaoFiltro(c.Id, c.Nome)).ToListAsync(ct);
        var contas = await db.Contas.AsNoTracking().OrderBy(c => c.Nome)
            .Select(c => new OpcaoFiltro(c.Id, c.Nome)).ToListAsync(ct);

        return new ExtratoMes(
            f,
            filtrados.Where(l => l.Tipo == TipoLancamento.Receita).Sum(l => l.ValorCentavos),
            filtrados.Where(l => l.Tipo == TipoLancamento.Despesa).Sum(l => l.ValorCentavos),
            dias, fatias, categorias, contas, ritmo, anterior);

        static LinhaExtrato Linha(Lancamento l) => new(
            l.Id, l.Tipo, l.ValorCentavos, l.Data, l.Categoria?.Nome ?? "", l.Conta?.Nome ?? "", l.Descricao) { PessoaId = l.PessoaId };
    }

    public async Task<QuadroTarefas> ObterQuadroTarefasAsync(DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var desde = hoje.AddDays(-(QuadroTarefas.DiasDeConcluidas - 1));
        var tarefas = await db.Tarefas.AsNoTracking().Include(t => t.Responsaveis)
            .Where(t => t.ConcluidaEm == null || t.ConcluidaEm >= desde)
            .ToListAsync(ct);
        return QuadroTarefas.Montar(tarefas, hoje, await PessoasDaCasa(db, ct));
    }

    public async Task<ResultadoRegistro> DefinirPrazoTarefaAsync(Guid tarefaId, DateOnly? prazo, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var tarefa = await db.Tarefas.FindAsync([tarefaId], ct);
        if (tarefa is null) return NaoEncontrado();

        if (tarefa.Prazo != prazo) tarefa.AvisoEnviadoEm = null; // novo dia, novo aviso
        tarefa.Prazo = prazo;
        if (prazo is null) { tarefa.Hora = null; tarefa.AvisoMinutos = null; } // sem dia não tem horário
        await db.SaveChangesAsync(ct);
        var quando = prazo is { } p ? $"para {p:dd/MM}" : "sem prazo";
        return new(true, $"\"{tarefa.Titulo}\" ficou {quando}.");
    }

    public async Task<QuadroHabitos> ObterQuadroHabitosAsync(DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        // O histórico inteiro é pequeno (um registro por hábito por dia) e a melhor sequência precisa dele.
        var habitos = await db.Habitos.AsNoTracking().ToListAsync(ct);
        var registros = await db.RegistrosHabito.AsNoTracking().ToListAsync(ct);
        return QuadroHabitos.Montar(habitos, registros, hoje);
    }

    public async Task<ResultadoRegistro> DefinirHabitoAtivoAsync(Guid habitoId, bool ativo, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var habito = await db.Habitos.FindAsync([habitoId], ct);
        if (habito is null) return NaoEncontrado();

        habito.Ativo = ativo;
        await db.SaveChangesAsync(ct);
        return new(true, ativo ? $"{habito.Nome} voltou para a rotina." : $"{habito.Nome} arquivado. O histórico continua guardado.");
    }

    // ---------- Fitness pela captura ----------

    private static async Task<ResultadoRegistro> RegistrarExercicio(FavoaDbContext db, Interpretacao i, CancellationToken ct)
    {
        var tipo = i.Tipo == TipoCaptura.Cardio ? TipoExercicio.Cardio : TipoExercicio.Forca;
        var exercicio = await FitnessServico.ObterOuCriarExercicio(db, i.Titulo!, tipo, ct);
        var criados = new List<ReferenciaRegistro>();
        if (db.Entry(exercicio).State == EntityState.Added) criados.Add(new(TipoReferencia.Exercicio, exercicio.Id));

        // Exercícios ditos na captura no mesmo dia vão para o mesmo "treino livre".
        var sessao = await db.Sessoes.Include(s => s.Exercicios)
            .Where(s => s.Data == i.Data && s.FichaId == null)
            .OrderByDescending(s => s.CriadoEm)
            .FirstOrDefaultAsync(ct);
        if (sessao is null)
        {
            sessao = new SessaoTreino { Data = i.Data };
            db.Sessoes.Add(sessao);
            criados.Add(new(TipoReferencia.SessaoTreino, sessao.Id));
        }

        var feito = new ExercicioRealizado
        {
            SessaoId = sessao.Id,
            ExercicioId = exercicio.Id,
            Ordem = sessao.Exercicios.Count,
            Series = i.Series,
            Repeticoes = i.Repeticoes,
            CargaKg = i.CargaKg,
            Minutos = i.Minutos,
            DistanciaKm = i.DistanciaKm,
            Intensidade = i.Intensidade,
        };
        db.ExerciciosRealizados.Add(feito);

        // Gasto estimado já na confirmação (precisa do peso registrado).
        var atividade = GastoCalorico.Classificar(exercicio.Nome, exercicio.Tipo);
        var gasto = "";
        if (atividade is Atividade.MuayThai or Atividade.Futebol or Atividade.Corrida)
        {
            var calculo = GastoCalorico.Calcular([new ItemGasto(atividade, i.Minutos, i.DistanciaKm, null, i.Intensidade)], null, null,
                GastoCalorico.PesoEm(await FitnessServico.PesosAsync(db, ct), i.Data));
            gasto = calculo.Kcal is { } kcal ? $" · ≈ {kcal} kcal" : calculo.SemPeso ? " · registre seu peso para ver o gasto" : "";
        }

        return new(true, $"{exercicio.Nome} · {FitnessServico.Descrever(feito)}{gasto}",
            new ReferenciaRegistro(TipoReferencia.ExercicioRealizado, feito.Id, CriadosJunto: criados));
    }

    private static async Task<ResultadoRegistro> RegistrarRefeicao(FavoaDbContext db, Interpretacao i, CancellationToken ct)
    {
        var alimento = (await FitnessServico.Buscar(db, i.Titulo!, 1, ct)).FirstOrDefault();
        // "1 marmita de frango": medida que a frase não conhecia. Tenta sem a primeira palavra.
        if (alimento is null && i.Gramas is null && i.Titulo!.Split(' ', 2) is [_, var semMedida])
            alimento = (await FitnessServico.Buscar(db, semMedida, 1, ct)).FirstOrDefault();
        if (alimento is null)
            return new(false, $"Não achei \"{i.Titulo}\" na base de alimentos. Cadastre em Dieta → Alimentos.");

        var gramas = i.Gramas;
        string? unidade = null;
        double? quantidade = null;
        if (gramas is null && i.Unidades is { } u)
        {
            // "comi 2 ovos": a unidade do alimento (cadastro próprio, senão a medida padrão da tabela do IBGE).
            var tabela = await db.Porcoes.AsNoTracking().Where(p => p.AlimentoId == alimento.Id).ToListAsync(ct);
            var porcao = FitnessServico.Porcoes(alimento, tabela).FirstOrDefault(p => p.Padrao);
            if (porcao is not null)
            {
                gramas = (int)Math.Round((double)u * porcao.Gramas, MidpointRounding.AwayFromZero);
                unidade = porcao.Nome;
                quantidade = (double)u;
            }
        }
        if (gramas is null or <= 0)
            return new(false, $"Quantos gramas de {alimento.Nome}? Ex.: comi 150g de {i.Titulo}");

        var refeicao = i.Refeicao ?? Refeicoes.PelaHora(DateTime.Now.Hour);
        var consumo = new Consumo { Data = i.Data, Refeicao = refeicao, AlimentoId = alimento.Id, Gramas = gramas.Value, Unidade = unidade, Quantidade = quantidade };
        db.Consumos.Add(consumo);

        var kcal = Macros.De(alimento, gramas.Value).Kcal;
        return new(true, $"{Refeicoes.Nome(refeicao)}: {gramas} g de {alimento.Nome} · {kcal:0} kcal",
            new ReferenciaRegistro(TipoReferencia.Consumo, consumo.Id));
    }

    private static ResultadoRegistro NaoEncontrado() => new(false, "Esse registro não existe mais.");

    // ---------- Registros ----------

    private static async Task<ResultadoRegistro> RegistrarLancamento(FavoaDbContext db, Interpretacao i, CancellationToken ct)
    {
        var tipo = i.Tipo == TipoCaptura.Receita ? TipoLancamento.Receita : TipoLancamento.Despesa;
        // Só categorias cadastradas: assim tudo fica ligado (limite, relatório, calendário). Criar é em Cadastros.
        var categoria = await AcharPorNome(db.Categorias.Where(c => c.Tipo == tipo), c => c.Nome, i.Categoria, ct);
        if (categoria is null) return new(false, await TextoEscolhaCategoria(db, tipo, i.Categoria, ct));
        var conta = await ObterOuCriarConta(db, i.Conta, ct);

        var criadosJunto = new List<ReferenciaRegistro>();
        if (db.Entry(conta).State == EntityState.Added) criadosJunto.Add(new(TipoReferencia.Conta, conta.Id));

        var lancamento = new Lancamento
        {
            ContaId = conta.Id,
            CategoriaId = categoria.Id,
            Tipo = tipo,
            ValorCentavos = i.ValorCentavos!.Value,
            Data = i.Data,
            Descricao = "", // o lançamento é a categoria; sem texto livre
        };

        // "Repetir todo mês": este é o primeiro; os próximos meses saem da recorrência.
        if (i.RepetirTodoMes)
        {
            var recorrencia = new Recorrencia
            {
                Tipo = tipo,
                Descricao = categoria.Nome,
                ValorCentavos = i.ValorCentavos.Value,
                CategoriaId = categoria.Id,
                ContaId = conta.Id,
                DiaDoMes = i.Data.Day,
                Inicio = i.Data,
                UltimaOcorrencia = i.Data,
            };
            db.Recorrencias.Add(recorrencia);
            lancamento.RecorrenciaId = recorrencia.Id;
            criadosJunto.Add(new(TipoReferencia.Recorrencia, recorrencia.Id));
        }
        db.Lancamentos.Add(lancamento);

        var sinal = tipo == TipoLancamento.Despesa ? "−" : "+";
        var mensagem = $"{sinal} {Texto.FormatarMoeda(i.ValorCentavos.Value)} em {categoria.Nome} · {conta.Nome}";
        if (i.RepetirTodoMes) mensagem += $" · todo dia {Math.Min(i.Data.Day, 31)}";
        if (tipo == TipoLancamento.Despesa && categoria.OrcamentoCentavos is > 0 and var limite)
            mensagem += " · " + await TextoLimiteCategoria(db, categoria.Id, limite, i.Data, i.ValorCentavos.Value, ct);

        return new(true, mensagem, new ReferenciaRegistro(TipoReferencia.Lancamento, lancamento.Id, CriadosJunto: criadosJunto));
    }

    /// <summary>"80% do limite de Alimentação fora" ou "passou R$ 40,00 do limite", contando o lançamento novo.</summary>
    private static async Task<string> TextoLimiteCategoria(FavoaDbContext db, Guid categoriaId, long limite, DateOnly data, long novo, CancellationToken ct)
    {
        var inicio = new DateOnly(data.Year, data.Month, 1);
        var fim = inicio.AddMonths(1);
        var gasto = novo + await db.Lancamentos
            .Where(l => l.CategoriaId == categoriaId && l.Tipo == TipoLancamento.Despesa && l.Data >= inicio && l.Data < fim)
            .Select(l => l.ValorCentavos).SumAsync(ct);
        return gasto > limite
            ? $"passou {Texto.FormatarMoeda(gasto - limite)} do limite"
            : $"{Math.Round(100.0 * gasto / limite):0}% do limite do mês";
    }

    private static ResultadoRegistro RegistrarAgua(FavoaDbContext db, Interpretacao i)
    {
        var agua = new RegistroAgua { Mililitros = i.Mililitros!.Value, Data = i.Data };
        db.RegistrosAgua.Add(agua);
        return new(true, $"+ {i.Mililitros} ml de água", new ReferenciaRegistro(TipoReferencia.Agua, agua.Id));
    }

    private static async Task<ResultadoRegistro> MarcarHabito(FavoaDbContext db, Interpretacao i, CancellationToken ct)
    {
        var habito = await AcharPorNome(db.Habitos.Where(h => h.Ativo), h => h.Nome, i.Titulo, ct);
        if (habito is null) return new(false, $"Não encontrei o hábito \"{i.Titulo}\".");

        if (await db.RegistrosHabito.AnyAsync(r => r.HabitoId == habito.Id && r.Data == i.Data, ct))
            return new(false, $"{habito.Nome} já estava marcado.");

        var registro = new RegistroHabito { HabitoId = habito.Id, Data = i.Data };
        db.RegistrosHabito.Add(registro);
        return new(true, $"{habito.Nome} feito", new ReferenciaRegistro(TipoReferencia.RegistroHabito, registro.Id));
    }

    private static ResultadoRegistro RegistrarTarefa(FavoaDbContext db, Interpretacao i)
    {
        var tarefa = new Tarefa
        {
            Titulo = i.Titulo!,
            Prazo = i.Prazo,
            Hora = i.Hora is { } h ? new TimeOnly(h.Hour, h.Minute) : null,
            AvisoMinutos = i.AvisoMinutos,
        };
        DefinirResponsaveis(tarefa, i.Responsaveis);
        db.Tarefas.Add(tarefa);
        var quando = i.Prazo is { } p ? $" para {p:dd/MM}" + (tarefa.Hora is { } hh ? $" às {hh.ToString("HH:mm")}" : "") : "";
        var aviso = tarefa.AvisoMinutos is { } a ? $" · lembrete {AvisoTarefa.Rotulo(a).ToLowerInvariant()}" : "";
        return new(true, $"Tarefa{quando}: {i.Titulo}{aviso}",
            new ReferenciaRegistro(TipoReferencia.Tarefa, tarefa.Id));
    }

    private static async Task<ResultadoRegistro> CriarHabito(FavoaDbContext db, Interpretacao i, CancellationToken ct)
    {
        var nome = Texto.Capitalizar(i.Titulo!);
        if (await AcharPorNome(db.Habitos, h => h.Nome, nome, ct) is not null)
            return new(false, $"O hábito {nome} já existe.");

        var habito = new Habito { Nome = nome };
        db.Habitos.Add(habito);
        return new(true, $"Novo hábito: {nome}", new ReferenciaRegistro(TipoReferencia.Habito, habito.Id));
    }

    private static async Task<ResultadoRegistro> CriarConta(FavoaDbContext db, Interpretacao i, CancellationToken ct)
    {
        var nome = Texto.Capitalizar(i.Titulo!);
        if (await AcharPorNome(db.Contas, c => c.Nome, nome, ct) is not null)
            return new(false, $"A conta {nome} já existe.");

        var conta = new Conta { Nome = nome };
        db.Contas.Add(conta);
        return new(true, $"Nova conta: {nome}", new ReferenciaRegistro(TipoReferencia.Conta, conta.Id));
    }

    private static async Task<ResultadoRegistro> DefinirOrcamento(FavoaDbContext db, Interpretacao i, CancellationToken ct)
    {
        var cfg = await db.Configuracoes.FindAsync([ChaveOrcamento(db)], ct);
        var anterior = cfg?.Valor ?? "";
        if (cfg is null)
            db.Configuracoes.Add(new Configuracao { Chave = ChaveOrcamento(db), Valor = i.ValorCentavos!.Value.ToString() });
        else
            cfg.Valor = i.ValorCentavos!.Value.ToString();

        return new(true, $"Orçamento mensal: {Texto.FormatarMoeda(i.ValorCentavos!.Value)}",
            new ReferenciaRegistro(TipoReferencia.Orcamento, Guid.Empty, anterior));
    }

    // ---------- Apoio ----------

    private static async Task<Conta> ObterOuCriarConta(FavoaDbContext db, string? nome, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(nome))
        {
            var existente = await AcharPorNome(db.Contas, c => c.Nome, nome, ct);
            if (existente is not null) return existente;

            var nova = new Conta { Nome = Texto.Capitalizar(nome) };
            db.Contas.Add(nova);
            return nova;
        }

        return await db.Contas.Where(c => c.Ativa).OrderBy(c => c.CriadoEm).FirstOrDefaultAsync(ct)
               ?? db.Contas.Add(new Conta { Nome = "Carteira" }).Entity;
    }

    /// <summary>Categorias de despesa e de receita, das mais usadas para as menos (depois por nome).</summary>
    internal static async Task<(IReadOnlyList<string> Despesa, IReadOnlyList<string> Receita)> CategoriasPorUso(FavoaDbContext db, CancellationToken ct)
    {
        var usos = await db.Lancamentos.GroupBy(l => l.CategoriaId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var todas = await db.Categorias.AsNoTracking().ToListAsync(ct);
        IReadOnlyList<string> Do(TipoLancamento t) => todas.Where(c => c.Tipo == t)
            .OrderByDescending(c => usos.GetValueOrDefault(c.Id)).ThenBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => c.Nome).ToList();
        return (Do(TipoLancamento.Despesa), Do(TipoLancamento.Receita));
    }

    /// <summary>Explica que a categoria não existe e lista as que existem do tipo.</summary>
    internal static async Task<string> TextoEscolhaCategoria(FavoaDbContext db, TipoLancamento tipo, string? pedida, CancellationToken ct)
    {
        var nome = tipo == TipoLancamento.Receita ? "receita" : "despesa";
        var lista = tipo == TipoLancamento.Receita ? (await CategoriasPorUso(db, ct)).Receita : (await CategoriasPorUso(db, ct)).Despesa;
        if (lista.Count == 0) return $"Nenhuma categoria de {nome} cadastrada. Crie em Contas e categorias.";
        var inicio = string.IsNullOrWhiteSpace(pedida) ? $"Escolha a categoria de {nome}" : $"A categoria {pedida} não existe. Escolha uma de {nome}";
        return $"{inicio}: {string.Join(", ", lista.Take(12))}{(lista.Count > 12 ? "…" : "")}. Para criar outra, use Contas e categorias > Categorias.";
    }

    /// <summary>Busca ignorando maiúsculas e acentos. As listas são pequenas: a comparação é feita em memória.</summary>
    private static async Task<T?> AcharPorNome<T>(IQueryable<T> consulta, Func<T, string> nome, string? alvo, CancellationToken ct)
        where T : class
    {
        var alvoNorm = Texto.Normalizar(alvo);
        if (alvoNorm.Length == 0) return null;

        var itens = await consulta.ToListAsync(ct);
        return itens.FirstOrDefault(x => Texto.Normalizar(nome(x)) == alvoNorm);
    }

    /// <summary>O orçamento é da casa: cada família guarda o seu (a chave leva o id da família).</summary>
    internal static string ChaveOrcamento(FavoaDbContext db) => AbrirBanco.ChaveDaFamilia(Configuracao.OrcamentoMensalCentavos, db.FamiliaAtual);

    private static async Task<long> LerConfigLong(FavoaDbContext db, string chave, long padrao, CancellationToken ct)
    {
        var cfg = await db.Configuracoes.FindAsync([chave], ct);
        return cfg is not null && long.TryParse(cfg.Valor, out var v) ? v : padrao;
    }

    /// <summary>Dias seguidos com o hábito feito, contando até hoje (ou até ontem, se hoje ainda não foi feito).</summary>
    private static int Sequencia(Guid habitoId, List<RegistroHabito> registros, DateOnly hoje)
    {
        var dias = registros.Where(r => r.HabitoId == habitoId).Select(r => r.Data).ToHashSet();
        var dia = dias.Contains(hoje) ? hoje : hoje.AddDays(-1);
        var total = 0;
        while (dias.Contains(dia))
        {
            total++;
            dia = dia.AddDays(-1);
        }
        return total;
    }

    private static async Task<IReadOnlyList<RegistroRecente>> Recentes(
        FavoaDbContext db, DateOnly hoje, List<Habito> habitos, CancellationToken ct)
    {
        var lancamentos = await db.Lancamentos
            .Include(l => l.Categoria)
            .Include(l => l.Conta)
            .OrderByDescending(l => l.CriadoEm)
            .Take(6)
            .ToListAsync(ct);

        var agua = await db.RegistrosAgua.Where(a => a.Data == hoje).ToListAsync(ct);
        var habitosHoje = await db.RegistrosHabito.Where(r => r.Data == hoje).ToListAsync(ct);
        var nomes = habitos.ToDictionary(h => h.Id, h => h.Nome);

        var itens = new List<RegistroRecente>();
        itens.AddRange(lancamentos.Select(l => new RegistroRecente(
            $"{l.Categoria?.Nome} · {l.Conta?.Nome}{(l.Data == hoje ? "" : l.Data == hoje.AddDays(-1) ? " · ontem" : $" · {l.Data:dd/MM}")}",
            $"{(l.Tipo == TipoLancamento.Despesa ? "−" : "+")} {Texto.FormatarMoeda(l.ValorCentavos)}",
            l.CriadoEm,
            new ReferenciaRegistro(TipoReferencia.Lancamento, l.Id))));
        itens.AddRange(agua.Select(a => new RegistroRecente(
            "Água", $"+ {a.Mililitros} ml", a.CriadoEm, new ReferenciaRegistro(TipoReferencia.Agua, a.Id))));
        itens.AddRange(habitosHoje
            .Where(r => nomes.ContainsKey(r.HabitoId))
            .Select(r => new RegistroRecente(
                nomes[r.HabitoId], "feito", r.CriadoEm, new ReferenciaRegistro(TipoReferencia.RegistroHabito, r.Id))));

        var feitos = await db.ExerciciosRealizados.AsNoTracking().Include(e => e.Exercicio).Include(e => e.Sessao)
            .Where(e => e.Sessao!.Data == hoje).ToListAsync(ct);
        itens.AddRange(feitos.Select(e => new RegistroRecente(
            e.Exercicio?.Nome ?? "Exercício", FitnessServico.Descrever(e), e.CriadoEm, new ReferenciaRegistro(TipoReferencia.ExercicioRealizado, e.Id))));

        var comidos = await db.Consumos.AsNoTracking().Include(c => c.Alimento).Where(c => c.Data == hoje).ToListAsync(ct);
        itens.AddRange(comidos.Select(c => new RegistroRecente(
            c.Alimento?.Nome ?? "Alimento", $"{Macros.De(c.Alimento!, c.Gramas).Kcal:0} kcal", c.CriadoEm, new ReferenciaRegistro(TipoReferencia.Consumo, c.Id))));

        var pesoHoje = await db.Pesos.AsNoTracking().FirstOrDefaultAsync(p => p.Data == hoje, ct);
        if (pesoHoje is not null)
            itens.Add(new RegistroRecente("Peso", $"{pesoHoje.Kg:0.0} kg".Replace('.', ','), pesoHoje.CriadoEm, new ReferenciaRegistro(TipoReferencia.Peso, pesoHoje.Id)));

        return itens.OrderByDescending(x => x.Quando).Take(6).ToList();
    }
}
