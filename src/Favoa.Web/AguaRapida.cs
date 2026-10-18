using Favoa.Core;
using Favoa.Core.Captura;

namespace Favoa.Web;

/// <summary>Água em um toque (+250 ml, +500 ml): o mesmo registro da frase "bebi 250 ml", com Desfazer.</summary>
public static class AguaRapida
{
    public static readonly int[] Copos = [250, 500, 750];

    public static async Task RegistrarAsync(IFavoaServico servico, EventosFavoa eventos, int mililitros)
    {
        var hoje = EventosFavoa.Hoje;
        var r = await servico.RegistrarAsync(new Interpretacao(TipoCaptura.Agua, $"bebi {mililitros} ml") { Mililitros = mililitros, Data = hoje });
        eventos.MostrarMensagem(r.Mensagem, r.Sucesso ? r.Criado : null);
        if (r.Sucesso) await eventos.NotificarRegistroSalvoAsync();
    }
}
