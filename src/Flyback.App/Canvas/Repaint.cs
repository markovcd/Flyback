namespace Flyback.App.Canvas;

/// <summary>
/// Asks for the canvas to be drawn again, from any part of it that changed what it
/// shows without changing the patch.
/// </summary>
internal sealed class Repaint
{
    public event Action? Requested;

    public void Request() => Requested?.Invoke();
}
