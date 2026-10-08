using Avalonia;
using Flyback.Host;

namespace Flyback.Ui;

/// <summary>Hands Avalonia the driver <see cref="OutputSettings.Driver"/> asks for, before it opens a window (ADR-0155).</summary>
public static class GraphicsDrivers
{
    /// <summary>What Avalonia tries on Windows, in order, for <paramref name="driver"/>.</summary>
    public static IReadOnlyList<Win32RenderingMode> Modes(GraphicsDriver driver) => driver switch
    {
        GraphicsDriver.Direct3D => [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
        _ => [Win32RenderingMode.Wgl, Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
    };

    public static AppBuilder UseDriver(this AppBuilder builder, GraphicsDriver driver) =>
        builder.With(new Win32PlatformOptions { RenderingMode = Modes(driver) });
}
