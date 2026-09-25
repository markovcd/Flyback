using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Canvas;

/// <summary>
/// An unpatched input turned by dragging up and down with the right button held on
/// it, across the socket's range. While it turns the socket is a pie of where the
/// value sits in that range.
/// </summary>
/// <remarks>
/// An edit like the inspector's slider, filed under the same name, so a turn here and
/// a drag there are one undo step each.
/// </remarks>
/// <param name="anchors">Holds the pointer where a turn begins, or leaves it free.</param>
internal sealed class SocketDial(
    CanvasHistory history,
    CanvasSelection selection,
    Repaint repaint,
    IPointerAnchors anchors)
{
    /// <summary>How far a drag has to travel to turn the socket end to end, as on <see cref="Knob"/>.</summary>
    private const double DialTravel = 160;

    private static readonly IBrush DialedBrush = new SolidColorBrush(Colors.Attention);

    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    private static readonly Cursor DialCursor = new(StandardCursorType.SizeNorthSouth);

    /// <summary>The socket being turned, and the range it turns across.</summary>
    private (Guid Node, int Port, float Min, float Max, float Was, PortSpec Spec)? dialed;

    /// <summary>How far along its range the socket is turned, 0 to 1, apart from the value so a stepped socket still turns smoothly.</summary>
    private double dialAt;

    private Point dialLast;
    private Point dialHome;
    private IPointerAnchor? dialAnchor;

    /// <summary>An input's value was turned on the canvas.</summary>
    public event EventHandler<SocketPick>? InputTurned;

    /// <summary>The hand came off a socket it was turning.</summary>
    public event EventHandler<SocketPick>? InputLetGo;

    public bool Turning => dialed is not null;

    /// <summary>Starts turning the input under <paramref name="graph"/>, and says whether there was one to turn.</summary>
    public bool Start(Control canvas, Point graph, Point screen)
    {
        if (!selection.Scene.HitPort(graph, out var nodeId, out var port, out var isOutput) || isOutput) return false;
        if (!Dialable(nodeId, port, out var node, out var spec)) return false;

        var value = node.InputValues[port];
        var min = Math.Min(spec.Min, value);
        var max = Math.Max(spec.Max, value);

        dialed = (nodeId, port, min, max, value, spec);
        dialAt = spec.Travel(value, min, max);
        dialLast = dialHome = screen;

        dialAnchor = anchors.Take(canvas);
        canvas.Cursor = dialAnchor is null ? DialCursor : HiddenCursor;

        repaint.Request();
        return true;
    }

    public void Move(Control canvas, Point screen, KeyModifiers modifiers)
    {
        if (dialed is not { } d) return;

        // The warp's own echo, which would otherwise turn it back.
        if (dialAnchor is not null && screen == dialHome) return;

        var fine = (modifiers & KeyModifiers.Shift) != 0 ? 5d : 1d;

        dialAt = Math.Clamp(dialAt + (dialLast.Y - screen.Y) / (DialTravel * fine), 0d, 1d);

        if (dialAnchor?.Return() == true)
        {
            dialLast = dialHome;
        }
        else
        {
            dialLast = screen;
            DropAnchor(canvas);
        }

        var value = d.Spec.At(dialAt, d.Min, d.Max);
        if (d.Spec.Stepped) value = MathF.Round(value);

        Set(d.Node, d.Port, value);
    }

    /// <summary>Stops turning, putting the value back first when <paramref name="restore"/>, and says whether a turn was under way.</summary>
    public bool End(Control canvas, bool restore = false)
    {
        if (dialed is not { } d) return false;

        if (restore) Set(d.Node, d.Port, d.Was);

        dialed = null;
        DropAnchor(canvas);

        repaint.Request();
        InputLetGo?.Invoke(this, new SocketPick(d.Node, d.Port));
        return true;
    }

    /// <summary>Draws the socket being turned as a pie of its value, over the socket it replaces.</summary>
    public void Draw(DrawingContext context, CanvasScene scene)
    {
        if (dialed is not { } d
            || history.Patch.Find(d.Node) is not { } node
            || NodeCatalog.Get(node.TypeId) is not { } def)
            return;

        var share = d.Max > d.Min ? d.Spec.Travel(node.InputValues[d.Port], d.Min, d.Max) : dialAt;

        NodeSkin.DrawPort(context, scene.InputAnchor(node, def, d.Port), def.Inputs[d.Port].Kind, share, DialedBrush);
    }

    /// <summary>Whether a press on this input can turn its value: unpatched, and resting on a knob of its own.</summary>
    private bool Dialable(Guid nodeId, int port, out NodeInstance node, out PortSpec spec)
    {
        node = null!;
        spec = default;

        var patch = history.Patch;

        if (patch.Find(nodeId) is not { } found || NodeCatalog.Get(found.TypeId) is not { } def) return false;
        if (port >= def.Inputs.Count || port >= found.InputValues.Length) return false;

        (node, spec) = (found, def.Inputs[port]);

        return KnobLinking.Linkable(spec)
            && patch.IncomingTo(nodeId, port) is null
            && !(ControlMap.Of(found, port) is { } link && patch.Control(link.Control) is not null);
    }

    private void Set(Guid nodeId, int port, float value)
    {
        if (history.Patch.Find(nodeId) is not { } node || port >= node.InputValues.Length) return;

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (node.InputValues[port] == value) return;

        node.InputValues[port] = value;

        history.Record($"{nodeId} input {port}");
        InputTurned?.Invoke(this, new SocketPick(nodeId, port));
    }

    private void DropAnchor(Control canvas)
    {
        dialAnchor?.Dispose();
        dialAnchor = null;

        if (dialed is not null) canvas.Cursor = DialCursor;
    }
}
