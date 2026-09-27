namespace Flyback.App.Controls;

/// <summary>
/// Keeps the mouse pointer where a drag began, so the drag reads motion rather than position
/// and never runs into the edge of the screen.
/// </summary>
internal interface IPointerAnchor : IDisposable
{
    /// <summary>Puts the pointer back where it was taken. False when it did not go.</summary>
    bool Return();
}