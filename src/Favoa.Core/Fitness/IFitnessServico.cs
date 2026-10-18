namespace Favoa.Core.Fitness;

// ---------- Treino ----------

public sealed record ExercicioResumo(
    Guid Id, string Nome, string Grupo, TipoExercicio Tipo, bool Ativo,
    int VezesFeito, DateOnly? UltimaVez, decimal? MelhorCargaKg);

public sealed record ItemFichaResumo(
    Guid ExercicioId, string Exercicio, TipoExercicio Tipo, int? Series, int? Repeticoes, decimal? CargaKg, int? Minutos);

public sealed record FichaResumo(Guid Id, string Nome, int Ordem, IReadOnlyList<ItemFichaResumo> Itens);

/// <summary>Para criar ou editar uma ficha. Exercícios são citados pelo nome; os que não existem são criados.</summary>
public sealed record FichaEdicao(Guid? Id, string Nome, IReadOnlyList<ItemFichaEdicao> Itens);

public sealed record ItemFichaEdicao(string Exercicio, TipoExercicio Tipo, int? Series, int? Repeticoes, decimal? CargaKg, int? Minutos);

/// <summary>Uma linha editável do treino do dia. Mutável de propósito: a tela faz bind direto.</summary>
public sealed class LinhaSessao
{
    public string Exercicio { get; set; } = "";
    public TipoExercicio Tipo { get; set; }
    public int? Series { get; set; }
    public int? Repeticoes { get; set; }
    public decimal? CargaKg { get; set; }
    public int? Minutos { get; set; }
    public decimal? DistanciaKm { get; set; }
    public Intensidade? Intensidade { get; set; }
    public bool Feito { get; set; }
    /// <summary>"3×10 · 40 kg em 03/10", para comparar com a última vez.</summary>
    public string? UltimaVez { get; set; }
}

public sealed class SessaoRascunho
{
    public DateOnly Data { get; set; }
    public Guid? FichaId { get; set; }
    public string Titulo { get; set; } = "Treino livre";
    public List<LinhaSessao> Linhas { get; set; } = [];
    public string Observacao { get; set; } = "";
    /// <summary>Duração e intensidade da musculação (opcionais; sem elas o gasto estima pelas séries).</summary>
    public int? MinutosForca { get; set; }
    public Intensidade? IntensidadeForca { get; set; }
}

/// <summary>Duração, intensidade e distância da última vez que a pessoa fez este esporte; serve para registrar em um toque.</summary>
public sealed record UltimoEsporte(string Nome, int? Minutos, Intensidade? Intensidade, decimal? DistanciaKm);

public sealed record SessaoResumo(Guid Id, DateOnly Data, string Titulo, IReadOnlyList<string> Itens, decimal VolumeKg, int MinutosCardio)
{
    /// <summary>Gasto bruto estimado do treino; nulo sem peso registrado ou sem dados para estimar.</summary>
    public int? Kcal { get; init; }
}

public sealed record PainelTreino(
    DateOnly Hoje,
    int TreinosUltimos7Dias,
    int MetaSemana,
    FichaResumo? ProximaFicha,
    IReadOnlyList<FichaResumo> Fichas,
    IReadOnlyList<SessaoResumo> Recentes,
    IReadOnlyList<ExercicioResumo> Exercicios)
{
    /// <summary>Último peso registrado; base do gasto calórico.</summary>
    public decimal? PesoAtualKg { get; init; }
}

public sealed record EvolucaoExercicio(ExercicioResumo Exercicio, IReadOnlyList<PontoEvolucao> Pontos, IReadOnlyList<string> Historico);

// ---------- Dieta ----------

public sealed record AlimentoResumo(
    Guid Id, string Nome, string Grupo, double Kcal, double Proteina, double Carboidrato, double Gordura,
    bool Personalizado, string? NomePorcao, int? GramasPorcao)
{
    /// <summary>TACO, IBGE, Open Food Facts ou Meu.</summary>
    public string Fonte { get; init; } = "";
    public string? Marca { get; init; }
}

