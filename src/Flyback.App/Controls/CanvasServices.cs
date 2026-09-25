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
    public static IServiceCollection AddCanvas(this IServiceCollection services)
    {
        // The pointer held where a socket's turn began; a test hands over anchors that hold nothing.
        services.TryAddSingleton<IPointerAnchors>(PlatformAnchors.Instance);

        services.AddSingleton<NodeGeometry>();
        services.AddSingleton<Repaint>();
        services.AddSingleton<CanvasReport>();
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
