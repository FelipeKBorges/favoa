using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Favoa.Core;
using Favoa.Core.Captura;
using Favoa.Core.Financas;
using Favoa.Core.Fitness;
using Favoa.Data;
using Microsoft.EntityFrameworkCore;

namespace Favoa.Mcp;

/// <summary>
/// As ações disponíveis no Fávoa pelo MCP. Respostas em texto curto (não JSON) para gastar pouco
/// do limite da conversa. Ids aparecem com 8 caracteres e são aceitos assim de volta.
/// </summary>
public sealed class Ferramentas(IFavoaServico favoa, IFitnessServico fitness, IDbContextFactory<FavoaDbContext> fabrica,
    Favoa.Core.Pessoas.PessoaFixa pessoa, Func<DateOnly>? relogio = null)
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    private DateOnly Hoje => relogio?.Invoke() ?? DateOnly.FromDateTime(DateTime.Now);

    private sealed record Definicao(string Nome, string Descricao, JsonObject Esquema, bool Grava, Func<JsonObject, CancellationToken, Task<string>> Executar);

    private IReadOnlyList<Definicao>? _definicoes;
    private IReadOnlyList<Definicao> Definicoes => _definicoes ??= Montar();

    public JsonArray Listar() => new(Definicoes.Select(d => (JsonNode)new JsonObject
    {
        ["name"] = d.Nome,
        ["description"] = d.Descricao,
        ["inputSchema"] = ComPessoa(d.Esquema),
        ["annotations"] = new JsonObject { ["readOnlyHint"] = !d.Grava, ["destructiveHint"] = false },
    }).ToArray());

    public async Task<(string Texto, bool Erro)> ExecutarAsync(string nome, JsonObject args, CancellationToken ct)
    {
        var d = Definicoes.FirstOrDefault(x => x.Nome == nome);
        if (d is null) return ($"Ferramenta desconhecida: {nome}", true);
        try
        {
            // Treino, dieta, peso, hábitos e água são de cada pessoa; finanças e tarefas são da casa.
            var alvo = await ResolverPessoaAsync(args["pessoa"]?.GetValue<string>(), ct);
            pessoa.Id = alvo.Id;
            pessoa.FamiliaId = alvo.FamiliaId; // o que é gravado pelo MCP nasce na família da pessoa
            return (await d.Executar(args, ct), false);
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return (e.Message, true);
        }
    }

    // =================================================================
    //  Catálogo
    // =================================================================

    private List<Definicao> Montar() =>
    [
        new("resumo_do_dia",
            "Resumo de um dia no Fávoa: finanças do mês, tarefas, hábitos, água, calorias, treino da semana e peso.",
            Esquema(("data", Texto("Data AAAA-MM-DD; padrão: hoje"), false)),
            false, ResumoDoDia),

        new("registrar",
            "Registra uma frase em português como na captura do app (Ctrl K). Ex.: 'uber 23', 'gastei 32 no mercado com o nubank', " +
            "'tarefa pagar luz sexta', 'supino 3x10 40kg', 'corri 5 km em 30 min', 'comi 150g de arroz no almoço', 'peso 82,5', " +
            "'bebi 500 ml', 'fiz ler 20 min'. Grava: use só após o usuário confirmar.",
            Esquema(("frase", Texto("A frase a registrar"), true)),
            true, Registrar),

        new("buscar_alimentos",
            "Busca na base de alimentos (Tabela TACO + cadastrados pelo usuário). Valores por 100 g. Devolve id curto para usar no plano.",
            Esquema(("texto", Texto("Palavras do alimento, ex.: 'frango peito grelhado'"), true), ("limite", Inteiro("Máximo de resultados (padrão 8, máx. 20)"), false)),
            false, BuscarAlimentos),

        new("dieta_do_dia",
            "Plano alimentar por refeição, o que foi comido no dia, totais e metas (kcal, proteína, carboidrato, gordura, água).",
            Esquema(("data", Texto("Data AAAA-MM-DD; padrão: hoje"), false)),
            false, DietaDoDia),

        new("definir_plano_refeicao",
            "Substitui o plano de UMA refeição pelos itens informados (lista vazia apaga o plano dela). " +
            "Refeições: cafe_da_manha, lanche_manha, almoco, lanche_tarde, jantar, ceia. Grava: use só após o usuário aprovar o plano.",
            Esquema(
                ("refeicao", Enum("Refeição", "cafe_da_manha", "lanche_manha", "almoco", "lanche_tarde", "jantar", "ceia"), true),
                ("itens", Lista("Itens do plano", Esquema(("alimento_id", Texto("Id curto vindo de buscar_alimentos"), true), ("gramas", Inteiro("Quantidade em gramas"), true))), true)),
            true, DefinirPlano),

        new("cadastrar_alimento",
            "Cadastra um alimento que não está na base (valores por 100 g, como no rótulo). Porção caseira opcional (ex.: scoop = 30 g). Grava.",
            Esquema(
                ("nome", Texto("Nome"), true), ("kcal", Numero("kcal por 100 g"), true), ("proteina", Numero("g por 100 g"), true),
                ("carboidrato", Numero("g por 100 g"), true), ("gordura", Numero("g por 100 g"), true),
                ("porcao_nome", Texto("Ex.: unidade, scoop, fatia"), false), ("porcao_gramas", Inteiro("Gramas da porção"), false)),
            true, CadastrarAlimento),

        new("definir_metas",
            "Altera metas diárias de dieta e a meta de treinos por semana. Só os campos informados mudam; 0 = sem meta. Grava.",
            Esquema(("kcal", Inteiro("kcal por dia"), false), ("proteina_g", Inteiro("g por dia"), false), ("carboidrato_g", Inteiro("g por dia"), false),
                    ("gordura_g", Inteiro("g por dia"), false), ("treinos_semana", Inteiro("treinos por semana"), false)),
            true, DefinirMetas),

        new("treinos",
            "Fichas de treino (com metas), últimos treinos e exercícios com recorde de carga.",
            Esquema(("ultimos", Inteiro("Quantos treinos recentes listar (padrão 8)"), false)),
            false, Treinos),

        new("evolucao_exercicio",
            "Histórico e evolução de carga (ou distância/tempo no cardio) de um exercício.",
            Esquema(("exercicio_id", Texto("Id curto vindo de treinos"), true)),
            false, Evolucao),

        new("salvar_ficha",
            "Cria uma ficha de treino, ou substitui uma existente se ficha_id vier. Exercícios pelo nome (novos são criados). Grava.",
            Esquema(
                ("nome", Texto("Ex.: Treino A — peito e tríceps"), true),
                ("ficha_id", Texto("Id curto para editar uma ficha existente"), false),
                ("itens", Lista("Exercícios na ordem", Esquema(
                    ("exercicio", Texto("Nome"), true), ("tipo", Enum("Tipo", "forca", "cardio"), false),
                    ("series", Inteiro("Séries"), false), ("repeticoes", Inteiro("Repetições"), false),
                    ("carga_kg", Numero("Carga em kg"), false), ("minutos", Inteiro("Minutos (cardio)"), false))), true)),
            true, SalvarFicha),

        new("historico_peso",
            "Pesos registrados (um por dia) e variação em ~30 dias.",
            Esquema(("dias", Inteiro("Janela em dias (padrão 90)"), false)),
            false, HistoricoPeso),

        new("financas_do_mes",
            "Receitas, despesas, saldo, orçamento e gastos por categoria de um mês.",
            Esquema(("ano", Inteiro("Ano; padrão: atual"), false), ("mes", Inteiro("Mês 1-12; padrão: atual"), false)),
            false, FinancasDoMes),

        new("tarefas",
            "Tarefas pendentes (atrasadas, hoje, próximas, sem prazo) e feitas nos últimos 7 dias.",
            Esquema(),
            false, Tarefas),
    ];

    // =================================================================
    //  Leitura
    // =================================================================

    private async Task<string> ResumoDoDia(JsonObject a, CancellationToken ct)
    {
        var dia = Data(a, "data");
        var p = await favoa.ObterPainelAsync(dia, ct);
        var r = p.Resumo;
        var sb = new StringBuilder($"{dia:dd/MM/yyyy} · colmeia {p.Centro.Percentual}%\n");
        foreach (var c in p.Celulas.Where(c => c.Chave != Core.Painel.CelulaColmeia.Hoje && c.Estado != Core.Painel.EstadoCelula.EmBreve))
            sb.AppendLine($"- {c.Rotulo}: {c.Valor} ({c.Detalhe})");
        sb.AppendLine($"Mês: despesas {Moeda(r.DespesasDoMesCentavos)}, receitas {Moeda(r.ReceitasDoMesCentavos)}" +
                      (r.OrcamentoMensalCentavos > 0 ? $", orçamento {Moeda(r.OrcamentoMensalCentavos)}" : ""));
        if (p.ProximasTarefas.Count > 0)
            sb.AppendLine("Próximas tarefas: " + string.Join("; ", p.ProximasTarefas.Select(t => t.Titulo + (t.Prazo is { } d ? $" ({d:dd/MM}{(t.Atrasada ? ", atrasada" : "")})" : ""))));
        if (p.HabitosPendentes.Count > 0)
            sb.AppendLine("Hábitos que faltam: " + string.Join(", ", p.HabitosPendentes.Select(h => h.Nome)));
        var peso = (await fitness.ObterPesosAsync(30, dia, ct)).Atual;
        if (peso is not null) sb.AppendLine($"Peso: {Kg(peso.Kg)} em {peso.Data:dd/MM}");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> BuscarAlimentos(JsonObject a, CancellationToken ct)
    {
        var texto = Obrigatorio(a, "texto");
        var limite = Math.Clamp(Int(a, "limite") ?? 8, 1, 20);
        var itens = await fitness.BuscarAlimentosAsync(texto, limite, ct);
        if (itens.Count == 0) return $"Nada encontrado para \"{texto}\". Tente outras palavras ou cadastrar_alimento.";

        var sb = new StringBuilder("id | alimento | kcal | P | C | G (por 100 g)\n");
        foreach (var x in itens)
            sb.AppendLine($"{Curto(x.Id)} | {x.Nome}{(x.Personalizado ? " ★" : "")} | {x.Kcal:0} | {N1(x.Proteina)} | {N1(x.Carboidrato)} | {N1(x.Gordura)}" +
                          (x.Porcoes.Count > 0 ? " | " + string.Join("; ", x.Porcoes.Take(4).Select(p => $"1 {p.Nome.ToLowerInvariant()} = {p.Gramas:0.#} g")) : ""));
        return sb.ToString().TrimEnd();
    }

    private async Task<string> DietaDoDia(JsonObject a, CancellationToken ct)
    {
        var dia = await fitness.ObterDiaDietaAsync(Data(a, "data"), ct);
        var m = dia.Metas;
        var sb = new StringBuilder();
        sb.AppendLine($"{dia.Data:dd/MM/yyyy} · comido: {Macros(dia.Total)} · plano: {Macros(dia.TotalPlano)}");
        sb.AppendLine(m.Kcal + m.ProteinaG + m.CarboidratoG + m.GorduraG == 0
            ? "Metas: não definidas"
            : $"Metas: {m.Kcal} kcal · P {m.ProteinaG} g · C {m.CarboidratoG} g · G {m.GorduraG} g");
        sb.AppendLine($"Água: {dia.AguaMl} de {dia.MetaAguaMl} ml");
        foreach (var r in dia.Refeicoes.Where(r => r.Plano.Count + r.Consumido.Count > 0))
        {
            sb.AppendLine($"\n{r.Nome} ({Chave(r.Refeicao)})");
            if (r.Plano.Count > 0) sb.AppendLine("  plano: " + string.Join("; ", r.Plano.Select(i => $"{i.Alimento} {i.Gramas} g [{Curto(i.AlimentoId)}]")) + $" = {r.TotalPlano.Kcal:0} kcal");
            if (r.Consumido.Count > 0) sb.AppendLine("  comido: " + string.Join("; ", r.Consumido.Select(i => $"{i.Alimento} {i.Gramas} g")) + $" = {r.TotalConsumido.Kcal:0} kcal");
        }
        return sb.ToString().TrimEnd();
    }

    private async Task<string> Treinos(JsonObject a, CancellationToken ct)
    {
        var p = await fitness.ObterPainelTreinoAsync(Hoje, ct);
        var sb = new StringBuilder($"{p.TreinosUltimos7Dias} de {p.MetaSemana} treinos nos últimos 7 dias. Próxima ficha: {p.ProximaFicha?.Nome ?? "nenhuma"}\n");
        sb.AppendLine(p.Fichas.Count == 0 ? "Fichas: nenhuma" : "Fichas:");
        foreach (var f in p.Fichas)
            sb.AppendLine($"- [{Curto(f.Id)}] {f.Nome}: " + string.Join("; ", f.Itens.Select(i => $"{i.Exercicio} {MetaItem(i)}")));
        var ultimos = Math.Clamp(Int(a, "ultimos") ?? 8, 0, 30);
        if (p.Recentes.Count > 0 && ultimos > 0)
        {
            sb.AppendLine("Últimos treinos:");
            foreach (var s in p.Recentes.Take(ultimos))
                sb.AppendLine($"- {s.Data:dd/MM} {s.Titulo}: {string.Join("; ", s.Itens)}");
        }
        if (p.Exercicios.Count > 0)
            sb.AppendLine("Exercícios: " + string.Join("; ", p.Exercicios.Select(e =>
                $"[{Curto(e.Id)}] {e.Nome} ({e.Grupo}{(e.MelhorCargaKg is { } c ? $", recorde {Kg(c)}" : "")}, {e.VezesFeito}x)")));
        return sb.ToString().TrimEnd();
    }

    private async Task<string> Evolucao(JsonObject a, CancellationToken ct)
    {
        var id = await ResolverId(Obrigatorio(a, "exercicio_id"), db => db.Exercicios.Select(e => e.Id), "exercício", ct);
        var ev = await fitness.ObterEvolucaoAsync(id, ct) ?? throw new KeyNotFoundException("Exercício não encontrado.");
        var unidade = ev.Exercicio.Tipo == TipoExercicio.Forca ? " kg" : "";
        return $"{ev.Exercicio.Nome} ({ev.Exercicio.Grupo})\n" +
               (ev.Pontos.Count == 0 ? "Sem cargas registradas." : "Evolução: " + string.Join(" → ", ev.Pontos.Select(p => $"{p.Data:dd/MM} {p.Valor.ToString("0.#", PtBr)}{unidade}"))) +
               (ev.Historico.Count > 0 ? "\nHistórico: " + string.Join("; ", ev.Historico.Take(10)) : "");
    }

    private async Task<string> HistoricoPeso(JsonObject a, CancellationToken ct)
    {
        var h = await fitness.ObterPesosAsync(Math.Clamp(Int(a, "dias") ?? 90, 7, 3650), Hoje, ct);
        if (h.Pontos.Count == 0) return "Nenhum peso registrado.";
        return $"Atual: {Kg(h.Atual!.Kg)} em {h.Atual.Data:dd/MM/yyyy}" +
               (h.Variacao30Dias is { } v ? $" · variação ~30 dias: {(v > 0 ? "+" : "")}{v.ToString("0.0", PtBr)} kg" : "") +
               "\n" + string.Join("; ", h.Pontos.Select(p => $"{p.Data:dd/MM} {p.Kg.ToString("0.0", PtBr)}"));
    }

    private async Task<string> FinancasDoMes(JsonObject a, CancellationToken ct)
    {
        var ano = Int(a, "ano") ?? Hoje.Year;
        var mes = Int(a, "mes") ?? Hoje.Month;
        if (mes is < 1 or > 12) throw new ArgumentException("mes deve ser de 1 a 12.");
        var e = await favoa.ObterExtratoAsync(new FiltroExtrato(ano, mes), Hoje, ct);
        var sb = new StringBuilder($"{mes:00}/{ano}: receitas {Moeda(e.ReceitasCentavos)} · despesas {Moeda(e.DespesasCentavos)} · saldo {Moeda(e.SaldoCentavos)} · {e.QuantidadeLancamentos} lançamentos\n");
        if (e.Ritmo is { } r)
            sb.AppendLine($"Orçamento: {Moeda(r.GastoCentavos)} de {Moeda(r.OrcamentoCentavos)} ({r.Situacao})" +
                          (r.PorDiaCentavos is { } pd ? $", dá {Moeda(pd)}/dia nos {r.DiasRestantes} dias restantes" : ""));
        if (e.DespesasMesAnteriorCentavos > 0) sb.AppendLine($"Mês anterior (mesmo período): {Moeda(e.DespesasMesAnteriorCentavos)}");
        if (e.DespesasPorCategoria.Count > 0)
            sb.AppendLine("Por categoria: " + string.Join("; ", e.DespesasPorCategoria.Select(f => $"{f.Nome} {Moeda(f.Centavos)} ({f.Fracao:P0})")));
        return sb.ToString().TrimEnd();
    }

    private async Task<string> Tarefas(JsonObject a, CancellationToken ct)
    {
        var q = await favoa.ObterQuadroTarefasAsync(Hoje, ct);
        string Lista(IEnumerable<Core.Rotina.TarefaItem> t) => string.Join("; ", t.Select(x => x.Titulo + (x.Prazo is { } d ? $" ({d:dd/MM})" : "")));
        var sb = new StringBuilder($"{q.Pendentes} pendentes · {q.FeitasUltimos7Dias} feitas nos últimos 7 dias\n");
        if (q.Atrasadas.Count > 0) sb.AppendLine("Atrasadas: " + Lista(q.Atrasadas));
        if (q.ParaHoje.Count > 0) sb.AppendLine("Hoje: " + Lista(q.ParaHoje));
        if (q.Proximas.Count > 0) sb.AppendLine("Próximas: " + Lista(q.Proximas));
        if (q.SemPrazo.Count > 0) sb.AppendLine("Sem prazo: " + Lista(q.SemPrazo));
        return sb.ToString().TrimEnd();
    }

    // =================================================================
    //  Escrita
    // =================================================================

    private async Task<string> Registrar(JsonObject a, CancellationToken ct)
    {
        var frase = Obrigatorio(a, "frase");
        var ctx = await favoa.ObterContextoAsync(Hoje, ct);
        var i = InterpretadorFrase.Interpretar(frase, ctx);
        if (!i.PodeSalvar) throw new ArgumentException(i.Aviso ?? "Não entendi a frase.");
        var r = await favoa.RegistrarAsync(i, ct);
        if (!r.Sucesso) throw new ArgumentException(r.Mensagem);
        return $"Registrado ({i.Modulo}): {r.Mensagem}";
    }

    private async Task<string> DefinirPlano(JsonObject a, CancellationToken ct)
    {
        var refeicao = LerRefeicao(Obrigatorio(a, "refeicao"));
        var itens = a["itens"] as JsonArray ?? throw new ArgumentException("Faltou 'itens' (pode ser lista vazia).");

        // Valida tudo antes de mexer no plano.
        var novos = new List<(Guid Alimento, int Gramas)>();
        foreach (var item in itens.OfType<JsonObject>())
        {
            var alimento = await ResolverId(Obrigatorio(item, "alimento_id"), db => db.Alimentos.Select(x => x.Id), "alimento", ct);
            var gramas = Int(item, "gramas") ?? throw new ArgumentException("Cada item precisa de 'gramas'.");
            if (gramas is <= 0 or > 3000) throw new ArgumentException($"Gramas fora do esperado: {gramas}.");
            novos.Add((alimento, gramas));
        }

        var atual = (await fitness.ObterDiaDietaAsync(Hoje, ct)).Refeicoes.Single(r => r.Refeicao == refeicao).Plano;
        foreach (var p in atual) await fitness.RemoverItemPlanoAsync(p.Id, ct);
        foreach (var (alimento, gramas) in novos) await fitness.AdicionarItemPlanoAsync(refeicao, alimento, gramas, ct);

        var depois = (await fitness.ObterDiaDietaAsync(Hoje, ct)).Refeicoes.Single(r => r.Refeicao == refeicao);
        return novos.Count == 0
            ? $"Plano de {depois.Nome} apagado."
            : $"Plano de {depois.Nome}: {string.Join("; ", depois.Plano.Select(i => $"{i.Alimento} {i.Gramas} g"))} = {Macros(depois.TotalPlano)}";
    }

    private async Task<string> CadastrarAlimento(JsonObject a, CancellationToken ct)
    {
        var r = await fitness.SalvarAlimentoAsync(new AlimentoEdicao(null, Obrigatorio(a, "nome"),
            Num(a, "kcal") ?? 0, Num(a, "proteina") ?? 0, Num(a, "carboidrato") ?? 0, Num(a, "gordura") ?? 0,
            Str(a, "porcao_nome"), Int(a, "porcao_gramas")), ct);
        if (!r.Sucesso) throw new ArgumentException(r.Mensagem);
        var criado = (await fitness.BuscarAlimentosAsync(Obrigatorio(a, "nome"), 1, ct)).FirstOrDefault();
        return criado is null ? r.Mensagem : $"{r.Mensagem} [id {Curto(criado.Id)}]";
    }

    private async Task<string> DefinirMetas(JsonObject a, CancellationToken ct)
    {
        var m = await fitness.ObterMetasAsync(ct);
        var nova = new MetasFitness(
            Int(a, "kcal") ?? m.Kcal, Int(a, "proteina_g") ?? m.ProteinaG, Int(a, "carboidrato_g") ?? m.CarboidratoG,
            Int(a, "gordura_g") ?? m.GorduraG, Int(a, "treinos_semana") ?? m.TreinosSemana, m.CompensacaoTreinoPct);
        if (nova.Kcal is < 0 or > 10000 || nova.TreinosSemana is < 0 or > 14) throw new ArgumentException("Metas fora do esperado.");
        await fitness.SalvarMetasAsync(nova, ct);
        return $"Metas: {nova.Kcal} kcal · P {nova.ProteinaG} g · C {nova.CarboidratoG} g · G {nova.GorduraG} g · {nova.TreinosSemana} treinos/semana";
    }

    private async Task<string> SalvarFicha(JsonObject a, CancellationToken ct)
    {
        Guid? id = Str(a, "ficha_id") is { } curto
            ? await ResolverId(curto, db => db.Fichas.Select(f => f.Id), "ficha", ct)
            : null;
        var itens = (a["itens"] as JsonArray ?? throw new ArgumentException("Faltou 'itens'."))
            .OfType<JsonObject>()
            .Select(i => new ItemFichaEdicao(
                Obrigatorio(i, "exercicio"),
                Str(i, "tipo") == "cardio" ? TipoExercicio.Cardio
                    : Str(i, "tipo") == "forca" ? TipoExercicio.Forca
                    : GruposMusculares.TipoPorGrupo(GruposMusculares.Inferir(Obrigatorio(i, "exercicio"))),
                Int(i, "series"), Int(i, "repeticoes"), Num(i, "carga_kg") is { } c ? (decimal)c : null, Int(i, "minutos")))
            .ToList();

        var r = await fitness.SalvarFichaAsync(new FichaEdicao(id, Obrigatorio(a, "nome"), itens), ct);
        if (!r.Sucesso) throw new ArgumentException(r.Mensagem);
        return r.Mensagem;
    }

    // =================================================================
    //  Apoio
    // =================================================================

    /// <summary>Aceita o id completo ou os 8 primeiros caracteres, desde que não haja ambiguidade.</summary>
    private async Task<Guid> ResolverId(string texto, Func<FavoaDbContext, IQueryable<Guid>> ids, string oQue, CancellationToken ct)
    {
        var alvo = texto.Trim().Trim('[', ']').Replace("-", "").ToLowerInvariant();
        if (alvo.Length < 6) throw new ArgumentException($"Id de {oQue} muito curto: '{texto}'.");
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var achados = (await ids(db).ToListAsync(ct)).Where(g => g.ToString("N").StartsWith(alvo)).Take(2).ToList();
        return achados.Count switch
        {
            1 => achados[0],
            0 => throw new KeyNotFoundException($"Não achei {oQue} com id '{texto}'."),
            _ => throw new ArgumentException($"Id '{texto}' é ambíguo; use mais caracteres."),
        };
    }

    private static string Curto(Guid id) => id.ToString("N")[..8];

    private static string Chave(Refeicao r) => r switch
    {
        Refeicao.CafeDaManha => "cafe_da_manha",
        Refeicao.LancheManha => "lanche_manha",
        Refeicao.Almoco => "almoco",
        Refeicao.LancheTarde => "lanche_tarde",
        Refeicao.Jantar => "jantar",
        _ => "ceia",
    };

    private static Refeicao LerRefeicao(string chave) =>
        Refeicoes.Todas.FirstOrDefault(r => Chave(r) == chave.Trim().ToLowerInvariant(), (Refeicao)(-1)) is var r && (int)r >= 0
            ? r
            : throw new ArgumentException($"Refeição inválida: '{chave}'. Use cafe_da_manha, lanche_manha, almoco, lanche_tarde, jantar ou ceia.");

    private DateOnly Data(JsonObject a, string nome) =>
        Str(a, nome) is { } t
            ? DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw new FormatException($"Data inválida: '{t}'. Use AAAA-MM-DD.")
            : Hoje;

    private static string Obrigatorio(JsonObject a, string nome) =>
        Str(a, nome) is { Length: > 0 } v ? v : throw new ArgumentException($"Faltou '{nome}'.");

    private static string? Str(JsonObject a, string nome) => a[nome] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    private static int? Int(JsonObject a, string nome) => a[nome] switch
    {
        JsonValue v when v.TryGetValue<int>(out var i) => i,
        JsonValue v when v.TryGetValue<double>(out var d) => (int)Math.Round(d),
        JsonValue v when v.TryGetValue<string>(out var s) && int.TryParse(s, out var i) => i,
        _ => null,
    };

    private static double? Num(JsonObject a, string nome) => a[nome] switch
    {
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<string>(out var s) && double.TryParse(s.Replace(',', '.'), CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    private static string Moeda(long centavos) => Core.Captura.Texto.FormatarMoeda(centavos);
    private static string Kg(decimal kg) => $"{kg.ToString("0.0", PtBr)} kg";
    private static string N1(double v) => v.ToString("0.#", PtBr);
    private static string Macros(Macros m) => $"{m.Kcal:0} kcal · P {m.Proteina:0} g · C {m.Carboidrato:0} g · G {m.Gordura:0} g";

    private static string MetaItem(ItemFichaResumo i)
    {
        var p = new List<string>();
        if (i.Series is { } s && i.Repeticoes is { } r) p.Add($"{s}x{r}");
        if (i.CargaKg is { } c && c > 0) p.Add(Kg(c));
        if (i.Minutos is { } m && m > 0) p.Add($"{m} min");
        return string.Join(" ", p);
    }

    // ---------- JSON Schema enxuto ----------

    /// <summary>Todas as ferramentas aceitam "pessoa" (nome). Sem ela, vale a primeira pessoa da casa.</summary>
    private static JsonNode ComPessoa(JsonObject esquema)
    {
        var copia = (JsonObject)esquema.DeepClone();
        ((JsonObject)copia["properties"]!)["pessoa"] = Texto("Nome da pessoa da casa (ex.: Eva). Treino, dieta, peso, hábitos e água são de cada pessoa; finanças e tarefas são da casa. Padrão: a primeira pessoa.");
        return copia;
    }

    private async Task<(Guid Id, Guid? FamiliaId)> ResolverPessoaAsync(string? nome, CancellationToken ct)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var pessoas = await db.Pessoas.AsNoTracking().OrderBy(p => p.Ordem).ThenBy(p => p.CriadoEm).ToListAsync(ct);
        if (pessoas.Count == 0) throw new InvalidOperationException("Nenhuma pessoa cadastrada no Fávoa.");
        if (string.IsNullOrWhiteSpace(nome)) return (pessoas[0].Id, pessoas[0].FamiliaId);
        var alvo = Favoa.Core.Captura.Texto.Normalizar(nome);
        var achada = (pessoas.FirstOrDefault(p => Favoa.Core.Captura.Texto.Normalizar(p.Nome) == alvo)
                ?? pessoas.FirstOrDefault(p => Favoa.Core.Captura.Texto.Normalizar(p.Nome).StartsWith(alvo) || alvo.StartsWith(Favoa.Core.Captura.Texto.Normalizar(p.Nome)))
                ?? throw new ArgumentException($"Não conheço '{nome}' na casa. Pessoas: {string.Join(", ", pessoas.Select(p => p.Nome))}."));
        return (achada.Id, achada.FamiliaId);
    }

    private static JsonObject Esquema(params (string Nome, JsonObject Tipo, bool Obrigatorio)[] campos)
    {
        var props = new JsonObject();
        foreach (var (nome, tipo, _) in campos) props[nome] = tipo;
        var esquema = new JsonObject { ["type"] = "object", ["properties"] = props };
        var obrigatorios = campos.Where(c => c.Obrigatorio).Select(c => (JsonNode)c.Nome).ToArray();
        if (obrigatorios.Length > 0) esquema["required"] = new JsonArray(obrigatorios);
        return esquema;
    }

    private static JsonObject Texto(string d) => new() { ["type"] = "string", ["description"] = d };
    private static JsonObject Inteiro(string d) => new() { ["type"] = "integer", ["description"] = d };
    private static JsonObject Numero(string d) => new() { ["type"] = "number", ["description"] = d };
    private static JsonObject Lista(string d, JsonObject item) => new() { ["type"] = "array", ["description"] = d, ["items"] = item };
    private static JsonObject Enum(string d, params string[] valores) =>
        new() { ["type"] = "string", ["description"] = d, ["enum"] = new JsonArray(valores.Select(v => (JsonNode)v).ToArray()) };
}