/// <summary>Produto achado numa base da internet (ainda não salvo no Fávoa). Valores por 100 g.</summary>
public sealed record ProdutoOnline(
    string Codigo, string Nome, string? Marca, double Kcal, double Proteina, double Carboidrato, double Gordura, double Fibra,
    string? NomePorcao, int? GramasPorcao);

/// <summary>Alimento que a pessoa lança com frequência, com a quantidade mais recente.</summary>
public sealed record AlimentoRecente(AlimentoResumo Alimento, int Gramas, int Vezes);

public sealed record AlimentoEdicao(
    Guid? Id, string Nome, double Kcal, double Proteina, double Carboidrato, double Gordura, string? NomePorcao, int? GramasPorcao);

public sealed record ItemRefeicao(Guid Id, Guid AlimentoId, string Alimento, int Gramas, Macros Macros, bool DoPlano);

public sealed record RefeicaoDia(Refeicao Refeicao, IReadOnlyList<ItemRefeicao> Plano, IReadOnlyList<ItemRefeicao> Consumido)
{
    public string Nome => Refeicoes.Nome(Refeicao);
    public Macros TotalPlano => Macros.Somar(Plano.Select(i => i.Macros));
    public Macros TotalConsumido => Macros.Somar(Consumido.Select(i => i.Macros));
    public bool SeguiuPlano => Plano.Count > 0 && Consumido.Any(c => c.DoPlano);
}

public sealed record DiaDieta(DateOnly Data, IReadOnlyList<RefeicaoDia> Refeicoes, MetasFitness Metas, int AguaMl, int MetaAguaMl)
{
    /// <summary>Gasto bruto dos treinos do dia.</summary>
    public int GastoTreinoKcal { get; init; }

    /// <summary>Meta base mais a parte do gasto do treino que entra na meta (zero se não há meta).</summary>
    public int MetaKcalDoDia => Metas.MetaKcalDoDia(GastoTreinoKcal);

    public Macros Total => Macros.Somar(Refeicoes.Select(r => r.TotalConsumido));
    public Macros TotalPlano => Macros.Somar(Refeicoes.Select(r => r.TotalPlano));
}

// ---------- Corpo ----------

public sealed record PontoPeso(Guid Id, DateOnly Data, decimal Kg);

public sealed record HistoricoPeso(IReadOnlyList<PontoPeso> Pontos)
{
    public PontoPeso? Atual => Pontos.Count == 0 ? null : Pontos[^1];

    /// <summary>Diferença entre o peso atual e o registro mais próximo de 30 dias atrás.</summary>
    public decimal? Variacao30Dias
    {
        get
        {
            if (Atual is not { } atual) return null;
            var alvo = atual.Data.AddDays(-30);
            var referencia = Pontos.Where(p => p.Data <= alvo).LastOrDefault() ?? Pontos[0];
            return referencia == atual ? null : atual.Kg - referencia.Kg;
        }
    }
}

/// <summary>Treino, dieta e peso. Separado de <see cref="IFavoaServico"/> para manter cada módulo enxuto.</summary>
public interface IFitnessServico
{
    // Treino
    Task<PainelTreino> ObterPainelTreinoAsync(DateOnly hoje, CancellationToken ct = default);
    Task<SessaoRascunho> PrepararSessaoAsync(Guid? fichaId, DateOnly dia, CancellationToken ct = default);
    /// <summary>Rascunho igual ao treino mais recente (mesmos exercícios, séries, cargas, tempos), para repetir sem digitar tudo.</summary>
    Task<SessaoRascunho?> PrepararRepeticaoAsync(DateOnly dia, CancellationToken ct = default);
    /// <summary>Para cada esporte pedido que a pessoa já fez com duração registrada, os números da última vez (nome conforme pedido).</summary>
    Task<IReadOnlyDictionary<string, UltimoEsporte>> ObterUltimosEsportesAsync(IEnumerable<string> nomes, CancellationToken ct = default);
    Task<ResultadoRegistro> SalvarSessaoAsync(SessaoRascunho sessao, CancellationToken ct = default);
    Task<ResultadoRegistro> ExcluirSessaoAsync(Guid sessaoId, CancellationToken ct = default);
    Task<ResultadoRegistro> SalvarFichaAsync(FichaEdicao ficha, CancellationToken ct = default);
    Task<ResultadoRegistro> ExcluirFichaAsync(Guid fichaId, CancellationToken ct = default);
    Task<EvolucaoExercicio?> ObterEvolucaoAsync(Guid exercicioId, CancellationToken ct = default);

