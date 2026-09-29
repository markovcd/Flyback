namespace Flyback.App.Controls;

/// <summary>A preview drawn by the GPU: an OpenGL control on the desktop, a canvas of its own in a page.</summary>
public interface IGpuPreview : IPreviewSurface
{
    /// <summary>
    /// Raised once, on the UI thread, when this surface cannot go on, with a message
    /// for the status bar; the host puts the processor's renderer in its place.
    /// </summary>
    event Action<string>? Failed;
}
