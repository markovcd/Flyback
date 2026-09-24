using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flyback.App.Controls;

/// <summary>The canvas and every service it is made of, for one container (ADR-0150).</summary>
internal static class CanvasServices
{
    /// <summary>
    /// Registers one canvas. Singletons, because a container is one window's and a
    /// window has one canvas.
    /// </summary>
    /// <remarks>
    /// The report line is added only where nothing has added it, so a canvas on its
    /// own says things somewhere and a canvas in a window says them on the window's.
    /// </remarks>
    public static IServiceCollection AddCanvas(this IServiceCollection services)
    {
        services.TryAddSingleton<ReportLine>();

        // The pointer held where a socket's turn began; a test hands over one that holds nothing.
        services.TryAddSingleton<Func<Visual, IPointerAnchor?>>(PointerAnchor.Take);

        services.AddSingleton<Repaint>();
        services.AddSingleton<CanvasHistory>();
        services.AddSingleton<CanvasSelection>();
        services.AddSingleton<Viewport>();
        services.AddSingleton<CanvasEdits>();
        services.AddSingleton<CanvasClipboard>();
        services.AddSingleton<KnobLinking>();
        services.AddSingleton<UndescribedTags>();
        services.AddSingleton<RemapMarks>();
        services.AddSingleton<HeldModules>();
        services.AddSingleton<SocketDial>();
        services.AddSingleton<CanvasTips>();
        services.AddSingleton<CanvasGestures>();
        services.AddSingleton<CanvasPainter>();
        services.AddSingleton<NodeEditor>();

        return services;
    }
}
