namespace Flyback.Ui.Controls;

/// <summary>A preview drawn by the GPU: an OpenGL control on the desktop, a canvas of its own in a page.</summary>
public interface IGpuPreview : IPreviewSurface
{
    /// <summary>
    /// Raised once, on the UI thread, when this surface cannot go on, with a message
    /// for the status bar; the host puts the processor's renderer in its place.
    /// </summary>
    event Action<string>? Failed;

    /// <summary>What draws the picture (OpenGL, Direct3D, WebGL), or null until the context is up.</summary>
    string? Api { get; }

    /// <summary>
    /// Whether the processor may take over when this surface fails or is turned off.
    /// False in a page, where it is too slow to keep up and the surface says what went wrong instead.
    /// </summary>
    bool ProcessorStandsIn { get; }
}
