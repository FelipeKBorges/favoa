namespace Favoa.Core.Pessoas;

/// <summary>
/// Quem usa o Fávoa na casa (ex.: Davi e Eva). Finanças e tarefas são da casa;
/// treino, dieta, peso, hábitos, água e lembretes são de cada pessoa.
/// </summary>
public sealed class Pessoa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "";
    /// <summary>Cor do círculo com a inicial (#RRGGBB).</summary>
    public string Cor { get; set; } = CoresPessoa.Padrao[0];
    public int Ordem { get; set; }

    /// <summary>Família (tenant) a que a pessoa pertence.</summary>
    public Guid? FamiliaId { get; set; }

    /// <summary>Legado do tempo do PIN (não é mais usado; o acesso agora é por <see cref="Favoa.Core.Acesso.Usuario"/>).</summary>
    public string? PinHash { get; set; }

    /// <summary>Legado do tempo do PIN.</summary>
    public int VersaoAcesso { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Registro que pertence a uma família (tenant). O banco filtra e preenche sozinho com a família atual.</summary>
public interface IDaFamilia
{
    /// <summary>Nulo só em catálogos comuns (alimentos da TACO/IBGE) e em dados anteriores às famílias.</summary>
    Guid? FamiliaId { get; set; }
}

/// <summary>Registro que pertence a uma pessoa. O banco preenche sozinho com a pessoa atual ao gravar.</summary>
public interface IComDono
{
    Guid? PessoaId { get; set; }
}

/// <summary>
/// Quem está usando agora. Nulo só em rotinas internas (exportar, agendador), que enxergam a casa toda;
/// nas telas é sempre uma pessoa.
/// </summary>
public interface IPessoaAtual
{
    Guid? Id { get; }

    /// <summary>Família de quem está usando. Nula = rotina interna que enxerga todas as famílias.</summary>
    Guid? FamiliaId => null;
}

/// <summary>Pessoa fixa: testes, MCP e rotinas de fundo (nulo = todas).</summary>
public sealed class PessoaFixa(Guid? id, Guid? familiaId = null) : IPessoaAtual
{
    public Guid? Id { get; set; } = id;
    public Guid? FamiliaId { get; set; } = familiaId;
}

public static class CoresPessoa
{
    public static readonly string[] Padrao = ["#F2A93B", "#7FC8B4", "#B9A3E3", "#F08A7E", "#8EB8F0", "#E8C468"];

    public static bool Valida(string? cor) =>
        cor is { Length: 7 } && cor[0] == '#' && cor[1..].All(Uri.IsHexDigit);

    public static string Inicial(string nome) =>
        string.IsNullOrWhiteSpace(nome) ? "?" : nome.Trim()[..1].ToUpperInvariant();
}

public sealed record PessoaInfo(Guid Id, string Nome, string Cor, int Ordem, bool TemAcesso)
{
    public string Inicial => CoresPessoa.Inicial(Nome);
}

/// <summary>Pessoa para escolher numa lista (responsável de tarefa).</summary>
public sealed record PessoaOpcao(Guid Id, string Nome, string Cor)
{
    public string Inicial => CoresPessoa.Inicial(Nome);
}
