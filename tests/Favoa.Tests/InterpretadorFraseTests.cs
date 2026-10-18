using Favoa.Core.Captura;

namespace Favoa.Tests;

public class InterpretadorFraseTests
{
    // Sábado, 10/10/2026
    private static readonly DateOnly Hoje = new(2026, 10, 10);

    private static readonly ContextoCaptura Ctx = new(
        Hoje,
        Categorias: ["Mercado", "Transporte", "Alimentação fora", "Salário", "Outros"],
        Contas: ["Nubank", "Carteira"],
        Habitos: ["Ler 20 min", "Meditação", "Alongar"]);

    private static Interpretacao I(string frase) => InterpretadorFrase.Interpretar(frase, Ctx);

    [Fact]
    public void Despesa_com_categoria_e_conta_conhecidas()
    {
        var r = I("gastei 32 no mercado com o nubank");

        Assert.Equal(TipoCaptura.Despesa, r.Tipo);
        Assert.Equal(3200, r.ValorCentavos);
        Assert.Equal("Mercado", r.Categoria);
        Assert.Equal("Nubank", r.Conta);
        Assert.Equal(Hoje, r.Data);
        Assert.True(r.PodeSalvar);
    }

    [Theory]
    [InlineData("gastei 32 no mercado com o nubank", null)]
    [InlineData("gastei 45 em farmácia remédio de gripe", "Farmácia remédio de gripe")]
    [InlineData("gastei 180 no mercado compras do mês com o nubank", "Compras do mês")]
    [InlineData("paguei 60 de transporte uber pro aeroporto ontem", "Uber pro aeroporto")]
    [InlineData("recebi 800 de freela site da padaria", "Freela site da padaria")]
    [InlineData("gastei 32 reais no mercado", null)]
    [InlineData("comprei 9,90 de pão", null)]
    public void Descricao_e_o_que_sobra_da_frase(string frase, string? descricao)
    {
        Assert.Equal(descricao, I(frase).Descricao);
    }

    [Theory]
    [InlineData("gastei 32,50 no mercado", 3250)]
    [InlineData("paguei R$ 1.240,00 de aluguel", 124000)]
    [InlineData("comprei 9,9 de pão", 990)]
    [InlineData("45 mercado", 4500)]
    public void Le_valores_em_formato_brasileiro(string frase, long centavos)
    {
        Assert.Equal(centavos, I(frase).ValorCentavos);
    }

    [Fact]
    public void Categoria_nova_vem_da_preposicao_e_conta_vazia_fica_nula()
    {
        var r = I("paguei 120 de pet shop");

        Assert.Equal("Pet", r.Categoria);
        Assert.Null(r.Conta);
    }

    [Theory]
    [InlineData("uber 23", TipoCaptura.Despesa, 2300, "Transporte", "Uber")]
    [InlineData("mercado 150", TipoCaptura.Despesa, 15000, "Mercado", null)]
    [InlineData("ifood 62,90", TipoCaptura.Despesa, 6290, "Alimentação fora", "Ifood")]
    [InlineData("almoço 45", TipoCaptura.Despesa, 4500, "Alimentação fora", "Almoço")]
    [InlineData("aluguel 1800", TipoCaptura.Despesa, 180000, "Moradia", "Aluguel")]
    [InlineData("conta de luz 230", TipoCaptura.Despesa, 23000, "Moradia", "Luz")]
    [InlineData("farmácia 87", TipoCaptura.Despesa, 8700, "Saúde", "Farmácia")]
    [InlineData("netflix 55,90", TipoCaptura.Despesa, 5590, "Assinaturas", "Netflix")]
    [InlineData("salário 5000", TipoCaptura.Receita, 500000, "Salário", null)]
    [InlineData("freela 800 site da padaria", TipoCaptura.Receita, 80000, "Extra", "Freela site da padaria")]
    [InlineData("tênis 399", TipoCaptura.Despesa, 39900, "Outros", "Tênis")]
    [InlineData("30 de uber", TipoCaptura.Despesa, 3000, "Transporte", "Uber")]
    public void Frase_curta_com_valor_vira_lancamento(string frase, TipoCaptura tipo, long centavos, string categoria, string? descricao)
    {
        var r = I(frase);

        Assert.Equal(tipo, r.Tipo);
        Assert.Equal(centavos, r.ValorCentavos);
        Assert.Equal(categoria, r.Categoria);
        Assert.Equal(descricao, r.Descricao);
        Assert.True(r.PodeSalvar);
    }

    [Fact]
    public void Frase_curta_reconhece_a_conta()
    {
        var r = I("gasolina 200 nubank");

        Assert.Equal("Transporte", r.Categoria);
        Assert.Equal("Nubank", r.Conta);
        Assert.Equal("Gasolina", r.Descricao);
    }

