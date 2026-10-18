using System.Text.Json.Nodes;
using Favoa.Core.Fitness;
using Favoa.Data;
using Favoa.Mcp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Favoa.Tests;

/// <summary>O servidor MCP de ponta a ponta: mensagens JSON-RPC entrando e saindo, contra um banco real em memória.</summary>
public sealed class McpTests : IAsyncLifetime
{
    private static readonly DateOnly Hoje = new(2026, 10, 10);
    private readonly SqliteConnection _conexao = new("Data Source=:memory:");
    private ServiceProvider _sp = null!;
    private ServidorMcp _servidor = null!;
    private FitnessServico _fit = null!;
    private int _id;

    public async Task InitializeAsync()
    {
        await _conexao.OpenAsync();
        _sp = new ServiceCollection().AddDbContextFactory<FavoaDbContext>(o => o.UseSqlite(_conexao)).BuildServiceProvider();
        await _sp.PrepararBancoComCasaAsync();
        var fabrica = _sp.GetRequiredService<IDbContextFactory<FavoaDbContext>>();
        // Mesma instância de pessoa para os serviços e as ferramentas: cada chamada escolhe a pessoa.
        var pessoa = new Favoa.Core.Pessoas.PessoaFixa(null);
        _fit = new FitnessServico(fabrica, pessoa);
        _servidor = new ServidorMcp(new Ferramentas(new FavoaServico(fabrica, pessoa), _fit, fabrica, pessoa, () => Hoje));
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conexao.DisposeAsync();
    }

    private async Task<JsonObject> Enviar(string metodo, JsonObject? parametros = null)
    {
        var msg = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = ++_id, ["method"] = metodo };
        if (parametros is not null) msg["params"] = parametros;
        var resposta = await _servidor.ProcessarAsync(msg.ToJsonString());
        return JsonNode.Parse(resposta!)!.AsObject();
    }

    private async Task<(string Texto, bool Erro)> Chamar(string ferramenta, JsonObject? args = null)
    {
        var r = await Enviar("tools/call", new JsonObject { ["name"] = ferramenta, ["arguments"] = args ?? new JsonObject() });
        var resultado = r["result"]!;
        return (resultado["content"]![0]!["text"]!.GetValue<string>(), resultado["isError"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Inicializa_com_a_versao_pedida_e_lista_ferramentas()
    {
        var init = await Enviar("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject(), ["clientInfo"] = new JsonObject { ["name"] = "teste" } });
        Assert.Equal("2025-06-18", init["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("favoa", init["result"]!["serverInfo"]!["name"]!.GetValue<string>());

        Assert.Null(await _servidor.ProcessarAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));

        var lista = await Enviar("tools/list");
        var nomes = lista["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("buscar_alimentos", nomes);
        Assert.Contains("definir_plano_refeicao", nomes);
        Assert.All(lista["result"]!["tools"]!.AsArray(), t => Assert.Equal("object", t!["inputSchema"]!["type"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Metodo_desconhecido_e_json_invalido_viram_erro_do_protocolo()
    {
        Assert.Equal(-32601, (await Enviar("recursos/qualquer"))["error"]!["code"]!.GetValue<int>());
        Assert.Contains("-32700", await _servidor.ProcessarAsync("{nao é json"));
    }

    [Fact]
    public async Task Montar_plano_de_ganho_de_peso_pelo_fluxo_do_mcp()
    {
        // 1. O cliente busca os alimentos e recebe ids curtos.
        var (busca, erroBusca) = await Chamar("buscar_alimentos", new JsonObject { ["texto"] = "aveia flocos", ["limite"] = 3 });
        Assert.False(erroBusca);
        var aveia = busca.Split('\n')[1].Split(" | ")[0];
        Assert.Equal(8, aveia.Length);
        var banana = (await Chamar("buscar_alimentos", new JsonObject { ["texto"] = "banana prata" })).Texto.Split('\n')[1].Split(" | ")[0];

        // 2. Define metas e o plano do café, depois de o usuário aprovar.
        Assert.False((await Chamar("definir_metas", new JsonObject { ["kcal"] = 3000, ["proteina_g"] = 160 })).Erro);
        var (plano, erroPlano) = await Chamar("definir_plano_refeicao", new JsonObject
        {
            ["refeicao"] = "cafe_da_manha",
            ["itens"] = new JsonArray(
                new JsonObject { ["alimento_id"] = aveia, ["gramas"] = 80 },
                new JsonObject { ["alimento_id"] = banana, ["gramas"] = 120 }),
        });
        Assert.False(erroPlano, plano);
        Assert.Contains("kcal", plano);

        // 3. Refazer substitui, não duplica.
        await Chamar("definir_plano_refeicao", new JsonObject
        {
            ["refeicao"] = "cafe_da_manha",
            ["itens"] = new JsonArray(new JsonObject { ["alimento_id"] = aveia, ["gramas"] = 100 }),
        });
        var cafe = (await _fit.ObterDiaDietaAsync(Hoje)).Refeicoes.Single(r => r.Refeicao == Refeicao.CafeDaManha);
        Assert.Equal(100, cafe.Plano.Single().Gramas);

        var (dieta, _) = await Chamar("dieta_do_dia");
        Assert.Contains("Metas: 3000 kcal", dieta);
        Assert.Contains("cafe_da_manha", dieta);
    }

    [Fact]
    public async Task Erros_de_uso_voltam_como_texto_para_o_cliente_corrigir()
    {
        var (t1, e1) = await Chamar("definir_plano_refeicao", new JsonObject { ["refeicao"] = "brunch", ["itens"] = new JsonArray() });
        Assert.True(e1);
        Assert.Contains("cafe_da_manha", t1);

        var (t2, e2) = await Chamar("definir_plano_refeicao", new JsonObject
        {
            ["refeicao"] = "almoco",
            ["itens"] = new JsonArray(new JsonObject { ["alimento_id"] = "ffffffff", ["gramas"] = 100 }),
        });
        Assert.True(e2);
        Assert.Contains("Não achei", t2);

        Assert.True((await Chamar("registrar", new JsonObject { ["frase"] = "bom dia" })).Erro);
        Assert.True((await Chamar("ferramenta_que_nao_existe")).Erro);
    }

    [Fact]
    public async Task Registrar_ficha_e_resumo()
    {
        Assert.False((await Chamar("registrar", new JsonObject { ["frase"] = "gastei 50 no mercado" })).Erro);
        var (ficha, erro) = await Chamar("salvar_ficha", new JsonObject
        {
            ["nome"] = "Treino A",
            ["itens"] = new JsonArray(
                new JsonObject { ["exercicio"] = "Supino reto", ["series"] = 4, ["repeticoes"] = 8, ["carga_kg"] = 50 },
                new JsonObject { ["exercicio"] = "Esteira", ["minutos"] = 15 }),
        });
        Assert.False(erro, ficha);

        var (treinos, _) = await Chamar("treinos");
        Assert.Contains("Supino reto 4x8 50,0 kg", treinos);
        Assert.Contains("Esteira 15 min", treinos); // virou cardio pelo nome

        var (resumo, _) = await Chamar("resumo_do_dia");
        Assert.Contains("despesas R$ 50,00", resumo);

        var (fin, _) = await Chamar("financas_do_mes");
        Assert.Contains("Mercado", fin);
    }
}
