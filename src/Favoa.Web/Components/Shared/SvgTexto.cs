using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Favoa.Web.Components.Shared;

/// <summary>
/// Elemento &lt;text&gt; do SVG. No Razor, "&lt;text&gt;" dentro de @if/@foreach é palavra reservada (não aceita atributos),
/// então os gráficos desenham o texto por aqui.
/// </summary>
public sealed class SvgTexto : ComponentBase
{
    [Parameter] public string? X { get; set; }
    [Parameter] public string? Y { get; set; }
    [Parameter] public string? Classe { get; set; }
    [Parameter] public string? Ancora { get; set; }
    [Parameter] public string? Base { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "text");
        if (X is not null) b.AddAttribute(1, "x", X);
        if (Y is not null) b.AddAttribute(2, "y", Y);
        if (Classe is not null) b.AddAttribute(3, "class", Classe);
        if (Ancora is not null) b.AddAttribute(4, "text-anchor", Ancora);
        if (Base is not null) b.AddAttribute(5, "dominant-baseline", Base);
        b.AddContent(6, ChildContent);
        b.CloseElement();
    }
}