    [Fact]
    public void Cartao_nao_vira_categoria()
    {
        var r = I("comprei tênis 399 no cartão nubank");

        Assert.Equal("Outros", r.Categoria);
        Assert.Equal("Nubank", r.Conta);
    }

    [Theory]
    [InlineData("ler 20 min")]
    [InlineData("estudei 2 horas")]
    [InlineData("caminhei por aí")]
    public void Medida_nao_vira_dinheiro(string frase)
    {
        Assert.NotEqual(TipoCaptura.Despesa, I(frase).Tipo);
    }

    [Theory]
    [InlineData("comprei um café na rua", false, "Alimentação fora")]
    [InlineData("salário de outubro", true, "Salário")]
    [InlineData("uber", true, null)]
    [InlineData("algo qualquer", false, null)]
    public void Sugere_categoria_pela_descricao(string texto, bool receita, string? categoria)
    {
        Assert.Equal(categoria, InterpretadorFrase.SugerirCategoria(texto, receita));
    }

    [Fact]
    public void Categoria_desconhecida_sem_pista_vira_outros()
    {
        Assert.Equal("Outros", I("gastei 15").Categoria);
    }

    [Fact]
    public void Ontem_muda_a_data()
    {
        Assert.Equal(Hoje.AddDays(-1), I("gastei 20 no mercado ontem").Data);
    }

    [Fact]
    public void Receita()
    {
        var r = I("recebi 5000 de salário na conta nubank");

        Assert.Equal(TipoCaptura.Receita, r.Tipo);
        Assert.Equal(500000, r.ValorCentavos);
        Assert.Equal("Salário", r.Categoria);
        Assert.Equal("Nubank", r.Conta);
    }

    [Fact]
    public void Despesa_sem_valor_avisa()
    {
        var r = I("gastei no mercado");

        Assert.False(r.PodeSalvar);
        Assert.NotNull(r.Aviso);
    }

    [Theory]
    [InlineData("bebi 500 ml de água", 500)]
    [InlineData("bebi 1,5 l", 1500)]
    [InlineData("água 300ml", 300)]
    public void Agua(string frase, int ml)
    {
        var r = I(frase);

        Assert.Equal(TipoCaptura.Agua, r.Tipo);
        Assert.Equal(ml, r.Mililitros);
    }

    [Fact]
    public void Tarefa_com_dia_da_semana()
    {
        var r = I("tarefa pagar condomínio sexta");

        Assert.Equal(TipoCaptura.Tarefa, r.Tipo);
        Assert.Equal("Pagar condomínio", r.Titulo);
        Assert.Equal(new DateOnly(2026, 10, 16), r.Prazo);
    }

    [Fact]
    public void Tarefa_com_data_e_sem_prazo()
    {
        Assert.Equal(new DateOnly(2026, 10, 20), I("tarefa revisar contrato até 20/10").Prazo);

        Assert.Equal(Hoje.AddDays(-1), I("tarefa pagar condomínio ontem").Prazo);

        var semPrazo = I("tarefa ligar para o contador");
        Assert.Null(semPrazo.Prazo);
        Assert.Equal("Ligar para o contador", semPrazo.Titulo);
    }

    [Fact]
    public void Habito_conhecido_sem_acento_e_com_verbo()
    {
        var r = I("fiz meditacao");

        Assert.Equal(TipoCaptura.Habito, r.Tipo);
        Assert.Equal("Meditação", r.Titulo);
        Assert.True(r.PodeSalvar);
    }

    [Fact]
    public void Habito_digitado_pelo_nome()
    {
        Assert.Equal("Ler 20 min", I("ler 20 min").Titulo);
    }

    [Fact]
    public void Habito_desconhecido_sugere_criar()
    {
        var r = I("fiz yoga");

        Assert.False(r.PodeSalvar);
        Assert.Contains("novo hábito", r.Aviso);
    }

    [Fact]
    public void Novo_habito_nova_conta_e_orcamento()
    {
        Assert.Equal("Beber água ao acordar", I("novo hábito Beber água ao acordar").Titulo);
        Assert.Equal("Itaú", I("nova conta Itaú").Titulo);

        var orc = I("orçamento 3000");
        Assert.Equal(TipoCaptura.Orcamento, orc.Tipo);
        Assert.Equal(300000, orc.ValorCentavos);
    }

