using System.Text.Json;
using System.Text.Json.Nodes;

namespace Favoa.Mcp;

/// <summary>
/// O mínimo do protocolo MCP (JSON-RPC 2.0, uma mensagem por linha) para um cliente MCP usar o Fávoa.
/// Sem pacotes externos: initialize, ping, tools/list e tools/call.
/// </summary>
public sealed class ServidorMcp(Ferramentas ferramentas)
{
    private static readonly string[] VersoesSuportadas = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

    public const string Instrucoes =
        "Fávoa é o painel pessoal de vida do usuário (finanças, tarefas, hábitos, treino, dieta, peso), guardado no computador dele. " +
        "Leia antes de propor (resumo_do_dia, dieta_do_dia, buscar_alimentos, treinos). " +
        "Ferramentas que gravam (registrar, definir_plano_refeicao, salvar_ficha, definir_metas, cadastrar_alimento) só depois de o usuário confirmar a proposta. " +
        "Use os ids curtos que as ferramentas devolvem. Valores de alimentos são por 100 g (Tabela TACO ou cadastro do usuário).";

    // Acentos sem escape: mensagens menores e legíveis no log.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Processa uma linha recebida. Devolve a linha de resposta, ou nulo para notificações.</summary>
    public async Task<string?> ProcessarAsync(string linha, CancellationToken ct = default)
    {
        JsonNode? mensagem;
        try { mensagem = JsonNode.Parse(linha); }
        catch (JsonException) { return Erro(null, -32700, "JSON inválido"); }

        if (mensagem is not JsonObject obj) return Erro(null, -32600, "Requisição inválida");

        var id = obj["id"]?.DeepClone();
        var metodo = obj["method"]?.GetValue<string>();
        var parametros = obj["params"] as JsonObject;

        // Notificações (sem id) não têm resposta.
        if (id is null) return null;
        if (metodo is null) return Erro(id, -32600, "Requisição inválida");

        try
        {
            JsonNode resultado = metodo switch
            {
                "initialize" => Inicializar(parametros),
                "ping" => new JsonObject(),
                "tools/list" => new JsonObject { ["tools"] = ferramentas.Listar() },
                "tools/call" => await Chamar(parametros, ct),
                _ => throw new MetodoDesconhecido(metodo),
            };
            return Resposta(id, resultado);
        }
        catch (MetodoDesconhecido e)
        {
            return Erro(id, -32601, $"Método não suportado: {e.Message}");
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException)
        {
            return Erro(id, -32602, e.Message);
        }
    }

    private static JsonObject Inicializar(JsonObject? p)
    {
        var pedida = p?["protocolVersion"]?.GetValue<string>();
        return new JsonObject
        {
            ["protocolVersion"] = VersoesSuportadas.Contains(pedida) ? pedida : VersoesSuportadas[1],
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "favoa", ["title"] = "Fávoa", ["version"] = "1.0.0" },
            ["instructions"] = Instrucoes,
        };
    }

    private async Task<JsonObject> Chamar(JsonObject? p, CancellationToken ct)
    {
        var nome = p?["name"]?.GetValue<string>() ?? throw new ArgumentException("Faltou o nome da ferramenta.");
        var argumentos = p["arguments"] as JsonObject ?? new JsonObject();

        // Erros de uso viram resposta normal com isError, para o cliente ler e corrigir.
        var (texto, erro) = await ferramentas.ExecutarAsync(nome, argumentos, ct);
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = texto }),
            ["isError"] = erro,
        };
    }

    private static string Resposta(JsonNode id, JsonNode resultado) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = resultado }.ToJsonString(Json);

    private static string Erro(JsonNode? id, int codigo, string mensagem) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = codigo, ["message"] = mensagem },
        }.ToJsonString(Json);

    private sealed class MetodoDesconhecido(string metodo) : Exception(metodo);
}