    // Dieta
    Task<DiaDieta> ObterDiaDietaAsync(DateOnly dia, CancellationToken ct = default);
    Task<IReadOnlyList<AlimentoResumo>> BuscarAlimentosAsync(string texto, int limite = 12, CancellationToken ct = default);
    Task<ResultadoRegistro> SalvarAlimentoAsync(AlimentoEdicao alimento, CancellationToken ct = default);

    /// <summary>Guarda um produto da internet na base local (ou devolve o que já foi guardado com o mesmo código).</summary>
    Task<AlimentoResumo> SalvarProdutoOnlineAsync(ProdutoOnline produto, CancellationToken ct = default);
    Task<ResultadoRegistro> AdicionarConsumoAsync(DateOnly dia, Refeicao refeicao, Guid alimentoId, int gramas, CancellationToken ct = default);
    Task<ResultadoRegistro> RemoverConsumoAsync(Guid consumoId, CancellationToken ct = default);
    Task<ResultadoRegistro> SeguirPlanoAsync(DateOnly dia, Refeicao refeicao, CancellationToken ct = default);
    /// <summary>Copia o que foi comido na refeição de outro dia (ex.: ontem) para o dia escolhido.</summary>
    Task<ResultadoRegistro> CopiarRefeicaoAsync(DateOnly origem, DateOnly destino, Refeicao refeicao, CancellationToken ct = default);
    /// <summary>Os alimentos mais lançados nos últimos 60 dias (mais frequentes primeiro), com a última quantidade usada.</summary>
    Task<IReadOnlyList<AlimentoRecente>> ObterAlimentosRecentesAsync(DateOnly hoje, int max = 8, CancellationToken ct = default);
    Task<ResultadoRegistro> AdicionarItemPlanoAsync(Refeicao refeicao, Guid alimentoId, int gramas, CancellationToken ct = default);
    Task<ResultadoRegistro> RemoverItemPlanoAsync(Guid itemId, CancellationToken ct = default);
    Task<MetasFitness> ObterMetasAsync(CancellationToken ct = default);
    Task SalvarMetasAsync(MetasFitness metas, CancellationToken ct = default);

    // Corpo
    Task<HistoricoPeso> ObterPesosAsync(int dias, DateOnly hoje, CancellationToken ct = default);
    Task<ResultadoRegistro> RegistrarPesoAsync(DateOnly dia, decimal kg, CancellationToken ct = default);
    Task<ResultadoRegistro> ExcluirPesoAsync(Guid id, CancellationToken ct = default);
    /// <summary>Altura, peso, IMC e as últimas medidas de cada tipo (da pessoa atual).</summary>
    Task<PainelMedidas> ObterPainelMedidasAsync(DateOnly hoje, CancellationToken ct = default);
    Task<ResultadoRegistro> SalvarAlturaAsync(int cm, CancellationToken ct = default);
    Task<ResultadoRegistro> RegistrarMedidaAsync(DateOnly dia, string tipo, decimal cm, CancellationToken ct = default);
}

public sealed record ResultadoBuscaOnline(bool Sucesso, string? Erro, IReadOnlyList<ProdutoOnline> Produtos);

/// <summary>Produtos com marca numa base aberta da internet (Open Food Facts). Só leitura; salvar é com <see cref="IFitnessServico"/>.</summary>
public interface IAlimentosOnline
{
    /// <summary>Nome do produto ou código de barras (8 a 14 dígitos).</summary>
    Task<ResultadoBuscaOnline> BuscarAsync(string texto, CancellationToken ct = default);
}
