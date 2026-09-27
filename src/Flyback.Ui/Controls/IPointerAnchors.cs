using Avalonia;

namespace Flyback.App.Controls;

/// <summary>Where a drag takes its <see cref="IPointerAnchor"/> from.</summary>
internal interface IPointerAnchors
{
    /// <summary>Anchors the pointer where it is now, or null to leave it free.</summary>
    IPointerAnchor? Take(Visual visual);
}