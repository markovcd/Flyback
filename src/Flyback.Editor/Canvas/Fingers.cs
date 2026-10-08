using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.Ui.Controls;
using Flyback.Editor.Notices;

namespace Flyback.Editor.Canvas;

/// <summary>
/// Fingers on the canvas, told to <see cref="CanvasGestures"/> as the mouse buttons it
/// already answers (ADR-0165): one finger is the left button, a finger held still is
/// the right, and a second finger is the middle, which pans, while spreading two zooms.
/// </summary>
/// <remarks>
/// A first finger is not a press until it moves, lifts or is held, since until then it
/// could still turn out to be any of the three.
/// </remarks>
internal sealed class Fingers
{
    /// <summary>How long a finger stays still before it is the right button.</summary>
    public static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(450);

    /// <summary>How far a finger travels, in screen pixels, before it is moving rather than resting.</summary>
    public const double Slop = FingerSwipe.Slop;

    /// <summary>How far from a socket, in screen pixels, a fingertip still lands on it.</summary>
    public const double Reach = 16;

    /// <summary>
    /// The most <see cref="Reach"/> may cover of the patch, however far out the view is,
    /// so a zoomed-out module keeps a body to tap and drag between its two rows of sockets.
    /// </summary>
    public const double MostReach = NodeGeometry.Width / 5;

    /// <summary>How soon after the first, in milliseconds, a second finger lands for the two to have come down together.</summary>
    private const ulong TogetherTime = 200;

    /// <summary>How close in time and place, in milliseconds and screen pixels, a second tap is to count as a double.</summary>
    private const ulong DoubleTapTime = 400;
    private const double DoubleTapDistance = 24;

    private enum Phase
    {
        None,

        /// <summary>One finger is down and has not yet said what it is.</summary>
        Waiting,
        Left,
        Right,

        /// <summary>Two fingers moving the view, with no gesture under them.</summary>
        Pinch,

        /// <summary>The gesture is over, and the fingers still down count for nothing until they lift.</summary>
        Spent,
    }

    private readonly CanvasGestures gestures;
    private readonly Viewport view;
    private readonly CanvasSelection selection;
    private readonly Reactions reactions;
    private readonly LastPress lastPress;
    private readonly DispatcherTimer holdTimer;

    private readonly Dictionary<int, Point> down = [];

    private Phase phase;
    private Control? canvas;
    private IPointer? first;
    private Point firstFrom;
    private ulong firstAt;

    /// <summary>How far a held finger's press was moved onto a socket, which its moves are moved by too.</summary>
    private Vector grip;

    /// <summary>Whether a second finger is holding the view, as the middle button, over a gesture of the first.</summary>
    private bool panning;

    private double span;

    private (Point At, ulong Time)? lastTap;

    private bool seen;

    public Fingers(CanvasGestures gestures, Viewport view, CanvasSelection selection, Reactions reactions, LastPress lastPress)
    {
        this.gestures = gestures;
        this.view = view;
        this.selection = selection;
        this.reactions = reactions;
        this.lastPress = lastPress;

        holdTimer = new DispatcherTimer { Interval = HoldTime };
        holdTimer.Tick += (_, _) => Held();
    }

    /// <summary>How many fingers are on the canvas.</summary>
    public int Count => down.Count;

    public void Down(Control on, IPointer pointer, Point screen, ulong time)
    {
        canvas = on;
        lastPress.Finger();

        if (!seen)
        {
            seen = true;
            reactions.Raise(new Touched());
        }

        // A third finger counts for nothing from landing to lifting.
        if (down.Count == 2) return;

        down[pointer.Id] = screen;

        if (down.Count == 1)
        {
            phase = Phase.Waiting;
            first = pointer;
            firstFrom = screen;
            firstAt = time;
            grip = default;
            holdTimer.Start();
            return;
        }

        span = Span();

        switch (phase)
        {
            case Phase.Waiting:
                holdTimer.Stop();
                phase = Phase.Pinch;
                break;

            // Two fingers that came down together are a pinch, whatever the first one
            // started on its way past the slop.
            case Phase.Left when time >= firstAt && time - firstAt <= TogetherTime:
                gestures.Abort(on);
                phase = Phase.Pinch;
                break;

            case Phase.Left:
                panning = true;
                gestures.Pressed(on, null, Middle(), MouseButton.Middle, KeyModifiers.None, 1);
                break;

            // A held finger is the right button held still, which a pinch takes back.
            case Phase.Right:
                gestures.Abort(on);
                gestures.Released(on, first, firstFrom, MouseButton.Right, KeyModifiers.None);
                phase = Phase.Pinch;
                break;
        }
    }

