using Favoa.Core.Edicao;

namespace Favoa.Web;

/// <summary>
/// Avisos entre componentes da mesma janela: a captura salva, o painel recarrega, o aviso oferece desfazer.
/// Escopo por circuito (uma aba do navegador).
/// </summary>
public sealed class EventosFavoa
{
    public event Func<Task>? RegistroSalvo;
    public event Action? AbrirCapturaSolicitado;
    public event Action<string?, Guid?>? AbrirLancamentoSolicitado;
    public event Action<ReferenciaRegistro>? AbrirEdicaoSolicitada;
    public event Action<string, ReferenciaRegistro?>? Mensagem;

    public async Task NotificarRegistroSalvoAsync()
    {
        if (RegistroSalvo is null) return;
        foreach (var handler in RegistroSalvo.GetInvocationList().Cast<Func<Task>>())
            await handler();
    }

    public void SolicitarCaptura() => AbrirCapturaSolicitado?.Invoke();

    /// <summary>Abre o lançamento rápido (painel de baixo). <paramref name="modo"/>: "despesa", "receita" ou "agua".</summary>
    public void SolicitarLancamento(string? modo = null, Guid? projetoId = null) => AbrirLancamentoSolicitado?.Invoke(modo, projetoId);

    public void SolicitarEdicao(ReferenciaRegistro referencia) => AbrirEdicaoSolicitada?.Invoke(referencia);

    /// <param name="desfazer">Quando informado, o aviso mostra o botão "Desfazer".</param>
    public void MostrarMensagem(string texto, ReferenciaRegistro? desfazer = null) => Mensagem?.Invoke(texto, desfazer);

    public static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Now);
}
