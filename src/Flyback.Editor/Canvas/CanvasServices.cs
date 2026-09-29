using Flyback.App.Controls;
using Flyback.App.Notices;
using Flyback.App.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flyback.App.Canvas;

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

        // One for the whole window, since the editor's container adds the canvas to its own.
        services.TryAddSingleton<Reactions>();

        // A canvas on its own counts nothing; the editor's container brings the run's.
        services.TryAddSingleton(Usage.Off);

        services.AddSingleton<NodeGeometry>();
        services.AddSingleton<Repaint>();
        services.AddSingleton<CanvasReport>();
        services.AddSingleton<CanvasHistory>();
        services.AddSingleton<CanvasSelection>();
        services.AddSingleton<Viewport>();
        services.AddSingleton<CanvasEdits>();
        services.AddSingleton<CanvasClipboard>();
        services.AddSingleton<KnobLinking>();
        services.AddPart<UndescribedTags>();
        services.AddSingleton<RemapMarks>();
        services.AddSingleton<HeldModules>();
        services.AddSingleton<SocketDial>();
        services.AddSingleton<CanvasTips>();
        services.AddSingleton<CanvasGestures>();
        services.AddSingleton<CanvasPainter>();
        services.AddPart<NodeEditor>();

        return services;
    }
}