    public void Move(Control on, IPointer pointer, Point screen)
    {
        if (!down.ContainsKey(pointer.Id)) return;

        var middle = Middle();
        down[pointer.Id] = screen;

        switch (phase)
        {
            case Phase.Waiting when pointer.Id == first?.Id:
                if (Distance(screen, firstFrom) <= Slop) return;

                holdTimer.Stop();
                phase = Phase.Left;
                gestures.Pressed(on, pointer, Socket(firstFrom), MouseButton.Left, KeyModifiers.None, 1);
                gestures.Moved(on, Wiring(screen), KeyModifiers.None, middleDown: false);
                return;

            case Phase.Left when panning:
                gestures.Moved(on, Middle(), KeyModifiers.None, middleDown: true);
                Zoom();
                return;

            case Phase.Left or Phase.Right when pointer.Id == first?.Id:
                gestures.Moved(on, phase == Phase.Left ? Wiring(screen) : screen + grip, KeyModifiers.None, middleDown: false);
                return;

            case Phase.Pinch:
                view.PanBy(Middle() - middle);
                Zoom();
                return;
        }
    }

    public void Up(Control on, IPointer pointer, Point screen, ulong time)
    {
        if (!down.Remove(pointer.Id)) return;

        var isFirst = pointer.Id == first?.Id;

        switch (phase)
        {
            case Phase.Waiting when isFirst:
                holdTimer.Stop();
                Tap(on, pointer, time);
                phase = Phase.Spent;
                break;

            case Phase.Left when isFirst:
                gestures.Released(on, pointer, panning ? screen : Wiring(screen), MouseButton.Left, KeyModifiers.None);
                panning = false;
                phase = Phase.Spent;
                break;

            case Phase.Left when panning:
                gestures.Released(on, null, Middle(), MouseButton.Middle, KeyModifiers.None);
                panning = false;
                break;

            case Phase.Right when isFirst:
                gestures.Released(on, pointer, screen, MouseButton.Right, KeyModifiers.None);
                phase = Phase.Spent;
                break;

            // The finger left down goes on moving the view, and one put back pinches again.
            case Phase.Pinch:
                span = Span();
                break;
        }

        if (down.Count == 0) Reset(on);
    }

    /// <summary>A finger was taken away from the canvas without lifting: whatever it was doing ends where it is.</summary>
    public void Lost(Control on, IPointer pointer)
    {
        if (!down.TryGetValue(pointer.Id, out var at)) return;

        // Only the first finger holds a gesture of its own, so any other is as good as lifted.
        if (pointer.Id != first?.Id || phase == Phase.Pinch)
        {
            Up(on, pointer, at, 0);
            return;
        }

        down.Remove(pointer.Id);
        holdTimer.Stop();

        if (phase is Phase.Left or Phase.Right) gestures.CaptureLost(on);

        phase = down.Count == 0 ? Phase.None : Phase.Spent;
        if (down.Count == 0) Reset(on);
    }

    /// <summary>The first finger has rested long enough to be the right button.</summary>
    public void Held()
    {
        holdTimer.Stop();

        if (phase != Phase.Waiting || canvas is null || first is null) return;

        phase = Phase.Right;

        var at = Socket(firstFrom);
        grip = at - firstFrom;
        gestures.Pressed(canvas, first, at, MouseButton.Right, KeyModifiers.None, 1);
    }

    private void Tap(Control on, IPointer pointer, ulong time)
    {
        var at = Socket(firstFrom);

        var clicks = lastTap is { } last && time >= last.Time && time - last.Time <= DoubleTapTime && Distance(last.At, at) <= DoubleTapDistance ? 2 : 1;
        lastTap = clicks == 2 ? null : (at, time);

        gestures.Pressed(on, pointer, at, MouseButton.Left, KeyModifiers.None, clicks);
        gestures.Released(on, pointer, at, MouseButton.Left, KeyModifiers.None);
    }

    /// <summary>The last finger is off the canvas, and nothing is under one any more.</summary>
    private void Reset(Control on)
    {
        phase = Phase.None;
        first = null;
        panning = false;
        holdTimer.Stop();
        gestures.Lifted(on);
    }

    /// <summary>Zooms by how far the two fingers spread since last time, about the point between them.</summary>
    private void Zoom()
    {
        if (down.Count < 2) return;

        var now = Span();
        if (span > 1 && now > 1) view.ZoomAt(Middle(), Math.Log(now / span) / Math.Log(1.12));

        span = now;
    }

    /// <summary>
    /// A point on the screen moved onto the socket within reach of it, if one is: an
    /// output when <paramref name="isOutput"/> says so, an input when it says not, either when null.
    /// </summary>
    private Point Socket(Point screen, bool? isOutput = null)
    {
        var graph = view.ToGraph(screen);
        var scene = selection.Scene;

        if (scene.HitPort(graph, out _, out _, out var output) && (isOutput is null || output == isOutput)) return screen;

        return scene.NearestSocket(graph, Math.Min(Reach / view.Zoom, MostReach), isOutput) is { } at ? view.GraphToScreen.Transform(at) : screen;
    }

    /// <summary>The same, only while a wire is being drawn, so a wire lands on the socket beside its end that can take it.</summary>
    private Point Wiring(Point screen) => gestures.PendingWireTakesOutput is { } output ? Socket(screen, output) : screen;

    private Point Middle()
    {
        var points = down.Values.Take(2).ToArray();

        return points.Length < 2 ? points[0] : (points[0] + points[1]) / 2;
    }

    private double Span()
    {
        var points = down.Values.Take(2).ToArray();

        return points.Length < 2 ? 0 : Distance(points[0], points[1]);
    }

    private static double Distance(Point a, Point b) => Point.Distance(a, b);
}
