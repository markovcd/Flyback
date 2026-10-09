using Avalonia;

namespace Flyback.Ui.Controls;

/// <summary>The platform's anchors.</summary>
internal sealed class PlatformAnchors : IPointerAnchors
{
    public static PlatformAnchors Instance { get; } = new();

    public IPointerAnchor? Take(Visual visual) => PointerAnchor.Take(visual);
}
