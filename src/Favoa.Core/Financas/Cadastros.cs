namespace Favoa.Core.Financas;

public sealed record CategoriaCadastro(Guid Id, string Nome, TipoLancamento Tipo, long? OrcamentoCentavos, int Lancamentos, long GastoMesCentavos)
{
    public double? FracaoOrcamento => OrcamentoCentavos is > 0 and var o ? (double)GastoMesCentavos / o : null;
}

public sealed record ContaCadastro(Guid Id, string Nome, bool Ativa, int Lancamentos, long SaldoCentavos);

public sealed record RecorrenciaCadastro(
    Guid Id, TipoLancamento Tipo, string Descricao, long ValorCentavos, string Categoria, string Conta,
    int DiaDoMes, bool Ativa, DateOnly Proxima, DateOnly? UltimaOcorrencia);

/// <summary>Dados do formulário de recorrência. <paramref name="Id"/> nulo = nova.</summary>
public sealed record DadosRecorrencia(
    Guid? Id, TipoLancamento Tipo, string Descricao, long ValorCentavos, string Categoria, string? Conta, int DiaDoMes, DateOnly Inicio);

public sealed record QuadroCadastros(
    IReadOnlyList<CategoriaCadastro> Categorias,
    IReadOnlyList<ContaCadastro> Contas,
    IReadOnlyList<RecorrenciaCadastro> Recorrencias);

/// <summary>Contas, categorias (com orçamento) e recorrências: a tela de Cadastros.</summary>
public interface ICadastrosServico
{
    Task<QuadroCadastros> ObterAsync(DateOnly hoje, CancellationToken ct = default);

    /// <summary>Cria (id nulo) ou renomeia e ajusta o orçamento. Orçamento só vale para despesas.</summary>
    Task<ResultadoRegistro> SalvarCategoriaAsync(Guid? id, string nome, TipoLancamento tipo, long? orcamentoCentavos, CancellationToken ct = default);

    /// <summary>Exclui; se tiver lançamentos ou recorrências, move para <paramref name="moverPara"/> (juntar categorias).</summary>
    Task<ResultadoRegistro> ExcluirCategoriaAsync(Guid id, Guid? moverPara, CancellationToken ct = default);

    Task<ResultadoRegistro> SalvarContaAsync(Guid? id, string nome, CancellationToken ct = default);
    Task<ResultadoRegistro> DefinirContaAtivaAsync(Guid id, bool ativa, CancellationToken ct = default);
    Task<ResultadoRegistro> ExcluirContaAsync(Guid id, Guid? moverPara, CancellationToken ct = default);

    Task<ResultadoRegistro> SalvarRecorrenciaAsync(DadosRecorrencia dados, DateOnly hoje, CancellationToken ct = default);
    /// <summary>Pausar ou retomar. Ao retomar, os meses pausados não são lançados.</summary>
    Task<ResultadoRegistro> DefinirRecorrenciaAtivaAsync(Guid id, bool ativa, DateOnly hoje, CancellationToken ct = default);
    /// <summary>Exclui a recorrência; os lançamentos já feitos ficam.</summary>
    Task<ResultadoRegistro> ExcluirRecorrenciaAsync(Guid id, CancellationToken ct = default);

    /// <summary>Cria os lançamentos das recorrências que venceram até hoje. Devolve quantos criou.</summary>
    Task<int> ProcessarRecorrenciasAsync(DateOnly hoje, CancellationToken ct = default);
}
