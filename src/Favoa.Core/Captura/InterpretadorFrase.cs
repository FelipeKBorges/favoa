using System.Text.RegularExpressions;
using AvisoTarefa_ = Favoa.Core.Rotina.AvisoTarefa;

namespace Favoa.Core.Captura;

/// <summary>
/// Transforma uma frase em português numa proposta de registro, por regras e sem internet.
/// As regras ficam aqui, testadas, e crescem conforme surgem frases que o Fávoa não entende.
/// </summary>
public static partial class InterpretadorFrase
{
    public static Interpretacao Interpretar(string? frase, ContextoCaptura ctx)
    {
        // "#chile" no meio da frase liga o registro a um projeto; sai do texto antes de interpretar o resto.
        var projeto = RxProjeto().Match(frase ?? "") is { Success: true } mProj ? mProj.Groups["nome"].Value : null;
        var semTag = projeto is null ? frase : RxProjeto().Replace(frase!, " ");
        var r = InterpretarSemProjeto(semTag, ctx);
        if (r.Tipo == TipoCaptura.Tarefa) r = ComDetalhesDaTarefa(semTag ?? "", ctx) ?? r;
        return projeto is null ? r : r with { Projeto = projeto, TextoOriginal = (frase ?? "").Trim() };
    }

    /// <summary>
    /// Tarefa: tira da frase "@eva", "avisar 30 min antes" e "às 15h", interpreta o resto e devolve com horário,
    /// aviso e responsáveis. Nulo quando a frase não tem nada disso.
    /// </summary>
    private static Interpretacao? ComDetalhesDaTarefa(string frase, ContextoCaptura ctx)
    {
        var texto = frase;
        string? aviso = null;

        var responsaveis = new List<Guid>();
        var desconhecidos = new List<string>();
        var casa = false;
        foreach (Match m in RxPessoa().Matches(texto))
        {
            var nome = m.Groups["nome"].Value;
            if (Texto.Normalizar(nome) is "casa" or "todos") { casa = true; continue; }
            if (ctx.PessoaPorApelido(nome) is { } p) { if (!responsaveis.Contains(p.Id)) responsaveis.Add(p.Id); }
            else desconhecidos.Add("@" + nome);
        }
        texto = RxPessoa().Replace(texto, " ");
        if (desconhecidos.Count > 0)
            aviso = ctx.Pessoas.Count == 0
                ? $"Não conheço {string.Join(", ", desconhecidos)}."
                : $"Não conheço {string.Join(", ", desconhecidos)}. Pessoas: {string.Join(", ", ctx.Pessoas.Select(p => "@" + p.Nome))}.";

        int? avisoMin = null;
        if (RxAvisoAntes().Match(texto) is { Success: true } mAviso)
        {
            var n = int.Parse(mAviso.Groups["n"].Value);
            var un = Texto.Normalizar(mAviso.Groups["un"].Value);
            avisoMin = un.StartsWith('d') ? n * 1440 : un.StartsWith('h') ? n * 60 : n;
            texto = texto.Remove(mAviso.Index, mAviso.Length).Insert(mAviso.Index, " ");
        }
        else if (RxAvisoNaHora().Match(texto) is { Success: true } mNaHora)
        {
            avisoMin = 0;
            texto = texto.Remove(mNaHora.Index, mNaHora.Length).Insert(mNaHora.Index, " ");
        }

        TimeOnly? hora = null;
        if (RxHorario().Match(texto) is { Success: true } mHora)
        {
            var h = int.Parse(mHora.Groups["h"].Value);
            var min = mHora.Groups["m"].Success ? int.Parse(mHora.Groups["m"].Value) : 0;
            if (h is >= 0 and <= 23 && min is >= 0 and <= 59)
            {
                hora = new TimeOnly(h, min);
                texto = texto.Remove(mHora.Index, mHora.Length).Insert(mHora.Index, " ");
            }
        }

        if (responsaveis.Count == 0 && !casa && desconhecidos.Count == 0 && avisoMin is null && hora is null) return null;

        var r = InterpretarSemProjeto(texto, ctx);
        if (r.Tipo != TipoCaptura.Tarefa) return null;
        var prazo = r.Prazo ?? (hora is null ? null : ctx.Hoje); // "às 15h" sem dia = hoje
        aviso ??= r.Aviso ?? (avisoMin is not null && hora is null
            ? "Para avisar antes, diga o horário. Ex.: às 15h"
            : AvisoTarefa_.Validar(prazo, hora, avisoMin));
        return r with
        {
            TextoOriginal = Limpar(frase),
            Prazo = prazo,
            Hora = hora,
            AvisoMinutos = avisoMin,
            Responsaveis = responsaveis,
            Aviso = aviso,
        };
    }