    [Theory]
    [InlineData("supino 3x10 40kg", "Supino", 3, 10, "40")]
    [InlineData("supino reto 4x8 com 62,5 kg", "Supino reto", 4, 8, "62.5")]
    [InlineData("fiz agachamento 5x5 100", "Agachamento", 5, 5, "100")]
    [InlineData("prancha 3x30", "Prancha", 3, 30, null)]
    public void Exercicio_de_forca(string frase, string nome, int series, int reps, string? carga)
    {
        var r = I(frase);

        Assert.Equal(TipoCaptura.Treino, r.Tipo);
        Assert.True(r.PodeSalvar);
        Assert.Equal(nome, r.Titulo);
        Assert.Equal(series, r.Series);
        Assert.Equal(reps, r.Repeticoes);
        Assert.Equal(carga is null ? null : decimal.Parse(carga, System.Globalization.CultureInfo.InvariantCulture), r.CargaKg);
    }

    [Theory]
    [InlineData("corri 5 km em 30 min", "Corrida", "5", 30)]
    [InlineData("pedalei 1h20min", "Bike", null, 80)]
    [InlineData("esteira 25 minutos", "Esteira", null, 25)]
    [InlineData("caminhei 3,5km", "Caminhada", "3.5", null)]
    public void Cardio(string frase, string nome, string? km, int? minutos)
    {
        var r = I(frase);

        Assert.Equal(TipoCaptura.Cardio, r.Tipo);
        Assert.Equal(nome, r.Titulo);
        Assert.Equal(km is null ? null : decimal.Parse(km, System.Globalization.CultureInfo.InvariantCulture), r.DistanciaKm);
        Assert.Equal(minutos, r.Minutos);
    }

    [Fact]
    public void Cardio_sem_distancia_nem_tempo_pede_mais()
    {
        var r = I("corri hoje");
        Assert.Equal(TipoCaptura.Cardio, r.Tipo);
        Assert.False(r.PodeSalvar);
    }

    [Theory]
    [InlineData("peso 82,5", "82.5")]
    [InlineData("pesei 79kg", "79")]
    [InlineData("meu peso hoje 101,3 kg", "101.3")]
    public void Peso(string frase, string kg)
    {
        var r = I(frase);
        Assert.Equal(TipoCaptura.Peso, r.Tipo);
        Assert.Equal(decimal.Parse(kg, System.Globalization.CultureInfo.InvariantCulture), r.PesoKg);
    }

    [Theory]
    [InlineData("comi 150g de arroz", "arroz", 150, null, null)]
    [InlineData("comi 200 g de frango grelhado no almoço", "frango grelhado", 200, null, Favoa.Core.Fitness.Refeicao.Almoco)]
    [InlineData("almocei 120g de feijão", "feijão", 120, null, Favoa.Core.Fitness.Refeicao.Almoco)]
    [InlineData("no café da manhã comi 2 ovos", null, null, null, null)] // começa com "no": não é refeição, cai no aviso padrão
    [InlineData("comi 2 ovos no café da manhã", "ovos", null, "2", Favoa.Core.Fitness.Refeicao.CafeDaManha)]
    [InlineData("tomei 300 ml de leite na janta", "leite", 300, null, Favoa.Core.Fitness.Refeicao.Jantar)]
    [InlineData("tomei 1 scoop de whey", "whey", null, "1", null)]
    [InlineData("comi 0,5 kg de melancia", "melancia", 500, null, null)]
    public void Refeicao(string frase, string? alimento, int? gramas, string? unidades, Favoa.Core.Fitness.Refeicao? refeicao)
    {
        var r = I(frase);
        if (alimento is null) { Assert.NotEqual(TipoCaptura.Refeicao, r.Tipo); return; }

        Assert.Equal(TipoCaptura.Refeicao, r.Tipo);
        Assert.True(r.PodeSalvar, r.Aviso);
        Assert.Equal(alimento, r.Titulo);
        Assert.Equal(gramas, r.Gramas);
        Assert.Equal(unidades is null ? null : decimal.Parse(unidades), r.Unidades);
        Assert.Equal(refeicao, r.Refeicao);
    }

    [Fact]
    public void Refeicao_sem_quantidade_pede_a_quantidade()
    {
        var r = I("comi arroz");
        Assert.Equal(TipoCaptura.Refeicao, r.Tipo);
        Assert.False(r.PodeSalvar);
    }

    [Fact]
    public void Frase_vazia_ou_desconhecida()
    {
        Assert.Equal(TipoCaptura.Desconhecido, I("   ").Tipo);
        Assert.False(I("bom dia").PodeSalvar);
    }
}

public class LerMoedaTests
{
    [Theory]
    [InlineData("32", 3200L)]
    [InlineData("32,5", 3250L)]
    [InlineData("32,50", 3250L)]
    [InlineData("32.50", 3250L)]
    [InlineData("R$ 1.240,00", 124000L)]
    [InlineData("1.240", 124000L)]
    [InlineData(" 9,9 ", 990L)]
    [InlineData("0", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData("-5", null)]
    public void Le_valor_digitado(string texto, long? centavos) => Assert.Equal(centavos, Texto.LerMoeda(texto));
}
