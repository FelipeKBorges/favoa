using Favoa.Core.Captura;

namespace Favoa.Core.Fitness;

/// <summary>Adivinha o grupo muscular pelo nome, para o exercício criado na hora já vir organizado.</summary>
public static class GruposMusculares
{
    public static readonly string[] Todos = ["Peito", "Costas", "Pernas", "Ombros", "Braços", "Core", "Cardio", "Outros"];

    private static readonly (string Grupo, string[] Palavras)[] Regras =
    [
        ("Cardio", ["corrida", "caminhada", "bike", "bicicleta", "esteira", "eliptico", "natacao", "remo ergometro", "pular corda", "escada", "spinning", "hiit", "cardio", "muay", "thai", "futebol", "pelada"]),
        ("Peito", ["supino", "crucifixo", "peck", "voador", "flexao", "crossover", "peitoral"]),
        ("Costas", ["remada", "puxada", "barra fixa", "pulldown", "pull down", "serrote", "levantamento terra", "terra", "costas", "pullover"]),
        ("Pernas", ["agachamento", "leg press", "leg", "extensora", "flexora", "stiff", "panturrilha", "afundo", "passada", "cadeira", "mesa flexora", "gluteo", "elevacao pelvica", "hack", "bulgaro", "abdutora", "adutora"]),
        ("Ombros", ["desenvolvimento", "elevacao lateral", "elevacao frontal", "arnold", "encolhimento", "face pull", "ombro"]),
        ("Braços", ["rosca", "triceps", "biceps", "martelo", "testa", "frances", "antebraco", "mergulho"]),
        ("Core", ["abdominal", "prancha", "abdomen", "core", "lombar", "infra", "obliquo"]),
    ];

    public static string Inferir(string nome)
    {
        var n = Texto.Normalizar(nome);
        foreach (var (grupo, palavras) in Regras)
            if (palavras.Any(p => n.Contains(p))) return grupo;
        return "Outros";
    }

    public static TipoExercicio TipoPorGrupo(string grupo) => grupo == "Cardio" ? TipoExercicio.Cardio : TipoExercicio.Forca;
}