    private static Interpretacao InterpretarSemProjeto(string? frase, ContextoCaptura ctx)
    {
        var original = Limpar(frase);
        var norm = Texto.Normalizar(original);
        if (norm.Length == 0) return Interpretacao.Vazia(ctx.Hoje);

        // Mapeamento de posição só é seguro quando a normalização preservou o tamanho.
        var fonte = norm.Length == original.Length ? original : norm;
        var data = LerData(norm, ctx.Hoje);
        var baseInterp = new Interpretacao(TipoCaptura.Desconhecido, original) { Data = data };

        if (RxOrcamento().Match(norm) is { Success: true } mOrc)
        {
            var valor = LerValor(mOrc.Groups["resto"].Value);
            return valor is null
                ? baseInterp with { Tipo = TipoCaptura.Orcamento, Aviso = "Faltou o valor do orçamento. Ex.: orçamento 3000" }
                : baseInterp with { Tipo = TipoCaptura.Orcamento, ValorCentavos = valor };
        }

        if (RxNovaConta().Match(norm) is { Success: true } mConta)
            return baseInterp with { Tipo = TipoCaptura.NovaConta, Titulo = Trecho(fonte, mConta.Groups["nome"]) };

        if (RxNovoHabito().Match(norm) is { Success: true } mNovoHab)
            return baseInterp with { Tipo = TipoCaptura.NovoHabito, Titulo = Trecho(fonte, mNovoHab.Groups["nome"]) };

        if (RxTarefa().Match(norm) is { Success: true } mTarefa)
            return InterpretarTarefa(baseInterp, fonte, norm, mTarefa.Groups["resto"], ctx.Hoje);

        if (RxPeso().Match(norm) is { Success: true } mPeso)
            return baseInterp with { Tipo = TipoCaptura.Peso, PesoKg = Decimal(mPeso.Groups["kg"].Value) };

        if (RxComi().Match(norm) is { Success: true } mComi)
            return InterpretarRefeicao(baseInterp, fonte, norm, mComi);

        if (RxAgua().Match(norm) is { Success: true } mAgua)
        {
            var qtd = decimal.Parse(mAgua.Groups["qtd"].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
            var ml = mAgua.Groups["un"].Value == "ml" ? qtd : qtd * 1000;
            return baseInterp with { Tipo = TipoCaptura.Agua, Mililitros = (int)Math.Round(ml) };
        }

        if (RxCardio().Match(norm) is { Success: true } mCardio)
            return InterpretarCardio(baseInterp, norm, mCardio);

        if (RxForca().Match(norm) is { Success: true } mForca)
            return baseInterp with
            {
                Tipo = TipoCaptura.Treino,
                Titulo = Texto.Capitalizar(Trecho(fonte, mForca.Groups["nome"])),
                Series = int.Parse(mForca.Groups["series"].Value),
                Repeticoes = int.Parse(mForca.Groups["reps"].Value),
                CargaKg = mForca.Groups["carga"].Success ? Decimal(mForca.Groups["carga"].Value) : null,
            };

        if (RxReceita().IsMatch(norm))
            return InterpretarDinheiro(baseInterp with { Tipo = TipoCaptura.Receita }, fonte, norm, ctx);

        if (RxDespesa().IsMatch(norm))
            return InterpretarDinheiro(baseInterp with { Tipo = TipoCaptura.Despesa }, fonte, norm, ctx);

        var habito = InterpretarHabito(baseInterp, fonte, norm, ctx);
        if (habito is not null) return habito;

        // Frase curta com valor ("uber 23", "mercado 150", "salário 5000"): é dinheiro.
        // Receita só quando a palavra indica (salário, freela...); senão, despesa.
        if (TemValorSolto(norm))
        {
            var tipo = PalavraChave(norm) is { Tipo: TipoCaptura.Receita } ? TipoCaptura.Receita : TipoCaptura.Despesa;
            return InterpretarDinheiro(baseInterp with { Tipo = tipo }, fonte, norm, ctx);
        }

        return baseInterp with { Aviso = "Ainda não sei registrar isso. Para dinheiro, basta o que foi e o valor: uber 23 · mercado 150 · salário 5000" };
    }

    // ---------- Fitness ----------

    private static Interpretacao InterpretarCardio(Interpretacao i, string norm, Match m)
    {
        var palavra = m.Groups["tipo"].Value;
        var nome = palavra switch
        {
            "corri" or "corrida" => "Corrida",
            "caminhei" or "caminhada" => "Caminhada",
            "pedalei" or "bike" or "bicicleta" or "spinning" => "Bike",
            "nadei" or "natacao" => "Natação",
            "esteira" => "Esteira",
            "eliptico" => "Elíptico",
            "futebol" or "pelada" => "Futebol",
            "muaythai" or "muay thai" => "Muay thai",
            _ => Texto.Capitalizar(palavra),
        };
        var esporte = nome is "Futebol" or "Muay thai";

        var km = RxKm().Match(norm);
        var min = RxMinutos().Match(norm);
        var horas = RxHoras().Match(norm);
        int? minutos = min.Success ? int.Parse(min.Groups["min"].Value) : null;
        if (horas.Success) minutos = (minutos ?? 0) + (int)Math.Round(Decimal(horas.Groups["h"].Value) * 60);

        if (esporte && minutos is null)
            return i with { Tipo = TipoCaptura.Cardio, Titulo = nome, Aviso = $"Faltou o tempo. Ex.: {nome.ToLowerInvariant()} 60 min moderado" };
        if (!km.Success && minutos is null)
            return i with { Tipo = TipoCaptura.Cardio, Titulo = nome, Aviso = "Faltou a distância ou o tempo. Ex.: corri 5 km em 30 min" };

        Fitness.Intensidade? intensidade = null;
        if (esporte)
            intensidade = RxIntensidade().Match(norm).Groups["n"].Value switch
            {
                "leve" or "suave" => Fitness.Intensidade.Leve,
                "intensa" or "intenso" or "pesada" or "pesado" or "puxada" or "puxado" or "forte" => Fitness.Intensidade.Intensa,
                _ => Fitness.Intensidade.Moderada,
            };

        return i with
        {
            Tipo = TipoCaptura.Cardio,
            Titulo = nome,
            DistanciaKm = km.Success ? Decimal(km.Groups["km"].Value) : null,
            Minutos = minutos,
            Intensidade = intensidade,
        };
    }

    private static Interpretacao InterpretarRefeicao(Interpretacao i, string fonte, string norm, Match m)
    {
        Fitness.Refeicao? refeicao = m.Groups["verbo"].Value switch
        {
            "almocei" => Fitness.Refeicao.Almoco,
            "jantei" => Fitness.Refeicao.Jantar,
            _ => null,
        };

        var resto = m.Groups["resto"];
        var inicio = resto.Index;
        var fim = resto.Index + resto.Length;

        // "no almoço", "no café da manhã", "na janta"... em qualquer ponto da frase.
        var mRef = RxNomeRefeicao().Match(norm, inicio, fim - inicio);
        if (mRef.Success)
        {
            refeicao = LerRefeicao(mRef.Groups["ref"].Value);
            var texto = (fonte[inicio..mRef.Index] + " " + fonte[(mRef.Index + mRef.Length)..fim]).Trim();
            return ComQuantidade(i with { Refeicao = refeicao }, texto);
        }

        return ComQuantidade(i with { Refeicao = refeicao }, fonte[inicio..fim]);
    }

    private static Interpretacao ComQuantidade(Interpretacao i, string texto)
    {
        var norm = Texto.Normalizar(texto);
        var original = norm.Length == texto.Trim().Length ? texto.Trim() : norm;
        var q = RxQuantidade().Match(norm);
        if (!q.Success)
            return i with { Tipo = TipoCaptura.Refeicao, Aviso = "Diga a quantidade. Ex.: comi 150g de arroz" };

        var alimento = original[(q.Index + q.Length)..].Trim();
        if (alimento.Length == 0)
            return i with { Tipo = TipoCaptura.Refeicao, Aviso = "Faltou dizer o que comeu. Ex.: comi 150g de arroz" };

        var valor = Decimal(q.Groups["qtd"].Value);
        var unidade = q.Groups["un"].Value;
        // Gramas (ou ml, que para comida dá quase no mesmo); qualquer outra medida usa a porção do alimento.
        int? gramas = unidade switch
        {
            "kg" => (int)Math.Round(valor * 1000),
            "g" or "grama" or "gramas" or "ml" => (int)Math.Round(valor),
            _ => null,
        };

        return i with
        {
            Tipo = TipoCaptura.Refeicao,
            Titulo = alimento,
            Gramas = gramas,
            Unidades = gramas is null ? valor : null,
        };
    }

    private static Fitness.Refeicao LerRefeicao(string nome) => nome switch
    {
        var n when n.StartsWith("cafe") => Fitness.Refeicao.CafeDaManha,
        var n when n.StartsWith("almoco") => Fitness.Refeicao.Almoco,
        var n when n.StartsWith("jant") => Fitness.Refeicao.Jantar,
        "ceia" => Fitness.Refeicao.Ceia,
        "lanche da manha" => Fitness.Refeicao.LancheManha,
        _ => Fitness.Refeicao.LancheTarde,
    };

    private static decimal Decimal(string valor) =>
        decimal.Parse(valor.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);

    // ---------- Dinheiro ----------

    private static Interpretacao InterpretarDinheiro(Interpretacao i, string fonte, string norm, ContextoCaptura ctx)
    {
        var valor = LerValor(norm);
        if (valor is null or 0)
            return i with { Aviso = "Faltou o valor. Ex.: gastei 32,50 no mercado" };

        // Trechos já "usados" (verbo, valor, conta, categoria, data). O que sobrar vira a descrição.
        var usados = new List<(int Inicio, int Fim)>();
        void Usar(Match m) { if (m.Success) usados.Add((m.Index, m.Index + m.Length)); }

        Usar(RxVerboDinheiro().Match(norm));
        Usar(RxValor().Match(norm));
        Usar(RxReais().Match(norm));
        foreach (Match m in RxDataRelativa().Matches(norm)) Usar(m);

        var conta = AcharConhecido(norm, ctx.Contas);
        if (conta is not null) Usar(ComPreposicao(norm, conta));
        else if (RxConta().Match(norm) is { Success: true } mConta)
        {
            conta = Texto.Capitalizar(Trecho(fonte, mConta.Groups["nome"]));
            Usar(mConta);
        }
        if (RxConta().Match(norm) is { Success: true } mFrase && conta is not null &&
            Texto.Normalizar(mFrase.Groups["nome"].Value) == Texto.Normalizar(conta))
            Usar(mFrase); // "com o cartão nubank": tira a expressão inteira

        var categoria = AcharConhecido(norm, ctx.Categorias, ignorar: conta);
        if (categoria is not null) Usar(ComPreposicao(norm, categoria));
        else if (PalavraChave(norm, conta) is { } chave && chave.Tipo == i.Tipo)
        {
            // "uber" → Transporte. A palavra fica na descrição: diz mais do que a categoria.
            categoria = ctx.Categorias.FirstOrDefault(c => Texto.Normalizar(c) == Texto.Normalizar(chave.Categoria)) ?? chave.Categoria;
            var mChave = ComPreposicao(norm, chave.Palavra);
            if (mChave.Success && mChave.Value != chave.Palavra) // tira só a preposição ("de uber" → "uber")
                usados.Add((mChave.Index, mChave.Index + mChave.Length - chave.Palavra.Length));
        }
        else
        {
            foreach (Match m in RxCategoria().Matches(norm))
            {
                var palavra = m.Groups["nome"].Value;
                if (PalavrasVazias.Contains(palavra)) continue;
                if (conta is not null && Texto.Normalizar(conta) == palavra) continue;
                categoria = Texto.Capitalizar(Trecho(fonte, m.Groups["nome"]));
                Usar(m);
                break;
            }
        }

        return i with
        {
            ValorCentavos = valor,
            Conta = conta,
            Categoria = categoria ?? "Outros",
            Descricao = Sobra(fonte, usados),
        };
    }

    /// <summary>Acha um nome conhecido na frase, junto com a preposição antes dele ("no mercado").</summary>
    private static Match ComPreposicao(string norm, string nome) =>
        Regex.Match(norm, $@"(?:\b(?:no|na|nos|nas|em|de|do|da|com|pelo|pela)\s+(?:o |a |um |uma )?)?(?<![a-z0-9]){Regex.Escape(Texto.Normalizar(nome))}(?![a-z0-9])");

    /// <summary>
    /// Junta o que não foi usado e limpa preposições soltas nas pontas.
    /// "gastei 45 na farmácia remédio de gripe" → "Remédio de gripe".
    /// </summary>
    private static string? Sobra(string fonte, List<(int Inicio, int Fim)> usados)
    {
        var chars = fonte.ToCharArray();
        foreach (var (inicio, fim) in usados)
            for (var k = inicio; k < fim && k < chars.Length; k++) chars[k] = ' ';

        var palavras = RxEspacos().Split(new string(chars).Trim()).Where(p => p.Length > 0).ToList();
        while (palavras.Count > 0 && Conectivos.Contains(Texto.Normalizar(palavras[0]).Trim(',', '.', ';'))) palavras.RemoveAt(0);
        while (palavras.Count > 0 && Conectivos.Contains(Texto.Normalizar(palavras[^1]).Trim(',', '.', ';'))) palavras.RemoveAt(palavras.Count - 1);

        var texto = string.Join(' ', palavras).Trim(' ', ',', '.', ';', '-');
        return texto.Count(char.IsLetter) < 3 ? null : Texto.Capitalizar(texto);
    }

    private static readonly HashSet<string> Conectivos =
        ["de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas", "com", "o", "a", "os", "as", "e", "um", "uma",
         "pra", "para", "pro", "pelo", "pela", "por", "reais", "real", "r$", "no cartao", "cartao", "conta"];

    /// <summary>Lê o primeiro valor monetário: "32", "32,50", "R$ 1.240,00". Ignora "3x10" e datas.</summary>
    public static long? LerValor(string norm)
    {
        var m = RxValor().Match(norm);
        if (!m.Success) return null;

        var inteiro = long.Parse(m.Groups["int"].Value.Replace(".", ""));
        var dec = m.Groups["dec"].Success ? m.Groups["dec"].Value.PadRight(2, '0') : "00";
        return inteiro * 100 + long.Parse(dec);
    }

    /// <summary>
    /// Categoria sugerida por uma palavra conhecida ("ifood" → Alimentação fora). Usada na captura e no
    /// formulário de lançamento, para a categoria vir certa sem a pessoa escolher.
    /// </summary>
    public static (string Palavra, string Categoria, TipoCaptura Tipo)? PalavraChave(string? texto, string? ignorar = null)
    {
        var norm = Texto.Normalizar(texto);
        var ignorarNorm = Texto.Normalizar(ignorar);
        foreach (var (palavras, categoria, tipo) in Chaves)
            foreach (var p in palavras)
                if (p != ignorarNorm && ContemPalavras(norm, p))
                    return (p, categoria, tipo);
        return null;
    }

    /// <summary>Sugestão de categoria para um texto livre (descrição do formulário).</summary>
    public static string? SugerirCategoria(string? texto, bool receita) =>
        PalavraChave(texto) is { } c && (c.Tipo == TipoCaptura.Receita) == receita ? c.Categoria : null;

    // As expressões mais longas primeiro dentro de cada grupo; os nomes batem com as categorias iniciais.
    private static readonly (string[] Palavras, string Categoria, TipoCaptura Tipo)[] Chaves =
    [
        (["salario", "pagamento do mes", "holerite", "adiantamento"], "Salário", TipoCaptura.Receita),
        (["freelance", "freela", "bonus", "decimo terceiro", "13o", "rendimento", "rendimentos", "reembolso", "cashback", "venda"], "Extra", TipoCaptura.Receita),
        (["supermercado", "mercado", "feira", "hortifruti", "acougue", "atacadao", "sacolao"], "Mercado", TipoCaptura.Despesa),
        (["ifood", "restaurante", "almoco", "janta", "jantar", "lanche", "padaria", "pizza", "hamburguer", "delivery", "cafe", "bar", "sorvete", "marmita"], "Alimentação fora", TipoCaptura.Despesa),
        (["uber", "taxi", "onibus", "metro", "trem", "passagem", "gasolina", "combustivel", "etanol", "alcool", "estacionamento", "pedagio", "oficina", "mecanico", "ipva"], "Transporte", TipoCaptura.Despesa),
        (["aluguel", "condominio", "luz", "energia", "conta de agua", "internet", "gas", "iptu", "faxina", "diarista"], "Moradia", TipoCaptura.Despesa),
        (["farmacia", "remedio", "remedios", "medico", "consulta", "dentista", "exame", "exames", "plano de saude", "academia", "psicologo"], "Saúde", TipoCaptura.Despesa),
        (["netflix", "spotify", "prime video", "amazon prime", "disney", "hbo", "youtube premium", "icloud", "google one", "assinatura"], "Assinaturas", TipoCaptura.Despesa),
        (["cinema", "show", "ingresso", "viagem", "hotel", "passeio", "jogo", "balada", "festa"], "Lazer", TipoCaptura.Despesa),
    ];

    /// <summary>Tem um valor que não é medida ("20 min", "5 km", "3x10" não contam).</summary>
    private static bool TemValorSolto(string norm) =>
        RxValor().Matches(norm).Any(m => !RxUnidadeDepois().IsMatch(norm, m.Index + m.Length));

    // ---------- Tarefa ----------

    private static Interpretacao InterpretarTarefa(Interpretacao i, string fonte, string norm, Group resto, DateOnly hoje)
    {
        var titulo = Trecho(fonte, resto);
        DateOnly? prazo = null;

        var mPrazo = RxPrazo().Match(norm, resto.Index);
        if (mPrazo.Success)
        {
            prazo = LerPrazo(mPrazo, hoje);
            if (prazo is not null)
                titulo = fonte[resto.Index..mPrazo.Index].Trim();
        }

        if (string.IsNullOrWhiteSpace(titulo))
            return i with { Tipo = TipoCaptura.Tarefa, Aviso = "Faltou dizer qual é a tarefa." };

        return i with { Tipo = TipoCaptura.Tarefa, Titulo = Texto.Capitalizar(titulo), Prazo = prazo };
    }

    private static DateOnly? LerPrazo(Match m, DateOnly hoje)
    {
        var palavra = m.Groups["quando"].Value;
        if (m.Groups["dia"].Success)
        {
            var dia = int.Parse(m.Groups["dia"].Value);
            var mes = int.Parse(m.Groups["mes"].Value);
            if (mes is < 1 or > 12 || dia < 1 || dia > DateTime.DaysInMonth(hoje.Year, mes)) return null;
            var data = new DateOnly(hoje.Year, mes, dia);
            return data < hoje ? data.AddYears(1) : data;
        }

        return palavra switch
        {
            "hoje" => hoje,
            "ontem" => hoje.AddDays(-1),
            "amanha" => hoje.AddDays(1),
            _ when DiasDaSemana.TryGetValue(palavra, out var alvo) => ProximoDia(hoje, alvo),
            _ => null,
        };
    }

    private static DateOnly ProximoDia(DateOnly hoje, DayOfWeek alvo)
    {
        var dias = ((int)alvo - (int)hoje.DayOfWeek + 7) % 7;
        return hoje.AddDays(dias == 0 ? 7 : dias);
    }

    // ---------- Hábito ----------

    private static Interpretacao? InterpretarHabito(Interpretacao i, string fonte, string norm, ContextoCaptura ctx)
    {
        var mFiz = RxFiz().Match(norm);
        var alvoNorm = mFiz.Success ? mFiz.Groups["nome"].Value : norm;

        var conhecido = ctx.Habitos.FirstOrDefault(h => Texto.Normalizar(h) == alvoNorm)
                        ?? ctx.Habitos
                            .OrderByDescending(h => h.Length)
                            .FirstOrDefault(h => ContemPalavras(alvoNorm, Texto.Normalizar(h)));

        if (conhecido is not null)
            return i with { Tipo = TipoCaptura.Habito, Titulo = conhecido };

        if (mFiz.Success)
        {
            var nome = Trecho(fonte, mFiz.Groups["nome"]);
            return i with
            {
                Tipo = TipoCaptura.Habito,
                Titulo = nome,
                Aviso = $"Não conheço o hábito \"{nome}\". Crie com: novo hábito {nome}",
            };
        }

        return null;
    }

    // ---------- Utilitários ----------

    private static DateOnly LerData(string norm, DateOnly hoje)
    {
        if (RxAnteontem().IsMatch(norm)) return hoje.AddDays(-2);
        if (RxOntem().IsMatch(norm)) return hoje.AddDays(-1);
        return hoje;
    }

    /// <summary>Procura um nome já cadastrado dentro da frase, preferindo o mais longo.</summary>
    private static string? AcharConhecido(string norm, IReadOnlyList<string> nomes, string? ignorar = null)
    {
        var ignorarNorm = Texto.Normalizar(ignorar);
        return nomes
            .Where(n => Texto.Normalizar(n) != ignorarNorm)
            .OrderByDescending(n => n.Length)
            .FirstOrDefault(n => ContemPalavras(norm, Texto.Normalizar(n)));
    }

    private static bool ContemPalavras(string texto, string trecho) =>
        trecho.Length > 0 && Regex.IsMatch(texto, $@"(?<![a-z0-9]){Regex.Escape(trecho)}(?![a-z0-9])");

    private static string Trecho(string fonte, Group g) => fonte.Substring(g.Index, g.Length).Trim();

    private static string Limpar(string? frase) =>
        string.IsNullOrWhiteSpace(frase) ? "" : RxEspacos().Replace(frase.Trim(), " ");

    private static readonly HashSet<string> PalavrasVazias =
        ["o", "a", "um", "uma", "hoje", "ontem", "anteontem", "reais", "real", "credito", "debito", "cartao", "conta", "pix", "dinheiro"];

    private static readonly Dictionary<string, DayOfWeek> DiasDaSemana = new()
    {
        ["domingo"] = DayOfWeek.Sunday,
        ["segunda"] = DayOfWeek.Monday,
        ["terca"] = DayOfWeek.Tuesday,
        ["quarta"] = DayOfWeek.Wednesday,
        ["quinta"] = DayOfWeek.Thursday,
        ["sexta"] = DayOfWeek.Friday,
        ["sabado"] = DayOfWeek.Saturday,
    };

    [GeneratedRegex(@"\s+")] private static partial Regex RxEspacos();
    [GeneratedRegex(@"^orcamento(?: mensal)?(?: de)?\b(?<resto>.*)$")] private static partial Regex RxOrcamento();
    [GeneratedRegex(@"^nova conta (?<nome>.+)$")] private static partial Regex RxNovaConta();
    [GeneratedRegex(@"^novo habito (?<nome>.+)$")] private static partial Regex RxNovoHabito();
    [GeneratedRegex(@"^(?:tarefa|fazer|lembrar de|lembrete) (?<resto>.+)$")] private static partial Regex RxTarefa();
    [GeneratedRegex(@"\s+(?:(?:ate|na|no|dia|para|pra)\s+)?(?<quando>hoje|ontem|amanha|domingo|segunda|terca|quarta|quinta|sexta|sabado|(?<dia>\d{1,2})/(?<mes>\d{1,2}))$")] private static partial Regex RxPrazo();
    [GeneratedRegex(@"(?:bebi|agua)\D*?(?<qtd>\d+(?:[.,]\d+)?)\s*(?<un>ml|l|litros?)(?![a-z])")] private static partial Regex RxAgua();
    [GeneratedRegex(@"^(?:fiz |treinei )?(?<nome>[a-z][a-z ]*?)\s+(?<series>\d+)\s*x\s*(?<reps>\d+)(?:\s*(?:com |de )?(?<carga>\d+(?:[.,]\d+)?)\s*(?:kg|quilos?)?)?\s*$")] private static partial Regex RxForca();
    [GeneratedRegex(@"^(?:(?:fiz|treinei|joguei|aula de) )?(?<tipo>corri|corrida|caminhei|caminhada|pedalei|bike|bicicleta|spinning|nadei|natacao|esteira|eliptico|muay ?thai|futebol|pelada)\b")] private static partial Regex RxCardio();
    [GeneratedRegex(@"\b(?<n>leve|suave|moderad[oa]|medio|media|intens[oa]|pesad[oa]|puxad[oa]|forte)\b")] private static partial Regex RxIntensidade();
    [GeneratedRegex(@"(?<km>\d+(?:[.,]\d+)?)\s*(?:km|quilometros?)\b")] private static partial Regex RxKm();
    [GeneratedRegex(@"(?<min>\d+)\s*(?:min|mins|minutos?)\b")] private static partial Regex RxMinutos();
    [GeneratedRegex(@"(?<h>\d+(?:[.,]\d+)?)\s*(?:h|horas?)(?![a-z])")] private static partial Regex RxHoras();
    [GeneratedRegex(@"^(?:meu )?(?:peso|pesei|pesando|balanca)\s*(?:de |hoje |:)?\s*(?<kg>\d{2,3}(?:[.,]\d{1,2})?)\s*(?:kg|quilos?)?(?: hoje)?$")] private static partial Regex RxPeso();
    [GeneratedRegex(@"^(?<verbo>comi|almocei|jantei|tomei|lanchei)\b(?<resto>.*)$")] private static partial Regex RxComi();
    [GeneratedRegex(@"\b(?:no|na|de|pro|pra)\s+(?<ref>cafe da manha|cafe|almoco|janta|jantar|lanche da manha|lanche da tarde|lanche|ceia)\b")] private static partial Regex RxNomeRefeicao();
    [GeneratedRegex(@"^\s*(?<qtd>\d+(?:[.,]\d+)?)\s*(?<un>kg|g|gramas?|ml|un|unidades?|fatias?|colher(?:es)?|porcao|porcoes|scoops?|copos?|xicaras?|potes?|latas?|pedacos?|conchas?|bifes?)?\b\s*(?:de |do |da )?")] private static partial Regex RxQuantidade();
    [GeneratedRegex(@"^(?:recebi|ganhei|entrou|caiu)\b")] private static partial Regex RxReceita();
    [GeneratedRegex(@"^(?:gastei|paguei|comprei|gasto|despesa)\b|^(?:r\$\s*)?\d")] private static partial Regex RxDespesa();
    [GeneratedRegex(@"(?<![\d/.,])(?:r\$\s*)?(?<int>\d{1,3}(?:\.\d{3})+|\d+)(?:,(?<dec>\d{1,2}))?(?![\d/x]|\s*x\s*\d)")] private static partial Regex RxValor();
    [GeneratedRegex(@"\b(?:com (?:o|a|meu|minha) (?:cartao |conta )?|no cartao |na conta |pelo |pela )(?<nome>[a-z][a-z0-9]*)")] private static partial Regex RxConta();
    [GeneratedRegex(@"\b(?:no|na|nos|nas|em|de)\s+(?:um |uma )?(?<nome>[a-z][a-z0-9]+)")] private static partial Regex RxCategoria();
    [GeneratedRegex(@"(?<![\w])@(?<nome>\p{L}[\p{L}\d_-]*)")] private static partial Regex RxPessoa();
    [GeneratedRegex(@"(?i)(?:(?:me\s+)?(?:avisar|avise|aviso|lembrar|lembre|lembrete|alerta)\s+(?:com\s+)?)?(?<n>\d{1,3})\s*(?<un>min|mins|minutos?|h|horas?|dias?)\s+antes(?![\p{L}])")] private static partial Regex RxAvisoAntes();
    [GeneratedRegex(@"(?i)(?:me\s+)?(?:avisar|avise|lembrar|lembre)\s+na\s+hora(?![\p{L}])")] private static partial Regex RxAvisoNaHora();
    [GeneratedRegex(@"(?i)(?<![\p{L}\d:])(?:(?:[àa]s|a)\s+)?(?<h>\d{1,2})(?:(?::|h)(?<m>\d{2})|h(?:rs?)?)(?![\p{L}\d])")] private static partial Regex RxHorario();
    [GeneratedRegex(@"(?<![\w])#(?<nome>[\p{L}\d][\p{L}\d_-]*)")] private static partial Regex RxProjeto();
    [GeneratedRegex(@"^(?:fiz|feito|conclui|completei|marquei)\s+(?:o |a )?(?<nome>.+)$")] private static partial Regex RxFiz();
    [GeneratedRegex(@"^(?:gastei|paguei|comprei|gasto|despesa|recebi|ganhei|entrou|caiu)\b")] private static partial Regex RxVerboDinheiro();
    [GeneratedRegex(@"\breais?\b")] private static partial Regex RxReais();
    [GeneratedRegex(@"\G\s*(?:min|mins|minutos?|h|horas?|km|kg|quilos?|g|gramas?|ml|l|litros?|x|vezes|reps?|series|%|paginas?)(?![a-z])")] private static partial Regex RxUnidadeDepois();
    [GeneratedRegex(@"\b(?:hoje|ontem|anteontem)\b")] private static partial Regex RxDataRelativa();
    [GeneratedRegex(@"\bontem\b")] private static partial Regex RxOntem();
    [GeneratedRegex(@"\banteontem\b")] private static partial Regex RxAnteontem();
}
