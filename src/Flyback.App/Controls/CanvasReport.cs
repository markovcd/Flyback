namespace Flyback.App.Controls;

/// <summary>
/// What the canvas has to say about something it was asked to do: a paste of
/// something that is not a patch, a group of one, a layout that had to shut a box.
/// </summary>
/// <remarks>
/// The canvas has nowhere to say it, and needs no window to be asked: whoever shows
/// the canvas listens, which in the editor is the window's report line.
/// </remarks>
internal sealed class CanvasReport
{
    public event EventHandler<string>? Said;

    public void Say(string message) => Said?.Invoke(this, message);
}
