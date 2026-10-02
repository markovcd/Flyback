using Flyback.Editor.Notices;

namespace Flyback.Editor.Canvas;

/// <summary>
/// What the canvas has to say about something it was asked to do: a paste of
/// something that is not a patch, a group of one, a layout that had to shut a box.
/// </summary>
/// <remarks>
/// The canvas has nowhere to say it, and needs no window to be asked: it raises
/// <see cref="CanvasSaid"/>, and in the editor the report line reacts.
/// </remarks>
internal sealed class CanvasReport(Reactions reactions)
{
    public void Say(string message) => reactions.Raise(new CanvasSaid(message));
}
