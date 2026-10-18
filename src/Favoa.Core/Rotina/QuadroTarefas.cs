namespace Favoa.Core.Rotina;

public sealed record TarefaItem(Guid Id, string Titulo, DateOnly? Prazo, DateOnly? ConcluidaEm, bool Atrasada)
{
    public bool Concluida => ConcluidaEm is not null;
    public TimeOnly? Hora { get; init; }
    public int? AvisoMinutos { get; init; }
    /// <summary>Quem cuida. Vazio = a casa.</summary>
    public IReadOnlyList<Pessoas.PessoaOpcao> Responsaveis { get; init; } = [];

    /// <summary>É da pessoa: ela está entre os responsáveis ou a tarefa é da casa.</summary>
    public bool EhDe(Guid? pessoa) => Responsaveis.Count == 0 || Responsaveis.Any(r => r.Id == pessoa);
}

/// <summary>As tarefas organizadas do jeito que a tela mostra. Montado em memória, sem banco.</summary>
public sealed record QuadroTarefas(
    DateOnly Hoje,
    IReadOnlyList<TarefaItem> Atrasadas,
    IReadOnlyList<TarefaItem> ParaHoje,
    IReadOnlyList<TarefaItem> Proximas,
    IReadOnlyList<TarefaItem> SemPrazo,
    IReadOnlyList<TarefaItem> Concluidas,
    int FeitasUltimos7Dias)
{
    /// <summary>Quantos dias de concluídas a tela mostra.</summary>
    public const int DiasDeConcluidas = 14;

    public int Pendentes => Atrasadas.Count + ParaHoje.Count + Proximas.Count + SemPrazo.Count;

    public static QuadroTarefas Montar(IEnumerable<Tarefa> tarefas, DateOnly hoje, IReadOnlyList<Pessoas.PessoaOpcao>? pessoas = null)
    {
        var porId = (pessoas ?? []).ToDictionary(p => p.Id);
        var itens = tarefas
            .Select(t => new TarefaItem(t.Id, t.Titulo, t.Prazo, t.ConcluidaEm, t.EstaAtrasada(hoje))
            {
                Hora = t.Hora,
                AvisoMinutos = t.AvisoMinutos,
                Responsaveis = t.Responsaveis.Select(r => porId.GetValueOrDefault(r.PessoaId)).OfType<Pessoas.PessoaOpcao>()
                    .OrderBy(p => (pessoas ?? []).ToList().IndexOf(p)).ToList(),
            })
            .ToList();
        var pendentes = itens.Where(t => !t.Concluida).ToList();

        return new QuadroTarefas(
            hoje,
            Atrasadas: pendentes.Where(t => t.Prazo < hoje).OrderBy(t => t.Prazo).ThenBy(t => t.Titulo).ToList(),
            ParaHoje: pendentes.Where(t => t.Prazo == hoje).OrderBy(t => t.Hora is null).ThenBy(t => t.Hora).ThenBy(t => t.Titulo).ToList(),
            Proximas: pendentes.Where(t => t.Prazo > hoje).OrderBy(t => t.Prazo).ThenBy(t => t.Hora is null).ThenBy(t => t.Hora).ThenBy(t => t.Titulo).ToList(),
            SemPrazo: pendentes.Where(t => t.Prazo is null).OrderBy(t => t.Titulo).ToList(),
            Concluidas: itens
                .Where(t => t.ConcluidaEm >= hoje.AddDays(-(DiasDeConcluidas - 1)))
                .OrderByDescending(t => t.ConcluidaEm).ThenBy(t => t.Titulo)
                .ToList(),
            FeitasUltimos7Dias: itens.Count(t => t.ConcluidaEm >= hoje.AddDays(-6)));
    }
}

/// <summary>Atalhos de prazo para mudar com um clique.</summary>
public static class AtalhosPrazo
{
    public sealed record Atalho(string Rotulo, DateOnly? Prazo);

    public static IReadOnlyList<Atalho> Para(DateOnly hoje)
    {
        // "Próxima semana" = a próxima segunda-feira (nunca hoje).
        var dias = ((int)DayOfWeek.Monday - (int)hoje.DayOfWeek + 7) % 7;
        var segunda = hoje.AddDays(dias == 0 ? 7 : dias);

        var lista = new List<Atalho> { new("Hoje", hoje), new("Amanhã", hoje.AddDays(1)) };
        // Fim de semana só faz sentido de segunda a quinta.
        if (hoje.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Thursday)
            lista.Add(new("Sábado", hoje.AddDays(DayOfWeek.Saturday - hoje.DayOfWeek)));
        lista.Add(new("Próxima semana", segunda));
        lista.Add(new("Sem prazo", null));
        return lista;
    }
}
