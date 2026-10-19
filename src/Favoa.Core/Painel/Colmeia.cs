using Favoa.Core.Captura;

namespace Favoa.Core.Painel;

public enum EstadoCelula
{
    Ativa,
    Alerta,
    EmBreve,
}

/// <summary>Uma célula hexagonal da colmeia. Preenchimento de 0 a 1 = quanto "mel" ela tem.</summary>
public sealed record CelulaColmeia(
    string Chave,
    string Rotulo,
    string Valor,
    string Detalhe,
    double Preenchimento,
    EstadoCelula Estado = EstadoCelula.Ativa)
{
    public const string Hoje = "hoje";
    public const string Financas = "financas";
    public const string Tarefas = "tarefas";
    public const string Habitos = "habitos";
    public const string Alimentacao = "alimentacao";
    public const string Treino = "treino";
    public const string Projetos = "projetos";

    public int Percentual => (int)Math.Round(Preenchimento * 100);
}

/// <summary>
/// Regra da colmeia: cada área ativa vira uma célula; o centro "Hoje" é a média delas.
/// Uma célula é "cheia" com 100%. Áreas ainda não construídas aparecem como "em breve".
/// </summary>
public static class CalculadoraColmeia
{
    public static IReadOnlyList<CelulaColmeia> Calcular(ResumoDoDia r)
    {
        var financas = CelulaFinancas(r);
        var tarefas = CelulaTarefas(r);
        var habitos = CelulaHabitos(r);
        var agua = CelulaAlimentacao(r);
        var treino = CelulaTreino(r);

        // Treino só entra na média depois do primeiro treino registrado.
        var ativas = r.TemTreinos
            ? new[] { financas, tarefas, habitos, agua, treino }
            : new[] { financas, tarefas, habitos, agua };
        var media = ativas.Average(c => c.Preenchimento);
        var cheias = ativas.Count(c => c.Preenchimento >= 0.999);

        var centro = new CelulaColmeia(
            CelulaColmeia.Hoje, "Hoje", $"{cheias} de {ativas.Length}", "metas do dia", media);

        return
        [
            financas,
            agua,
            tarefas,
            centro,
            CelulaProjetos(r),
            habitos,
            treino,
        ];
    }

    private static CelulaColmeia CelulaFinancas(ResumoDoDia r)
    {
        if (r.OrcamentoMensalCentavos <= 0)
            return new(CelulaColmeia.Financas, "Finanças", Texto.FormatarMoeda(r.DespesasDoMesCentavos),
                "gastos no mês · defina um orçamento", 0, EstadoCelula.Alerta);

        var livre = r.OrcamentoMensalCentavos - r.DespesasDoMesCentavos;
        var fill = Limitar((double)livre / r.OrcamentoMensalCentavos);
        return livre < 0
            ? new(CelulaColmeia.Financas, "Finanças", Texto.FormatarMoeda(-livre), "acima do orçamento", 0, EstadoCelula.Alerta)
            : new(CelulaColmeia.Financas, "Finanças", Texto.FormatarMoeda(livre), "livres no mês", fill);
    }

    private static CelulaColmeia CelulaTarefas(ResumoDoDia r)
    {
        var total = r.TarefasConcluidasHoje + r.TarefasParaHoje + r.TarefasAtrasadas;
        if (total == 0 && r.SemTarefasCadastradas)
            return new(CelulaColmeia.Tarefas, "Tarefas", "Nenhuma", "crie a primeira", 0);
        if (total == 0)
            return new(CelulaColmeia.Tarefas, "Tarefas", "Livre", "nada para hoje", 1);

        var fill = (double)r.TarefasConcluidasHoje / total;
        var pendentes = r.TarefasParaHoje + r.TarefasAtrasadas;
        return r.TarefasAtrasadas > 0
            ? new(CelulaColmeia.Tarefas, "Tarefas", $"{pendentes} hoje", Plural(r.TarefasAtrasadas, "atrasada", "atrasadas"), fill, EstadoCelula.Alerta)
            : new(CelulaColmeia.Tarefas, "Tarefas", pendentes == 0 ? "Feito" : $"{pendentes} hoje", $"{r.TarefasConcluidasHoje} concluída(s)", fill);
    }

