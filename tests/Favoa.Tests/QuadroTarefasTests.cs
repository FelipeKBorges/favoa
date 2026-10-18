using Favoa.Core.Rotina;

namespace Favoa.Tests;

public sealed class QuadroTarefasTests
{
    private static readonly DateOnly Hoje = new(2026, 10, 10); // sábado

    private static Tarefa T(string titulo, DateOnly? prazo = null, DateOnly? concluida = null) =>
        new() { Titulo = titulo, Prazo = prazo, ConcluidaEm = concluida };

    [Fact]
    public void Separa_em_grupos_e_ordena_por_prazo()
    {
        var q = QuadroTarefas.Montar(
        [
            T("Renovar CNH", Hoje.AddDays(-3)),
            T("Pagar luz", Hoje.AddDays(-1)),
            T("Mercado", Hoje),
            T("Dentista", Hoje.AddDays(5)),
            T("IPVA", Hoje.AddDays(2)),
            T("Ler livro"),
            T("Feita hoje", Hoje, concluida: Hoje),
        ], Hoje);

        Assert.Equal(["Renovar CNH", "Pagar luz"], q.Atrasadas.Select(t => t.Titulo));
        Assert.All(q.Atrasadas, t => Assert.True(t.Atrasada));
        Assert.Equal(["Mercado"], q.ParaHoje.Select(t => t.Titulo));
        Assert.Equal(["IPVA", "Dentista"], q.Proximas.Select(t => t.Titulo));
        Assert.Equal(["Ler livro"], q.SemPrazo.Select(t => t.Titulo));
        Assert.Equal(["Feita hoje"], q.Concluidas.Select(t => t.Titulo));
        Assert.Equal(6, q.Pendentes);
    }

    [Fact]
    public void Concluidas_antigas_saem_e_contagem_da_semana()
    {
        var q = QuadroTarefas.Montar(
        [
            T("Ontem", concluida: Hoje.AddDays(-1)),
            T("Há 6 dias", concluida: Hoje.AddDays(-6)),
            T("Há 7 dias", concluida: Hoje.AddDays(-7)),
            T("Há 20 dias", concluida: Hoje.AddDays(-20)),
        ], Hoje);

        Assert.Equal(["Ontem", "Há 6 dias", "Há 7 dias"], q.Concluidas.Select(t => t.Titulo));
        Assert.Equal(2, q.FeitasUltimos7Dias);
    }

    [Fact]
    public void Atalhos_de_prazo_no_sabado()
    {
        var a = AtalhosPrazo.Para(Hoje);

        Assert.Equal(["Hoje", "Amanhã", "Próxima semana", "Sem prazo"], a.Select(x => x.Rotulo));
        Assert.Equal(new DateOnly(2026, 10, 12), a[2].Prazo); // segunda
        Assert.Null(a[3].Prazo);
    }

    [Fact]
    public void Atalhos_de_prazo_na_segunda_incluem_sabado_e_pulam_para_a_outra_segunda()
    {
        var segunda = new DateOnly(2026, 10, 12);
        var a = AtalhosPrazo.Para(segunda);

        Assert.Contains(a, x => x.Rotulo == "Sábado" && x.Prazo == new DateOnly(2026, 10, 17));
        Assert.Equal(new DateOnly(2026, 10, 19), a.Single(x => x.Rotulo == "Próxima semana").Prazo);
    }
}
