using System.Globalization;
using Favoa.Core;
using Favoa.Core.Captura;
using Favoa.Core.Fitness;
using Favoa.Core.Rotina;
using Microsoft.EntityFrameworkCore;

namespace Favoa.Data;

public sealed class FitnessServico(IDbContextFactory<FavoaDbContext> fabrica, Favoa.Core.Pessoas.IPessoaAtual pessoa) : IFitnessServico
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    private const int MetaAguaPadraoMl = 2500;

    // =================================================================
    //  Treino
    // =================================================================

    public async Task<PainelTreino> ObterPainelTreinoAsync(DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);

        var fichas = await CarregarFichas(db, ct);
        var pesos = await PesosAsync(db, ct);
        var sessoes = await db.Sessoes.AsNoTracking()
            .Include(s => s.Ficha)
            .Include(s => s.Exercicios).ThenInclude(e => e.Exercicio)
            .OrderByDescending(s => s.Data).ThenByDescending(s => s.CriadoEm)
            .Take(30)
            .ToListAsync(ct);

        var datas = await db.Sessoes.Where(s => s.Data > hoje.AddDays(-7)).Select(s => s.Data).ToListAsync(ct);
        var ultimaFicha = sessoes.FirstOrDefault(s => s.FichaId != null)?.FichaId;
        var proxima = EvolucaoTreino.ProximaFicha(
            fichas.Select(f => new FichaTreino { Id = f.Id, Nome = f.Nome, Ordem = f.Ordem }).ToList(), ultimaFicha);

        return new PainelTreino(
            hoje,
            EvolucaoTreino.DiasTreinadosUltimos7(datas, hoje),
            (await LerMetas(db, ct)).TreinosSemana,
            proxima is null ? null : fichas.First(f => f.Id == proxima.Id),
            fichas,
            sessoes.Select(x => Resumir(x, pesos)).ToList(),
            await ResumirExercicios(db, ct))
        {
            PesoAtualKg = GastoCalorico.PesoEm(pesos, hoje),
        };
    }

    public async Task<SessaoRascunho> PrepararSessaoAsync(Guid? fichaId, DateOnly dia, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var rascunho = new SessaoRascunho { Data = dia };

        if (fichaId is null) return rascunho;
        var ficha = await db.Fichas.AsNoTracking()
            .Include(f => f.Itens).ThenInclude(i => i.Exercicio)
            .FirstOrDefaultAsync(f => f.Id == fichaId, ct);
        if (ficha is null) return rascunho;

        rascunho.FichaId = ficha.Id;
        rascunho.Titulo = ficha.Nome;

        var ids = ficha.Itens.Select(i => i.ExercicioId).ToList();
        var ultimas = (await db.ExerciciosRealizados.AsNoTracking()
                .Include(e => e.Sessao)
                .Where(e => ids.Contains(e.ExercicioId))
                .ToListAsync(ct))
            .GroupBy(e => e.ExercicioId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Sessao!.Data).ThenByDescending(e => e.CriadoEm).First());

        foreach (var item in ficha.Itens.OrderBy(i => i.Ordem))
        {
            var ultima = ultimas.GetValueOrDefault(item.ExercicioId);
            rascunho.Linhas.Add(new LinhaSessao
            {
                Exercicio = item.Exercicio!.Nome,
                Tipo = item.Exercicio.Tipo,
                // Começa pelo que fez da última vez (progressão), senão pela meta da ficha.
                Series = ultima?.Series ?? item.Series,
                Repeticoes = ultima?.Repeticoes ?? item.Repeticoes,
                CargaKg = ultima?.CargaKg ?? item.CargaKg,
                Minutos = ultima?.Minutos ?? item.Minutos,
                DistanciaKm = ultima?.DistanciaKm,
                Intensidade = ultima?.Intensidade,
                UltimaVez = ultima is null ? null : $"{Descrever(ultima)} em {ultima.Sessao!.Data:dd/MM}",
            });
        }

        return rascunho;
    }

    public async Task<SessaoRascunho?> PrepararRepeticaoAsync(DateOnly dia, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var ultima = await db.Sessoes.AsNoTracking()
            .Include(s => s.Exercicios).ThenInclude(e => e.Exercicio)
            .OrderByDescending(s => s.Data).ThenByDescending(s => s.CriadoEm)
            .FirstOrDefaultAsync(ct);
        if (ultima is null || ultima.Exercicios.Count == 0) return null;

        var titulo = ultima.FichaId is { } fid ? (await db.Fichas.AsNoTracking().Where(f => f.Id == fid).Select(f => f.Nome).FirstOrDefaultAsync(ct)) ?? "Treino livre" : "Treino livre";
        var r = new SessaoRascunho { Data = dia, FichaId = ultima.FichaId, Titulo = titulo, MinutosForca = ultima.MinutosForca, IntensidadeForca = ultima.IntensidadeForca };
        foreach (var e in ultima.Exercicios.OrderBy(x => x.Ordem))
        {
            r.Linhas.Add(new LinhaSessao
            {
                Exercicio = e.Exercicio!.Nome, Tipo = e.Exercicio.Tipo, Series = e.Series, Repeticoes = e.Repeticoes, CargaKg = e.CargaKg,
                Minutos = e.Minutos, DistanciaKm = e.DistanciaKm, Intensidade = e.Intensidade, Feito = true,
                UltimaVez = $"{Descrever(e)} em {ultima.Data:dd/MM}",
            });
        }
        return r;
    }

    public async Task<IReadOnlyDictionary<string, UltimoEsporte>> ObterUltimosEsportesAsync(IEnumerable<string> nomes, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var exercicios = await db.Exercicios.AsNoTracking().ToListAsync(ct);
        var resultado = new Dictionary<string, UltimoEsporte>();
        foreach (var nome in nomes)
        {
            var ids = exercicios.Where(e => Texto.Normalizar(e.Nome) == Texto.Normalizar(nome)).Select(e => e.Id).ToList();
            if (ids.Count == 0) continue;
            var ultimo = await db.ExerciciosRealizados.AsNoTracking()
                .Where(x => ids.Contains(x.ExercicioId) && x.Minutos > 0)
                .OrderByDescending(x => x.Sessao!.Data).ThenByDescending(x => x.Sessao!.CriadoEm)
                .FirstOrDefaultAsync(ct);
            if (ultimo is not null) resultado[nome] = new UltimoEsporte(nome, ultimo.Minutos, ultimo.Intensidade, ultimo.DistanciaKm);
        }
        return resultado;
    }

    public async Task<ResultadoRegistro> SalvarSessaoAsync(SessaoRascunho r, CancellationToken ct = default)
    {
        var linhas = r.Linhas.Where(l => l.Feito && !string.IsNullOrWhiteSpace(l.Exercicio)).ToList();
        if (linhas.Count == 0) return new(false, "Marque pelo menos um exercício como feito.");
        if (r.MinutosForca < 0 || linhas.Any(l => l.Series < 0 || l.Repeticoes < 0 || l.CargaKg < 0 || l.Minutos < 0 || l.DistanciaKm < 0))
            return new(false, "Há números negativos no treino.");

        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var sessao = new SessaoTreino { Data = r.Data, FichaId = r.FichaId, Observacao = r.Observacao.Trim(), MinutosForca = r.MinutosForca is > 0 ? r.MinutosForca : null, IntensidadeForca = r.IntensidadeForca };
        db.Sessoes.Add(sessao);

        var ordem = 0;
        foreach (var l in linhas)
        {
            var exercicio = await ObterOuCriarExercicio(db, l.Exercicio, l.Tipo, ct);
            sessao.Exercicios.Add(new ExercicioRealizado
            {
                ExercicioId = exercicio.Id,
                Ordem = ordem++,
                Series = l.Series,
                Repeticoes = l.Repeticoes,
                CargaKg = l.CargaKg,
                Minutos = l.Minutos,
                DistanciaKm = l.DistanciaKm,
                Intensidade = GastoCalorico.PedeIntensidade(GastoCalorico.Classificar(l.Exercicio, l.Tipo)) && l.Tipo != TipoExercicio.Forca ? l.Intensidade : null,
            });
        }

        await db.SaveChangesAsync(ct);
        return new(true, $"{r.Titulo} salvo: {Plural(linhas.Count, "exercício", "exercícios")}.", new(Favoa.Core.Edicao.TipoReferencia.SessaoTreino, sessao.Id));
    }

    public async Task<ResultadoRegistro> ExcluirSessaoAsync(Guid sessaoId, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var sessao = await db.Sessoes.FindAsync([sessaoId], ct);
        if (sessao is null) return NaoEncontrado();
        db.Sessoes.Remove(sessao);
        await db.SaveChangesAsync(ct);
        return new(true, "Treino excluído.");
    }

    public async Task<ResultadoRegistro> SalvarFichaAsync(FichaEdicao f, CancellationToken ct = default)
    {
        var nome = f.Nome.Trim();
        if (nome.Length == 0) return new(false, "Dê um nome à ficha. Ex.: Treino A — peito e tríceps");
        var itens = f.Itens.Where(i => !string.IsNullOrWhiteSpace(i.Exercicio)).ToList();
        if (itens.Count == 0) return new(false, "Adicione pelo menos um exercício.");

        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        FichaTreino ficha;
        if (f.Id is { } id)
        {
            ficha = await db.Fichas.Include(x => x.Itens).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException();
            db.ItensFicha.RemoveRange(ficha.Itens);
            ficha.Itens.Clear();
        }
        else
        {
            ficha = new FichaTreino { Ordem = await db.Fichas.CountAsync(ct) };
            db.Fichas.Add(ficha);
        }
        ficha.Nome = nome;

        var ordem = 0;
        foreach (var i in itens)
        {
            var exercicio = await ObterOuCriarExercicio(db, i.Exercicio, i.Tipo, ct);
            ficha.Itens.Add(new ItemFicha
            {
                ExercicioId = exercicio.Id, Ordem = ordem++,
                Series = i.Series, Repeticoes = i.Repeticoes, CargaKg = i.CargaKg, Minutos = i.Minutos,
            });
        }

        await db.SaveChangesAsync(ct);
        return new(true, f.Id is null ? $"Ficha criada: {nome}" : $"Ficha atualizada: {nome}");
    }

    public async Task<ResultadoRegistro> ExcluirFichaAsync(Guid fichaId, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var ficha = await db.Fichas.FindAsync([fichaId], ct);
        if (ficha is null) return NaoEncontrado();
        db.Fichas.Remove(ficha); // os treinos feitos com ela ficam, só perdem o vínculo
        await db.SaveChangesAsync(ct);
        return new(true, $"Ficha {ficha.Nome} excluída. Os treinos feitos continuam no histórico.");
    }

    public async Task<EvolucaoExercicio?> ObterEvolucaoAsync(Guid exercicioId, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var resumo = (await ResumirExercicios(db, ct)).FirstOrDefault(e => e.Id == exercicioId);
        if (resumo is null) return null;

        var feitos = await db.ExerciciosRealizados.AsNoTracking()
            .Include(e => e.Sessao)
            .Where(e => e.ExercicioId == exercicioId)
            .ToListAsync(ct);

        var pontos = EvolucaoTreino.Pontos(feitos.Select(e => (e.Sessao!.Data, e)), resumo.Tipo);
        var historico = feitos
            .OrderByDescending(e => e.Sessao!.Data).ThenByDescending(e => e.CriadoEm)
            .Take(20)
            .Select(e => $"{e.Sessao!.Data:dd/MM/yyyy} · {Descrever(e)}")
            .ToList();
        return new EvolucaoExercicio(resumo, pontos, historico);
    }

    // =================================================================
    //  Dieta
    // =================================================================

    public async Task<DiaDieta> ObterDiaDietaAsync(DateOnly dia, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var plano = await db.ItensPlano.AsNoTracking().Include(i => i.Alimento).OrderBy(i => i.Ordem).ToListAsync(ct);
        var consumos = await db.Consumos.AsNoTracking().Include(c => c.Alimento)
            .Where(c => c.Data == dia).OrderBy(c => c.CriadoEm).ToListAsync(ct);

        var refeicoes = Refeicoes.Todas.Select(r => new RefeicaoDia(
            r,
            plano.Where(p => p.Refeicao == r).Select(p => new ItemRefeicao(p.Id, p.AlimentoId, p.Alimento!.Nome, p.Gramas, Macros.De(p.Alimento, p.Gramas), true) { Medida = TextoMedida(p.Unidade, p.Quantidade) }).ToList(),
            consumos.Where(c => c.Refeicao == r).Select(c => new ItemRefeicao(c.Id, c.AlimentoId, c.Alimento!.Nome, c.Gramas, Macros.De(c.Alimento, c.Gramas), c.DoPlano) { Medida = TextoMedida(c.Unidade, c.Quantidade) }).ToList()))
            .ToList();

        var agua = await db.RegistrosAgua.Where(a => a.Data == dia).SumAsync(a => (int?)a.Mililitros, ct) ?? 0;
        var chaveAgua = AbrirBanco.ChaveDaPessoa(Configuracao.MetaAguaMl, db.PessoaAtual);
        var metaAgua = await db.Configuracoes.Where(c => c.Chave == chaveAgua).Select(c => c.Valor).FirstOrDefaultAsync(ct);

        var gasto = await GastoPorDiaAsync(db, dia, dia, ct);
        return new DiaDieta(dia, refeicoes, await LerMetas(db, ct), agua,
            int.TryParse(metaAgua, out var m) && m > 0 ? m : MetaAguaPadraoMl)
        {
            GastoTreinoKcal = gasto.GetValueOrDefault(dia),
        };
    }

    public async Task<IReadOnlyList<AlimentoResumo>> BuscarAlimentosAsync(string texto, int limite = 12, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        return await ResumirComPorcoesAsync(db, await Buscar(db, texto, limite, ct), ct);
    }

    /// <summary>
    /// Busca por palavras (sem acento). Ordem: o que você mais come, o que está no plano,
    /// os seus alimentos, os que começam com a primeira palavra, e por fim os nomes mais curtos.
    /// </summary>
    internal static async Task<List<Alimento>> Buscar(FavoaDbContext db, string texto, int limite, CancellationToken ct)
    {
        var termos = Texto.Normalizar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.TrimEnd('s')) // "ovos" acha "ovo"
            .Where(t => t.Length > 0)
            .ToArray();
        if (termos.Length == 0) return [];

        var todos = await db.Alimentos.AsNoTracking().ToListAsync(ct);
        var usos = await db.Consumos.GroupBy(c => c.AlimentoId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var noPlano = (await db.ItensPlano.Select(i => i.AlimentoId).ToListAsync(ct)).ToHashSet();

        return todos
            .Select(a => (Alimento: a, Nome: Texto.Normalizar(a.Nome),
                // Produto com marca também é achado pela marca ("leite moça").
                Busca: Texto.Normalizar(a.Fonte == FontesAlimento.OpenFoodFacts ? $"{a.Nome} {a.Grupo}" : a.Nome)))
            .Where(x => termos.All(t => x.Busca.Contains(t)))
            .OrderByDescending(x => usos.GetValueOrDefault(x.Alimento.Id))
            .ThenByDescending(x => noPlano.Contains(x.Alimento.Id))
            .ThenByDescending(x => x.Alimento.Personalizado)
            .ThenByDescending(x => x.Nome.StartsWith(termos[0]))
            .ThenByDescending(x => x.Alimento.CodigoTaco is not null) // TACO antes do IBGE quando empatam
            .ThenBy(x => x.Nome.Length)
            .Take(limite)
            .Select(x => x.Alimento)
            .ToList();
    }

    public async Task<AlimentoResumo> SalvarProdutoOnlineAsync(ProdutoOnline p, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var existente = await db.Alimentos.FirstOrDefaultAsync(a => a.Fonte == FontesAlimento.OpenFoodFacts && a.CodigoExterno == p.Codigo, ct);
        if (existente is not null) return Resumir(existente);

        var nome = Texto.Capitalizar(p.Nome.Trim());
        var alimento = new Alimento
        {
            Nome = nome.Length > 120 ? nome[..120] : nome,
            Grupo = string.IsNullOrWhiteSpace(p.Marca) ? "Produtos" : (p.Marca.Length > 80 ? p.Marca[..80] : p.Marca),
            Kcal = Math.Round(p.Kcal, 1),
            Proteina = Math.Round(p.Proteina, 1),
            Carboidrato = Math.Round(p.Carboidrato, 1),
            Gordura = Math.Round(p.Gordura, 1),
            Fibra = Math.Round(p.Fibra, 1),
            Fonte = FontesAlimento.OpenFoodFacts,
            CodigoExterno = p.Codigo,
            NomePorcao = p.GramasPorcao is > 0 ? (p.NomePorcao ?? "porção") : null,
            GramasPorcao = p.GramasPorcao is > 0 ? p.GramasPorcao : null,
        };
        db.Alimentos.Add(alimento);
        await db.SaveChangesAsync(ct);
        return Resumir(alimento);
    }

    public async Task<ResultadoRegistro> SalvarAlimentoAsync(AlimentoEdicao e, CancellationToken ct = default)
    {
        var nome = Texto.Capitalizar(e.Nome);
        if (nome.Length == 0) return new(false, "Dê um nome ao alimento.");
        if (e.Kcal < 0 || e.Proteina < 0 || e.Carboidrato < 0 || e.Gordura < 0) return new(false, "Valores não podem ser negativos.");
        if (e.GramasPorcao is <= 0) return new(false, "A porção precisa ter mais de 0 g.");

        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var alimento = e.Id is { } id ? await db.Alimentos.FindAsync([id], ct) : null;
        if (alimento is null)
        {
            alimento = new Alimento { Personalizado = true, Grupo = "Meus alimentos" };
            db.Alimentos.Add(alimento);
        }

        alimento.Nome = nome;
        alimento.Kcal = e.Kcal;
        alimento.Proteina = e.Proteina;
        alimento.Carboidrato = e.Carboidrato;
        alimento.Gordura = e.Gordura;
        alimento.NomePorcao = string.IsNullOrWhiteSpace(e.NomePorcao) ? null : e.NomePorcao.Trim();
        alimento.GramasPorcao = e.GramasPorcao;
        await db.SaveChangesAsync(ct);
        return new(true, $"Alimento salvo: {nome}");
    }

    /// <summary>"1,5 fatia" para o que foi lançado numa medida; nada para g e ml (a tela já mostra os gramas).</summary>
    internal static string? TextoMedida(string? unidade, double? quantidade) =>
        unidade is null or "g" or "ml" || quantidade is not > 0
            ? null
            : $"{quantidade.Value.ToString("0.##", PtBr)} {(quantidade > 1 ? PluralDaMedida(unidade.ToLower(PtBr)) : unidade.ToLower(PtBr))}";

    /// <summary>"fatia" → "fatias", "colher de sopa" → "colheres de sopa", "unidade pequena" → "unidades pequenas", "porção" → "porções".</summary>
    public static string PluralDaMedida(string medida)
    {
        var palavras = medida.Split(' ');
        string[] ligacoes = ["de", "do", "da", "dos", "das"];
        for (var i = 0; i < palavras.Length; i++)
        {
            var w = palavras[i];
            if (i > 0 && (ligacoes.Contains(palavras[i - 1]) || w.Contains('/') || w.Length < 3)) break;
            if (i > 0 && ligacoes.Contains(w)) break;
            palavras[i] = w.EndsWith("ão") ? w[..^2] + "ões" : w.EndsWith('r') ? w + "es" : w.EndsWith('s') ? w : w + "s";
            if (i == 0 && palavras.Length > 1 && ligacoes.Contains(palavras[1])) break;
        }
        return string.Join(' ', palavras);
    }

    private static (string? Unidade, double? Quantidade) LimparUnidade(string? unidade, double? quantidade) =>
        string.IsNullOrWhiteSpace(unidade) || quantidade is not > 0
            ? (null, null)
            : (unidade.Trim().Length > 60 ? unidade.Trim()[..60] : unidade.Trim(), Math.Round(quantidade.Value, 2));

    public Task<ResultadoRegistro> AdicionarConsumoAsync(DateOnly dia, Refeicao refeicao, Guid alimentoId, int gramas, CancellationToken ct = default) =>
        AdicionarConsumoAsync(dia, refeicao, alimentoId, gramas, null, null, ct);

    public async Task<ResultadoRegistro> AdicionarConsumoAsync(DateOnly dia, Refeicao refeicao, Guid alimentoId, int gramas, string? unidade, double? quantidade, CancellationToken ct = default)
    {
        if (gramas <= 0) return new(false, "Informe a quantidade em gramas.");
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var alimento = await db.Alimentos.FindAsync([alimentoId], ct);
        if (alimento is null) return NaoEncontrado();

        var (un, qtd) = LimparUnidade(unidade, quantidade);
        var consumo = new Consumo { Data = dia, Refeicao = refeicao, AlimentoId = alimentoId, Gramas = gramas, Unidade = un, Quantidade = qtd };
        db.Consumos.Add(consumo);
        await db.SaveChangesAsync(ct);
        var medida = TextoMedida(un, qtd);
        return new(true, $"{(medida is null ? "" : medida + " = ")}{gramas} g de {alimento.Nome} · {Macros.De(alimento, gramas).Kcal:0} kcal",
            new(Core.Edicao.TipoReferencia.Consumo, consumo.Id));
    }

    public async Task<ResultadoRegistro> RemoverConsumoAsync(Guid consumoId, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var c = await db.Consumos.FindAsync([consumoId], ct);
        if (c is null) return NaoEncontrado();
        db.Consumos.Remove(c);
        await db.SaveChangesAsync(ct);
        return new(true, "Removido.");
    }

    public async Task<ResultadoRegistro> SeguirPlanoAsync(DateOnly dia, Refeicao refeicao, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var itens = await db.ItensPlano.Where(i => i.Refeicao == refeicao).OrderBy(i => i.Ordem).ToListAsync(ct);
        if (itens.Count == 0) return new(false, $"Não há plano para {Refeicoes.Nome(refeicao).ToLower(PtBr)}.");

        // Clicar de novo não duplica: tira o que veio do plano antes.
        var anteriores = await db.Consumos.Where(c => c.Data == dia && c.Refeicao == refeicao && c.DoPlano).ToListAsync(ct);
        db.Consumos.RemoveRange(anteriores);
        db.Consumos.AddRange(itens.Select(i => new Consumo { Data = dia, Refeicao = refeicao, AlimentoId = i.AlimentoId, Gramas = i.Gramas, Unidade = i.Unidade, Quantidade = i.Quantidade, DoPlano = true }));
        await db.SaveChangesAsync(ct);
        return new(true, $"{Refeicoes.Nome(refeicao)} conforme o plano.");
    }

    public async Task<ResultadoRegistro> CopiarRefeicaoAsync(DateOnly origem, DateOnly destino, Refeicao refeicao, CancellationToken ct = default)
    {
        if (origem == destino) return new(false, "Escolha outro dia para copiar.");
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var itens = await db.Consumos.Where(c => c.Data == origem && c.Refeicao == refeicao).OrderBy(c => c.CriadoEm).ToListAsync(ct);
        if (itens.Count == 0) return new(false, $"Não há {Refeicoes.Nome(refeicao).ToLower(PtBr)} em {origem:dd/MM} para copiar.");
        db.Consumos.AddRange(itens.Select(c => new Consumo { Data = destino, Refeicao = refeicao, AlimentoId = c.AlimentoId, Gramas = c.Gramas, Unidade = c.Unidade, Quantidade = c.Quantidade, DoPlano = c.DoPlano }));
        await db.SaveChangesAsync(ct);
        return new(true, $"{Refeicoes.Nome(refeicao)} copiado de {origem:dd/MM}: {Plural(itens.Count, "item", "itens")}.");
    }

    public async Task<IReadOnlyList<AlimentoRecente>> ObterAlimentosRecentesAsync(DateOnly hoje, int max = 8, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var desde = hoje.AddDays(-60);
        var consumos = await db.Consumos.AsNoTracking().Where(c => c.Data >= desde).Include(c => c.Alimento).ToListAsync(ct);
        var top = consumos.Where(c => c.Alimento is not null)
            .GroupBy(c => c.AlimentoId)
            .Select(g => (Ultimo: g.OrderByDescending(c => c.Data).ThenByDescending(c => c.CriadoEm).First(), Vezes: g.Count()))
            .OrderByDescending(x => x.Vezes).ThenByDescending(x => x.Ultimo.Data)
            .Take(max)
            .ToList();
        var resumos = (await ResumirComPorcoesAsync(db, top.Select(x => x.Ultimo.Alimento!).ToList(), ct)).ToDictionary(r => r.Id);
        return top.Select(x => new AlimentoRecente(resumos[x.Ultimo.AlimentoId], x.Ultimo.Gramas, x.Vezes)).ToList();
    }

    public Task<ResultadoRegistro> AdicionarItemPlanoAsync(Refeicao refeicao, Guid alimentoId, int gramas, CancellationToken ct = default) =>
        AdicionarItemPlanoAsync(refeicao, alimentoId, gramas, null, null, ct);

    public async Task<ResultadoRegistro> AdicionarItemPlanoAsync(Refeicao refeicao, Guid alimentoId, int gramas, string? unidade, double? quantidade, CancellationToken ct = default)
    {
        if (gramas <= 0) return new(false, "Informe a quantidade em gramas.");
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var alimento = await db.Alimentos.FindAsync([alimentoId], ct);
        if (alimento is null) return NaoEncontrado();

        var ordem = await db.ItensPlano.CountAsync(i => i.Refeicao == refeicao, ct);
        var (un, qtd) = LimparUnidade(unidade, quantidade);
        db.ItensPlano.Add(new ItemPlano { Refeicao = refeicao, AlimentoId = alimentoId, Gramas = gramas, Unidade = un, Quantidade = qtd, Ordem = ordem });
        await db.SaveChangesAsync(ct);
        return new(true, $"{alimento.Nome} no plano do {Refeicoes.Nome(refeicao).ToLower(PtBr)}.");
    }

    public async Task<ResultadoRegistro> RemoverItemPlanoAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var item = await db.ItensPlano.FindAsync([itemId], ct);
        if (item is null) return NaoEncontrado();
        db.ItensPlano.Remove(item);
        await db.SaveChangesAsync(ct);
        return new(true, "Tirado do plano.");
    }

    public async Task<MetasFitness> ObterMetasAsync(CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        return await LerMetas(db, ct);
    }

    public async Task SalvarMetasAsync(MetasFitness m, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        foreach (var (chave, valor) in new[]
                 {
                     (MetasFitness.ChaveKcal, m.Kcal), (MetasFitness.ChaveProteina, m.ProteinaG),
                     (MetasFitness.ChaveCarboidrato, m.CarboidratoG), (MetasFitness.ChaveGordura, m.GorduraG),
                     (MetasFitness.ChaveTreinosSemana, m.TreinosSemana), (MetasFitness.ChaveCompensacao, Math.Min(100, m.CompensacaoTreinoPct)),
                 })
        {
            var chavePessoa = AbrirBanco.ChaveDaPessoa(chave, db.PessoaAtual);
            var cfg = await db.Configuracoes.FindAsync([chavePessoa], ct);
            var texto = Math.Max(0, valor).ToString(CultureInfo.InvariantCulture);
            if (cfg is null) db.Configuracoes.Add(new Configuracao { Chave = chavePessoa, Valor = texto });
            else cfg.Valor = texto;
        }
        await db.SaveChangesAsync(ct);
    }

    // =================================================================
    //  Corpo
    // =================================================================

    public async Task<HistoricoPeso> ObterPesosAsync(int dias, DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var desde = hoje.AddDays(-dias);
        var pesos = await db.Pesos.AsNoTracking().Where(p => p.Data >= desde).OrderBy(p => p.Data).ToListAsync(ct);
        return new HistoricoPeso(pesos.Select(p => new PontoPeso(p.Id, p.Data, p.Kg)).ToList());
    }

    public async Task<ResultadoRegistro> RegistrarPesoAsync(DateOnly dia, decimal kg, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        return await RegistrarPeso(db, dia, kg, ct);
    }

    /// <summary>Usado também pela captura ("peso 82,5").</summary>
    internal static async Task<ResultadoRegistro> RegistrarPeso(FavoaDbContext db, DateOnly dia, decimal kg, CancellationToken ct)
    {
        if (kg is < 20 or > 400) return new(false, "Peso fora do esperado. Ex.: peso 82,5");
        var existente = await db.Pesos.FirstOrDefaultAsync(p => p.Data == dia, ct);
        var anterior = existente?.Kg;
        if (existente is null)
        {
            existente = new RegistroPeso { Data = dia, Kg = kg };
            db.Pesos.Add(existente);
        }
        else existente.Kg = kg;
        await db.SaveChangesAsync(ct);

        return new(true, $"Peso: {kg.ToString("0.0", PtBr)} kg",
            new(Core.Edicao.TipoReferencia.Peso, existente.Id, anterior?.ToString(CultureInfo.InvariantCulture)));
    }

    public async Task<ResultadoRegistro> ExcluirPesoAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var p = await db.Pesos.FindAsync([id], ct);
        if (p is null) return NaoEncontrado();
        db.Pesos.Remove(p);
        await db.SaveChangesAsync(ct);
        return new(true, "Peso excluído.");
    }

    private const string ChaveAltura = "altura_cm";

    public async Task<PainelMedidas> ObterPainelMedidasAsync(DateOnly hoje, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var chave = AbrirBanco.ChaveDaPessoa(ChaveAltura, db.PessoaAtual);
        var texto = await db.Configuracoes.Where(c => c.Chave == chave).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        int? altura = int.TryParse(texto, out var a) && a is >= CalculoCorpo.AlturaMinCm and <= CalculoCorpo.AlturaMaxCm ? a : null;

        var pesos = await db.Pesos.AsNoTracking().OrderBy(p => p.Data).ToListAsync(ct);
        var historico = new HistoricoPeso(pesos.Select(p => new PontoPeso(p.Id, p.Data, p.Kg)).ToList());

        var todas = await db.Medidas.AsNoTracking().OrderBy(m => m.Data).ToListAsync(ct);
        var medidas = new List<MedidaAtual>();
        foreach (var tipo in TiposMedida.Todos)
        {
            var pontos = todas.Where(m => m.Tipo == tipo).Select(m => new PontoMedida(m.Data, m.Cm)).ToList();
            if (pontos.Count == 0) continue;
            var atual = pontos[^1];
            decimal? variacao = pontos.Count > 1 ? atual.Cm - pontos[^2].Cm : null;
            medidas.Add(new MedidaAtual(tipo, atual.Cm, atual.Data, variacao, pontos.TakeLast(30).ToList()));
        }
        return new PainelMedidas(altura, historico, medidas);
    }

    public async Task<ResultadoRegistro> SalvarAlturaAsync(int cm, CancellationToken ct = default)
    {
        if (cm is < CalculoCorpo.AlturaMinCm or > CalculoCorpo.AlturaMaxCm) return new(false, "Altura fora do esperado. Use centímetros, ex.: 172.");
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var chave = AbrirBanco.ChaveDaPessoa(ChaveAltura, db.PessoaAtual);
        var cfg = await db.Configuracoes.FindAsync([chave], ct);
        var texto = cm.ToString(CultureInfo.InvariantCulture);
        if (cfg is null) db.Configuracoes.Add(new Configuracao { Chave = chave, Valor = texto });
        else cfg.Valor = texto;
        await db.SaveChangesAsync(ct);
        return new(true, $"Altura salva: {cm} cm.");
    }

    public async Task<ResultadoRegistro> RegistrarMedidaAsync(DateOnly dia, string tipo, decimal cm, CancellationToken ct = default)
    {
        var canonico = TiposMedida.Canonico(tipo);
        if (canonico is null) return new(false, "Medida desconhecida. Use: " + string.Join(", ", TiposMedida.Todos).ToLowerInvariant() + ".");
        if (cm is < 5 or > 300) return new(false, "Medida fora do esperado. Use centímetros, ex.: 84,5.");
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var existente = await db.Medidas.FirstOrDefaultAsync(m => m.Tipo == canonico && m.Data == dia, ct);
        if (existente is null) db.Medidas.Add(new MedidaCorporal { Tipo = canonico, Data = dia, Cm = cm });
        else existente.Cm = cm;
        await db.SaveChangesAsync(ct);
        return new(true, $"{canonico} {cm.ToString("0.#", new CultureInfo("pt-BR"))} cm registrada.");
    }

    // =================================================================
    //  Apoio (também usado pelo FavoaServico na captura)
    // =================================================================

    internal static async Task<Exercicio> ObterOuCriarExercicio(FavoaDbContext db, string nome, TipoExercicio? tipo, CancellationToken ct)
    {
        nome = Texto.Capitalizar(nome);
        var alvo = Texto.Normalizar(nome);
        var local = db.Exercicios.Local.FirstOrDefault(e => Texto.Normalizar(e.Nome) == alvo);
        if (local is not null) return local;

        var todos = await db.Exercicios.ToListAsync(ct);
        var existente = todos.FirstOrDefault(e => Texto.Normalizar(e.Nome) == alvo);
        if (existente is not null) return existente;

        // "supino" quando só existe "Supino reto": é ele. Com "reto" e "inclinado", não dá para saber: cria um novo.
        var comecam = todos.Where(e => Texto.Normalizar(e.Nome).StartsWith(alvo + " ")).ToList();
        if (comecam.Count == 1) return comecam[0];

        var grupo = GruposMusculares.Inferir(nome);
        var novo = new Exercicio { Nome = nome, Grupo = grupo, Tipo = tipo ?? GruposMusculares.TipoPorGrupo(grupo) };
        db.Exercicios.Add(novo);
        return novo;
    }

    internal static async Task<MetasFitness> LerMetas(FavoaDbContext db, CancellationToken ct)
    {
        // Metas são de cada pessoa ("meta_kcal@{pessoa}").
        var chaves = new[] { MetasFitness.ChaveKcal, MetasFitness.ChaveProteina, MetasFitness.ChaveCarboidrato, MetasFitness.ChaveGordura, MetasFitness.ChaveTreinosSemana, MetasFitness.ChaveCompensacao }
            .Select(c => AbrirBanco.ChaveDaPessoa(c, db.PessoaAtual)).ToArray();
        var valores = await db.Configuracoes.Where(c => chaves.Contains(c.Chave)).ToDictionaryAsync(c => c.Chave, c => c.Valor, ct);
        int Ler(string chave, int padrao) => valores.TryGetValue(AbrirBanco.ChaveDaPessoa(chave, db.PessoaAtual), out var v) && int.TryParse(v, out var n) ? n : padrao;

        var p = MetasFitness.Padrao;
        return new MetasFitness(
            Ler(MetasFitness.ChaveKcal, p.Kcal), Ler(MetasFitness.ChaveProteina, p.ProteinaG),
            Ler(MetasFitness.ChaveCarboidrato, p.CarboidratoG), Ler(MetasFitness.ChaveGordura, p.GorduraG),
            Ler(MetasFitness.ChaveTreinosSemana, p.TreinosSemana),
            Math.Clamp(Ler(MetasFitness.ChaveCompensacao, p.CompensacaoTreinoPct), 0, 100));
    }

    private static async Task<List<FichaResumo>> CarregarFichas(FavoaDbContext db, CancellationToken ct) =>
        (await db.Fichas.AsNoTracking().Where(f => f.Ativa)
            .Include(f => f.Itens).ThenInclude(i => i.Exercicio)
            .ToListAsync(ct))
        .OrderBy(f => f.Ordem).ThenBy(f => f.Nome)
        .Select(f => new FichaResumo(f.Id, f.Nome, f.Ordem, f.Itens.OrderBy(i => i.Ordem)
            .Select(i => new ItemFichaResumo(i.ExercicioId, i.Exercicio!.Nome, i.Exercicio.Tipo, i.Series, i.Repeticoes, i.CargaKg, i.Minutos))
            .ToList()))
        .ToList();

    private static async Task<List<ExercicioResumo>> ResumirExercicios(FavoaDbContext db, CancellationToken ct)
    {
        var exercicios = await db.Exercicios.AsNoTracking().OrderBy(e => e.Nome).ToListAsync(ct);
        var feitos = await db.ExerciciosRealizados.AsNoTracking().Include(e => e.Sessao).ToListAsync(ct);
        var porExercicio = feitos.GroupBy(f => f.ExercicioId).ToDictionary(g => g.Key, g => g.ToList());

        return exercicios.Select(e =>
        {
            var lista = porExercicio.GetValueOrDefault(e.Id) ?? [];
            return new ExercicioResumo(e.Id, e.Nome, e.Grupo, e.Tipo, e.Ativo,
                lista.Select(f => f.Sessao!.Data).Distinct().Count(),
                lista.Count == 0 ? null : lista.Max(f => f.Sessao!.Data),
                lista.Where(f => f.CargaKg > 0).Select(f => f.CargaKg).DefaultIfEmpty(null).Max());
        }).ToList();
    }

    private static SessaoResumo Resumir(SessaoTreino s, IReadOnlyList<(DateOnly Data, decimal Kg)> pesos) => new(
        s.Id,
        s.Data,
        s.Ficha?.Nome ?? (s.Exercicios.All(e => e.Exercicio?.Tipo == TipoExercicio.Cardio) ? "Cardio" : "Treino livre"),
        s.Exercicios.OrderBy(e => e.Ordem).Select(e => $"{e.Exercicio?.Nome} · {Descrever(e)}").ToList(),
        s.Exercicios.Sum(e => e.Volume),
        s.Exercicios.Sum(e => e.Minutos ?? 0))
    {
        Kcal = GastoDaSessao(s, pesos).Kcal,
    };

    /// <summary>Gasto estimado de uma sessão (exercícios e o exercício de cada linha precisam estar carregados).</summary>
    internal static GastoSessao GastoDaSessao(SessaoTreino s, IReadOnlyList<(DateOnly Data, decimal Kg)> pesos)
    {
        var itens = s.Exercicios.OrderBy(e => e.Ordem)
            .Select(e => new ItemGasto(GastoCalorico.Classificar(e.Exercicio?.Nome ?? "", e.Exercicio?.Tipo ?? TipoExercicio.Cardio), e.Minutos, e.DistanciaKm, e.Series, e.Intensidade))
            .ToList();
        return GastoCalorico.Calcular(itens, s.MinutosForca, s.IntensidadeForca, GastoCalorico.PesoEm(pesos, s.Data));
    }

    internal static async Task<List<(DateOnly Data, decimal Kg)>> PesosAsync(FavoaDbContext db, CancellationToken ct) =>
        (await db.Pesos.AsNoTracking().OrderBy(p => p.Data).Select(p => new { p.Data, p.Kg }).ToListAsync(ct)).Select(p => (p.Data, p.Kg)).ToList();

    /// <summary>Gasto bruto estimado dos treinos de cada dia do intervalo (só dias com treino e gasto calculável).</summary>
    internal static async Task<Dictionary<DateOnly, int>> GastoPorDiaAsync(FavoaDbContext db, DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var sessoes = await db.Sessoes.AsNoTracking().Include(x => x.Exercicios).ThenInclude(e => e.Exercicio)
            .Where(x => x.Data >= de && x.Data <= ate).ToListAsync(ct);
        if (sessoes.Count == 0) return [];
        var pesos = await PesosAsync(db, ct);
        return sessoes.GroupBy(x => x.Data)
            .Select(g => (g.Key, Kcal: g.Sum(x => GastoDaSessao(x, pesos).Kcal ?? 0)))
            .Where(x => x.Kcal > 0)
            .ToDictionary(x => x.Key, x => x.Kcal);
    }

    private static AlimentoResumo Resumir(Alimento a, IReadOnlyList<PorcaoAlimento>? tabela = null) =>
        new(a.Id, a.Nome, a.Grupo, a.Kcal, a.Proteina, a.Carboidrato, a.Gordura, a.Personalizado, a.NomePorcao, a.GramasPorcao)
        {
            Fonte = FontesAlimento.Rotulo(a),
            Marca = a.Fonte == FontesAlimento.OpenFoodFacts && a.Grupo != "Produtos" ? a.Grupo : null,
            Porcoes = Porcoes(a, tabela ?? []),
        };

    /// <summary>
    /// Medidas que a busca oferece: a porção do cadastro próprio (ou do rótulo, no Open Food Facts) primeiro e já escolhida,
    /// depois as da tabela do IBGE, da mais usada para a menos (unidade, fatia, colheres...).
    /// </summary>
    internal static List<PorcaoResumo> Porcoes(Alimento a, IReadOnlyList<PorcaoAlimento> tabela)
    {
        var lista = new List<PorcaoResumo>();
        if (a.GramasPorcao is > 0)
            lista.Add(new(string.IsNullOrWhiteSpace(a.NomePorcao) ? "porção" : a.NomePorcao, a.GramasPorcao.Value, true));
        var temPadrao = lista.Count > 0;
        // A medida da casa vale no lugar da do catálogo com o mesmo nome.
        var validas = tabela.GroupBy(p => p.Nome, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.Origem == OrigemPorcao.Meu).First())
            .OrderBy(p => OrdemMedida(p.Nome)).ThenBy(p => p.Gramas);
        foreach (var p in validas)
        {
            if (lista.Any(x => string.Equals(x.Nome, p.Nome, StringComparison.OrdinalIgnoreCase))) continue;
            lista.Add(new(p.Nome, p.Gramas, p.Padrao && !temPadrao, Estimada: p.Origem == OrigemPorcao.Ibge));
        }
        return lista;
    }

    private static readonly string[] OrdemDasMedidas =
        ["Unidade", "Unidade pequena", "Fatia", "Pedaço", "Metade", "Colher de sopa", "Colher de sobremesa", "Colher de chá", "Colher de arroz/servir",
         "Concha", "Copo americano", "Xícara de chá", "Porção"];

    private static int OrdemMedida(string nome) { var i = Array.IndexOf(OrdemDasMedidas, nome); return i < 0 ? OrdemDasMedidas.Length : i; }

    private static async Task<List<AlimentoResumo>> ResumirComPorcoesAsync(FavoaDbContext db, IReadOnlyList<Alimento> alimentos, CancellationToken ct)
    {
        var ids = alimentos.Select(a => a.Id).ToList();
        var porcoes = (await db.Porcoes.AsNoTracking().Where(p => ids.Contains(p.AlimentoId)).ToListAsync(ct))
            .ToLookup(p => p.AlimentoId);
        // Como a pessoa lançou cada um da última vez: a busca já abre na mesma unidade e quantidade.
        var ultimos = (await db.Consumos.AsNoTracking().Where(c => ids.Contains(c.AlimentoId) && c.Unidade != null).ToListAsync(ct))
            .GroupBy(c => c.AlimentoId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Data).ThenByDescending(c => c.CriadoEm).First());
        return alimentos.Select(a => Resumir(a, porcoes[a.Id].ToList()) with { }).Select(r => ultimos.TryGetValue(r.Id, out var u)
            ? r with { UltimaUnidade = u.Unidade, UltimaQuantidade = u.Quantidade } : r).ToList();
    }

    public async Task<IReadOnlyList<PorcaoEdicao>> ObterPorcoesAsync(Guid alimentoId, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var todas = await db.Porcoes.AsNoTracking().Where(p => p.AlimentoId == alimentoId).ToListAsync(ct);
        return todas.GroupBy(p => p.Nome, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Valida: g.OrderByDescending(p => p.Origem == OrigemPorcao.Meu).First(), Ajusta: g.Count() > 1))
            .OrderBy(x => OrdemMedida(x.Valida.Nome)).ThenBy(x => x.Valida.Gramas)
            .Select(x => new PorcaoEdicao(x.Valida.Id, x.Valida.Nome, x.Valida.Gramas, x.Valida.Origem == OrigemPorcao.Meu, x.Ajusta))
            .ToList();
    }

    public async Task<ResultadoRegistro> SalvarPorcaoAsync(Guid alimentoId, string nome, double gramas, CancellationToken ct = default)
    {
        nome = (nome ?? "").Trim();
        if (nome.Length == 0) return new(false, "Dê um nome para a medida (fatia, colher, pote...).");
        if (nome.Length > 60) nome = nome[..60];
        if (!double.IsFinite(gramas) || gramas <= 0 || gramas > 5000) return new(false, "Os gramas da medida precisam estar entre 0 e 5.000.");
        gramas = Math.Round(gramas, 1);

        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var alimento = await db.Alimentos.FindAsync([alimentoId], ct);
        if (alimento is null) return NaoEncontrado();

        var existentes = await db.Porcoes.Where(p => p.AlimentoId == alimentoId).ToListAsync(ct);
        var daCasa = existentes.FirstOrDefault(p => p.Origem == OrigemPorcao.Meu && string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase));
        if (daCasa is not null) daCasa.Gramas = gramas;
        else
        {
            var catalogo = existentes.FirstOrDefault(p => string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase));
            db.Porcoes.Add(new PorcaoAlimento
            {
                AlimentoId = alimentoId, Nome = catalogo?.Nome ?? nome, Gramas = gramas, Origem = OrigemPorcao.Meu,
                Padrao = catalogo?.Padrao ?? false,
            });
        }
        await db.SaveChangesAsync(ct);
        return new(true, $"1 {nome.ToLower(PtBr)} de {alimento.Nome} = {gramas.ToString("0.#", PtBr)} g.");
    }

    public async Task<ResultadoRegistro> RemoverPorcaoAsync(Guid porcaoId, CancellationToken ct = default)
    {
        await using var db = await fabrica.AbrirAsync(pessoa, ct);
        var p = await db.Porcoes.FindAsync([porcaoId], ct);
        if (p is null) return NaoEncontrado();
        if (p.Origem != OrigemPorcao.Meu) return new(false, "Essa medida é do catálogo do IBGE. Ajuste o valor para a sua casa em vez de apagar.");
        db.Porcoes.Remove(p);
        await db.SaveChangesAsync(ct);
        return new(true, "Medida removida.");
    }

    /// <summary>"3×10 · 40 kg" ou "5 km · 30 min".</summary>
    internal static string Descrever(ExercicioRealizado e)
    {
        var partes = new List<string>();
        if (e.Series is { } s && e.Repeticoes is { } r) partes.Add($"{s}×{r}");
        else if (e.Series is { } so) partes.Add($"{so} séries");
        if (e.CargaKg is { } c && c > 0) partes.Add($"{c.ToString("0.#", PtBr)} kg");
        if (e.DistanciaKm is { } d && d > 0) partes.Add($"{d.ToString("0.#", PtBr)} km");
        if (e.Minutos is { } m && m > 0) partes.Add($"{m} min");
        if (e.Intensidade is { } i) partes.Add(GastoCalorico.Rotulo(i));
        return partes.Count == 0 ? "feito" : string.Join(" · ", partes);
    }

    private static string Plural(int n, string um, string varios) => $"{n} {(n == 1 ? um : varios)}";

    private static ResultadoRegistro NaoEncontrado() => new(false, "Esse registro não existe mais.");
}
