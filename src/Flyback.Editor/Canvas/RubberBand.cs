using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>The rubber band drawn out over empty canvas, selecting what it sweeps.</summary>
internal sealed class RubberBand(CanvasSelection selection, Viewport view)
{
    /// <summary>
    /// The band's edge. Dashed, because it is a gesture in progress rather than anything
    /// in the patch, and drawn over the canvas so it keeps its size at any zoom.
    /// </summary>
    private static readonly IPen Edge = new ImmutablePen(
        new ImmutableSolidColorBrush(Colors.Attention),
        1,
        new ImmutableDashStyle([4, 3], 0));

    private static readonly IBrush Fill = new ImmutableSolidColorBrush(Colors.Attention, 0.08);

    /// <summary>The two corners of the rubber band, in graph space, so it stays over the same modules at any zoom.</summary>
    private Point from;
    private Point to;

    /// <summary>
    /// What the rubber band adds to: what was selected when it began with the modifier
    /// held, and nothing otherwise. Held apart, so sweeping back off a module takes it
    /// out again.
    /// </summary>
    private readonly HashSet<Guid> kept = [];

    /// <summary>What was selected when the rubber band began, which backing out of one puts back.</summary>
    private readonly HashSet<Guid> was = [];

    /// <summary>Where the band went down, in graph space.</summary>
    public Point From => from;

    /// <summary>The button drawing the rubber band.</summary>
    public MouseButton Button { get; private set; }

    /// <summary>Begins a band at <paramref name="graph"/>, adding to the selection where <paramref name="adding"/>, for whichever button pressed.</summary>
    public void Start(Point graph, bool adding, MouseButton button)
    {
        from = to = graph;
        Button = button;

        kept.Clear();
        if (adding) kept.UnionWith(selection.Ids);

        was.Clear();
        was.UnionWith(selection.Ids);
    }

    /// <summary>Pulls the band's far corner to <paramref name="graph"/>, selecting what it is over now.</summary>
    public void Stretch(Point graph)
    {
        to = graph;
        Sweep();
    }

    /// <summary>Selects what the rubber band is over, together with whatever it was told to keep.</summary>
    public void Sweep()
    {
        var wanted = new HashSet<Guid>(kept);

        wanted.UnionWith(selection.Scene.Swept(CanvasScene.Band(from, to)));

        // Only when it changed: this runs on every move, and the inspector is rebuilt
        // whenever a selection is announced.
        if (wanted.Count == selection.Count && wanted.All(selection.Contains)) return;

        selection.Replace(wanted);
        selection.FocusTop();
        selection.Announce();
    }

    /// <summary>Puts back the selection the band replaced.</summary>
    public void Abort()
    {
        selection.Replace(was);
        selection.Refocus();
        selection.Announce();
    }

    /// <summary>Forgets what the band began from, as the gesture ends.</summary>
    public void Clear()
    {
        kept.Clear();
        was.Clear();
    }

    /// <summary>The rubber band, over the canvas rather than in it, so its hairline and dashes hold at any zoom.</summary>
    public void Draw(DrawingContext context)
    {
        var band = CanvasScene.Band(
            view.GraphToScreen.Transform(from),
            view.GraphToScreen.Transform(to));

        // A band with no width or height is a click that has not moved yet.
        if (band.Width < 1 || band.Height < 1) return;

        context.DrawRectangle(Fill, Edge, band);
    }
}
