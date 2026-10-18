using System.Reflection;

namespace Favoa.Web;

/// <summary>Versão publicada (commit) e quando foi compilada, para conferir se a atualização entrou.</summary>
public static class VersaoFavoa
{
    private static readonly Assembly App = typeof(VersaoFavoa).Assembly;

    /// <summary>"7593695" quando publicado pelo script; "dev" rodando pelo dotnet run.</summary>
    public static string Codigo { get; } = Ler();

    public static DateTime PublicadaEm { get; } = File.GetLastWriteTime(App.Location);

    private static string Ler()
    {
        var info = App.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var mais = info.IndexOf('+');
        return mais >= 0 && mais < info.Length - 1 ? info[(mais + 1)..] : "dev";
    }
}
