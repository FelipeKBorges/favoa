namespace Favoa.Core.Fitness;

// =====================================================================
//  Treino
// =====================================================================

/// <summary>Quanto o treino foi puxado. Escolhe o MET usado na estimativa de gasto (<see cref="GastoCalorico"/>).</summary>
public enum Intensidade
{
    Leve = 0,
    Moderada = 1,
    Intensa = 2,
}

public enum TipoExercicio
{
    Forca = 0,
    Cardio = 1,
}

public sealed class Exercicio : Favoa.Core.Pessoas.IDaFamilia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Família dona do registro (tenant). Nulo = catálogo comum a todas.</summary>
    public Guid? FamiliaId { get; set; }
    public string Nome { get; set; } = "";
    /// <summary>Peito, Costas, Pernas, Ombros, Braços, Core, Cardio...</summary>
    public string Grupo { get; set; } = "";
    public TipoExercicio Tipo { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Treino A, B, C... com os exercícios e as metas de cada um.</summary>
public sealed class FichaTreino : Favoa.Core.Pessoas.IComDono, Favoa.Core.Pessoas.IDaFamilia
{
    /// <summary>Casa (tenant) do registro. O banco filtra e preenche sozinho com a família atual.</summary>
    public Guid? FamiliaId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Dono do registro (pessoa da casa).</summary>
    public Guid? PessoaId { get; set; }
    public string Nome { get; set; } = "";
    public int Ordem { get; set; }
    public bool Ativa { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public List<ItemFicha> Itens { get; set; } = [];
}

public sealed class ItemFicha : Favoa.Core.Pessoas.IDaFamilia
{
    /// <summary>Casa (tenant) do registro. O banco filtra e preenche sozinho com a família atual.</summary>
    public Guid? FamiliaId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FichaId { get; set; }
    public Guid ExercicioId { get; set; }
    public int Ordem { get; set; }
    public int? Series { get; set; }
    public int? Repeticoes { get; set; }
    public decimal? CargaKg { get; set; }
    public int? Minutos { get; set; }
    public Exercicio? Exercicio { get; set; }
}

/// <summary>Um treino feito num dia.</summary>
public sealed class SessaoTreino : Favoa.Core.Pessoas.IComDono, Favoa.Core.Pessoas.IDaFamilia
{
    /// <summary>Casa (tenant) do registro. O banco filtra e preenche sozinho com a família atual.</summary>
    public Guid? FamiliaId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Dono do registro (pessoa da casa).</summary>
    public Guid? PessoaId { get; set; }
    public DateOnly Data { get; set; }
    public Guid? FichaId { get; set; }
    public string Observacao { get; set; } = "";
    /// <summary>Duração da musculação do dia, quando a pessoa informa. Sem ela, o gasto estima pelas séries.</summary>
    public int? MinutosForca { get; set; }
    public Intensidade? IntensidadeForca { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public FichaTreino? Ficha { get; set; }
    public List<ExercicioRealizado> Exercicios { get; set; } = [];
}

/// <summary>Um exercício dentro de uma sessão. Força usa séries/repetições/carga; cardio, minutos/distância.</summary>
public sealed class ExercicioRealizado : Favoa.Core.Pessoas.IDaFamilia
{
    /// <summary>Casa (tenant) do registro. O banco filtra e preenche sozinho com a família atual.</summary>
    public Guid? FamiliaId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessaoId { get; set; }
    public Guid ExercicioId { get; set; }
    public int Ordem { get; set; }
    public int? Series { get; set; }
    public int? Repeticoes { get; set; }
    public decimal? CargaKg { get; set; }
    public int? Minutos { get; set; }
    public decimal? DistanciaKm { get; set; }
    /// <summary>Esportes (muay thai, futebol): quão puxado foi.</summary>
    public Intensidade? Intensidade { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public SessaoTreino? Sessao { get; set; }
    public Exercicio? Exercicio { get; set; }

    /// <summary>Séries × repetições × carga. Medida simples de volume para comparar treinos.</summary>
    public decimal Volume => (Series ?? 0) * (Repeticoes ?? 0) * (CargaKg ?? 0);
}

// =====================================================================
//  Dieta
// =====================================================================

/// <summary>Valores por 100 g. A base inicial vem da TACO (UNICAMP); a pessoa pode cadastrar os seus.</summary>
public sealed class Alimento : Favoa.Core.Pessoas.IDaFamilia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Família dona do registro (tenant). Nulo = catálogo comum a todas.</summary>
    public Guid? FamiliaId { get; set; }
    public string Nome { get; set; } = "";
    public string Grupo { get; set; } = "";
    public double Kcal { get; set; }
    public double Proteina { get; set; }
    public double Carboidrato { get; set; }
    public double Gordura { get; set; }
    public double Fibra { get; set; }
    public int? CodigoTaco { get; set; }
    /// <summary>De onde veio quando não é TACO nem cadastro próprio: <see cref="FontesAlimento"/>.</summary>
    public string? Fonte { get; set; }
    /// <summary>Código na fonte (código do IBGE, código de barras no Open Food Facts).</summary>
    public string? CodigoExterno { get; set; }
    public bool Personalizado { get; set; }
    /// <summary>Porção caseira opcional: "1 unidade" = 50 g.</summary>
    public string? NomePorcao { get; set; }
    public int? GramasPorcao { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Medida caseira de um alimento: "1 unidade = 45 g", "1 fatia = 25 g". Vem da tabela de medidas referidas do IBGE (POF 2008-2009,
/// estimativa de consumo médio) ou é cadastrada pela pessoa. <see cref="Gramas"/> vale para a porção como o alimento é lançado.
/// </summary>
public sealed class PorcaoAlimento : Favoa.Core.Pessoas.IDaFamilia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Casa dona da medida. Nulo = catálogo do IBGE; medida com o mesmo nome da casa vale no lugar da do catálogo.</summary>
    public Guid? FamiliaId { get; set; }
    public Guid AlimentoId { get; set; }
    public string Nome { get; set; } = "";
    public double Gramas { get; set; }
    /// <summary>Unidade que a busca já deixa escolhida ao abrir o alimento (ovo → unidade, pão de forma → fatia).</summary>
    public bool Padrao { get; set; }
    public string Origem { get; set; } = OrigemPorcao.Ibge;
}

public static class OrigemPorcao
{
    public const string Ibge = "IBGE";
    public const string Meu = "Meu";
}

public static class FontesAlimento
{
    public const string Ibge = "IBGE";
    public const string OpenFoodFacts = "Open Food Facts";
    /// <summary>Preparados comuns (pastel, esfiha, X-burger...) que as tabelas oficiais não trazem: valores médios estimados pelo Fávoa.</summary>
    public const string Estimativa = "Estimativa";

    /// <summary>Rótulo curto da origem, para a busca e a lista de alimentos.</summary>
    public static string Rotulo(Alimento a) =>
        a.Personalizado ? "Meu" : a.CodigoTaco is not null ? "TACO" : a.Fonte ?? "";
}

public enum Refeicao
{
    CafeDaManha = 0,
    LancheManha = 1,
    Almoco = 2,
    LancheTarde = 3,
    Jantar = 4,
    Ceia = 5,
}

public static class Refeicoes
{
    public static readonly Refeicao[] Todas = Enum.GetValues<Refeicao>();

    public static string Nome(Refeicao r) => r switch
    {
        Refeicao.CafeDaManha => "Café da manhã",
        Refeicao.LancheManha => "Lanche da manhã",
        Refeicao.Almoco => "Almoço",
        Refeicao.LancheTarde => "Lanche da tarde",
        Refeicao.Jantar => "Jantar",
        _ => "Ceia",
    };

    /// <summary>Refeição provável pela hora, para quando a frase não diz ("comi 2 ovos").</summary>
    public static Refeicao PelaHora(int hora) => hora switch
    {
        < 10 => Refeicao.CafeDaManha,
        < 12 => Refeicao.LancheManha,
        < 15 => Refeicao.Almoco,
        < 18 => Refeicao.LancheTarde,
        < 22 => Refeicao.Jantar,
        _ => Refeicao.Ceia,
    };
}

/// <summary>Um item do plano alimentar: em tal refeição, tantos gramas de tal alimento.</summary>
public sealed class ItemPlano : Favoa.Core.Pessoas.IComDono, Favoa.Core.Pessoas.IDaFamilia
{
    /// <summary>Casa (tenant) do registro. O banco filtra e preenche sozinho com a família atual.</summary>
    public Guid? FamiliaId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Dono do registro (pessoa da casa).</summary>
    public Guid? PessoaId { get; set; }
    public Refeicao Refeicao { get; set; }
    public Guid AlimentoId { get; set; }
    public int Gramas { get; set; }
    /// <summary>Como foi lançado: "g", "ml" ou o nome da medida ("Unidade"). Nulo = só gramas.</summary>
    public string? Unidade { get; set; }
    /// <summary>Quantas dessa unidade (1,5 fatia). Os gramas acima são o que conta nas contas.</summary>
    public double? Quantidade { get; set; }
    public int Ordem { get; set; }
    public Alimento? Alimento { get; set; }
}

/// <summary>O que foi comido num dia.</summary>
public sealed class Consumo : Favoa.Core.Pessoas.IComDono, Favoa.Core.Pessoas.IDaFamilia
{
    /// <summary>Casa (tenant) do registro. O banco filtra e preenche sozinho com a família atual.</summary>
    public Guid? FamiliaId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Dono do registro (pessoa da casa).</summary>
    public Guid? PessoaId { get; set; }
    public DateOnly Data { get; set; }
    public Refeicao Refeicao { get; set; }
    public Guid AlimentoId { get; set; }
    public int Gramas { get; set; }
    /// <summary>Como foi lançado: "g", "ml" ou o nome da medida ("Unidade"). Nulo = só gramas.</summary>
    public string? Unidade { get; set; }
    /// <summary>Quantas dessa unidade (1,5 fatia). Os gramas acima são o que conta nas contas.</summary>
    public double? Quantidade { get; set; }
    /// <summary>Veio do botão "segui o plano".</summary>
    public bool DoPlano { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public Alimento? Alimento { get; set; }
}

// =====================================================================
//  Corpo
// =====================================================================

/// <summary>Um peso por dia (registrar de novo no mesmo dia substitui).</summary>
public sealed class RegistroPeso : Favoa.Core.Pessoas.IComDono, Favoa.Core.Pessoas.IDaFamilia
{
    /// <summary>Casa (tenant) do registro. O banco filtra e preenche sozinho com a família atual.</summary>
    public Guid? FamiliaId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Dono do registro (pessoa da casa).</summary>
    public Guid? PessoaId { get; set; }
    public DateOnly Data { get; set; }
    public decimal Kg { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

// =====================================================================
//  Cálculos puros
// =====================================================================

public readonly record struct Macros(double Kcal, double Proteina, double Carboidrato, double Gordura)
{
    public static readonly Macros Zero = new(0, 0, 0, 0);

    public static Macros De(Alimento a, int gramas) => new(
        a.Kcal * gramas / 100.0,
        a.Proteina * gramas / 100.0,
        a.Carboidrato * gramas / 100.0,
        a.Gordura * gramas / 100.0);

    public static Macros operator +(Macros x, Macros y) =>
        new(x.Kcal + y.Kcal, x.Proteina + y.Proteina, x.Carboidrato + y.Carboidrato, x.Gordura + y.Gordura);

    public static Macros Somar(IEnumerable<Macros> itens) => itens.Aggregate(Zero, (a, b) => a + b);
}

/// <summary>Metas configuráveis. Zero = sem meta.</summary>
public sealed record MetasFitness(int Kcal, int ProteinaG, int CarboidratoG, int GorduraG, int TreinosSemana, int CompensacaoTreinoPct = 100)
{
    public static readonly MetasFitness Padrao = new(0, 0, 0, 0, 3);

    /// <summary>Meta de calorias do dia: a base (dia sem treino) mais a parte do gasto do treino que entra na meta.</summary>
    public int MetaKcalDoDia(int gastoTreinoKcal) =>
        Kcal <= 0 ? 0 : Kcal + (int)Math.Round(Math.Max(0, gastoTreinoKcal) * Math.Clamp(CompensacaoTreinoPct, 0, 100) / 100.0, MidpointRounding.AwayFromZero);

    public const string ChaveCompensacao = "meta_kcal_compensacao";
    public const string ChaveKcal = "meta_kcal";
    public const string ChaveProteina = "meta_proteina_g";
    public const string ChaveCarboidrato = "meta_carboidrato_g";
    public const string ChaveGordura = "meta_gordura_g";
    public const string ChaveTreinosSemana = "meta_treinos_semana";
}

public sealed record PontoEvolucao(DateOnly Data, decimal Valor);

public static class EvolucaoTreino
{
    /// <summary>
    /// Maior carga de cada dia em que o exercício foi feito (força) ou a maior distância/tempo (cardio).
    /// </summary>
    public static IReadOnlyList<PontoEvolucao> Pontos(IEnumerable<(DateOnly Data, ExercicioRealizado Feito)> historico, TipoExercicio tipo) =>
        historico
            .GroupBy(h => h.Data)
            .Select(g => new PontoEvolucao(g.Key, g.Max(h => tipo == TipoExercicio.Forca
                ? h.Feito.CargaKg ?? 0
                : h.Feito.DistanciaKm ?? h.Feito.Minutos ?? 0)))
            .Where(p => p.Valor > 0)
            .OrderBy(p => p.Data)
            .ToList();

    /// <summary>
    /// Próxima ficha na rotação: a que vem depois da última usada (por ordem). Sem histórico, a primeira.
    /// </summary>
    public static FichaTreino? ProximaFicha(IReadOnlyList<FichaTreino> fichasAtivas, Guid? ultimaFichaId)
    {
        var ordenadas = fichasAtivas.OrderBy(f => f.Ordem).ThenBy(f => f.Nome).ToList();
        if (ordenadas.Count == 0) return null;
        var i = ordenadas.FindIndex(f => f.Id == ultimaFichaId);
        return ordenadas[(i + 1) % ordenadas.Count];
    }

    /// <summary>Treinos (dias distintos) nos últimos 7 dias, contando hoje.</summary>
    public static int DiasTreinadosUltimos7(IEnumerable<DateOnly> datasSessoes, DateOnly hoje) =>
        datasSessoes.Where(d => d <= hoje && d > hoje.AddDays(-7)).Distinct().Count();
}
