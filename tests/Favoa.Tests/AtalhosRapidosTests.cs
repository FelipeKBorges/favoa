using Favoa.Core.Painel;
using Xunit;

namespace Favoa.Tests;

public class AtalhosRapidosTests
{
    [Fact]
    public void Sem_uso_mantem_a_ordem_padrao()
    {
        var r = AtalhosRapidos.Escolher(new UsoRecente(0, 0, 0, 0));
        Assert.Equal(["despesa", "agua", "treino"], r);
    }

    [Fact]
    public void Os_mais_usados_vem_primeiro()
    {
        var r = AtalhosRapidos.Escolher(new UsoRecente(1, 2, 9, 5));
        Assert.Equal(["treino", "peso", "agua"], r);
    }
}