    private static CelulaColmeia CelulaHabitos(ResumoDoDia r)
    {
        if (r.HabitosAtivos == 0)
            return new(CelulaColmeia.Habitos, "Hábitos", "Nenhum", "crie com: novo hábito ler", 0, EstadoCelula.Alerta);

        var detalhe = r.MelhorSequenciaDias > 1 ? $"{r.MelhorSequenciaDias} dias seguidos" : "comece uma sequência";
        return new(CelulaColmeia.Habitos, "Hábitos", $"{r.HabitosFeitosHoje} de {r.HabitosAtivos}", detalhe,
            (double)r.HabitosFeitosHoje / r.HabitosAtivos);
    }

    private static CelulaColmeia CelulaAlimentacao(ResumoDoDia r)
    {
        var metaAgua = r.MetaAguaMl <= 0 ? 2500 : r.MetaAguaMl;
        var fracAgua = Limitar((double)r.AguaHojeMl / metaAgua);

        if (!r.TemConsumoHoje)
            return new(CelulaColmeia.Alimentacao, "Alimentação", Litros(r.AguaHojeMl), $"de {Litros(metaAgua)} de água", fracAgua);

        var kcal = $"{r.KcalHoje:N0} kcal".Replace(',', '.');
        if (r.MetaKcal <= 0)
            return new(CelulaColmeia.Alimentacao, "Alimentação", kcal, $"{Litros(r.AguaHojeMl)} de água · defina a meta", fracAgua);

        // Metade água, metade calorias. Passar mais de 10% da meta acende o alerta.
        var fracKcal = (double)r.KcalHoje / r.MetaKcal;
        var detalhe = $"de {r.MetaKcal:N0} · {Litros(r.AguaHojeMl)} água".Replace(',', '.');
        return fracKcal > 1.1
            ? new(CelulaColmeia.Alimentacao, "Alimentação", kcal, $"acima da meta de {r.MetaKcal:N0}".Replace(',', '.'), (fracAgua + 1) / 2, EstadoCelula.Alerta)
            : new(CelulaColmeia.Alimentacao, "Alimentação", kcal, detalhe, (fracAgua + Limitar(fracKcal)) / 2);
    }

    private static CelulaColmeia CelulaTreino(ResumoDoDia r)
    {
        if (!r.TemTreinos)
            return new(CelulaColmeia.Treino, "Treino", "Nenhum", "diga: supino 3x10 40kg", 0, EstadoCelula.Alerta);

        var meta = Math.Max(1, r.MetaTreinosSemana);
        return new(CelulaColmeia.Treino, "Treino", $"{r.TreinosUltimos7Dias} de {meta}",
            r.TreinouHoje ? "treinou hoje" : "treinos nos últimos 7 dias",
            Limitar((double)r.TreinosUltimos7Dias / meta));
    }

    /// <summary>Não entra na média do dia: mostra o andamento dos projetos ativos e o mais próximo.</summary>
    private static CelulaColmeia CelulaProjetos(ResumoDoDia r)
    {
        if (r.ProjetosAtivos == 0)
            return new(CelulaColmeia.Projetos, "Projetos", "Nenhum", "viagem, trabalho, casa", 0, EstadoCelula.EmBreve);

        var detalhe = r.ProjetoMaisProximo is null ? Plural(r.ProjetosAtivos, "projeto ativo", "projetos ativos")
            : r.DiasProjetoMaisProximo is { } d ? $"{r.ProjetoMaisProximo} · {(d == 0 ? "hoje" : d == 1 ? "amanhã" : $"em {d} dias")}"
            : r.ProjetoMaisProximo;
        return new(CelulaColmeia.Projetos, "Projetos", $"{Math.Round(r.ProgressoProjetos * 100):0}%", detalhe, Limitar(r.ProgressoProjetos));
    }

    private static string Litros(int ml) =>
        ml < 1000 ? $"{ml} ml" : $"{ml / 1000.0:0.#} L".Replace('.', ',');

    private static string Plural(int n, string um, string varios) => $"{n} {(n == 1 ? um : varios)}";

    private static double Limitar(double v) => Math.Clamp(v, 0, 1);
}
