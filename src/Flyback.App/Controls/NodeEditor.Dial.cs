using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// An unpatched input turned by dragging up and down with the right button held on
/// it, across the socket's range. While it turns the socket is a pie of where the value
/// sits in that range, and nothing else is drawn.
/// </summary>
/// <remarks>
/// An edit like the inspector's slider, filed under the same name, so a turn here
/// and a drag there are one undo step each.
/// </remarks>
public sealed partial class NodeEditor
{
    /// <summary>How far a drag has to travel to turn the socket end to end, as on <see cref="Knob"/>.</summary>
    private const double DialTravel = 160;

    private static readonly IBrush DialedBrush = new SolidColorBrush(Colors.Attention);

    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    private static readonly Cursor DialCursor = new(StandardCursorType.SizeNorthSouth);

    /// <summary>The socket being turned, and the range it turns across.</summary>
    private (Guid Node, int Port, float Min, float Max, float Was, PortSpec Spec)? dialed;

    /// <summary>How far along its range the socket is turned, 0 to 1, kept apart from the value so a stepped socket still turns smoothly.</summary>
    private double dialAt;

    private Point dialLast;
    private Point dialHome;
    private IPointerAnchor? dialAnchor;

    /// <summary>Holds the pointer where a turn begins. Null leaves the pointer free.</summary>
    internal Func<Visual, IPointerAnchor?> KnobAnchor { get; set; } = PointerAnchor.Take;

    /// <summary>An input's value was turned on the canvas.</summary>
    public event EventHandler<SocketPick>? InputTurned;

    /// <summary>The hand came off a socket it was turning.</summary>
    public event EventHandler<SocketPick>? InputLetGo;

    /// <summary>Whether a press on this input can turn its value: unpatched, and resting on a knob of its own.</summary>
    private bool Dialable(Guid nodeId, int port, out NodeInstance node, out PortSpec spec)
    {
        node = null!;
        spec = default;

        if (patch.Find(nodeId) is not { } found || NodeCatalog.Get(found.TypeId) is not { } def) return false;
        if (port >= def.Inputs.Count || port >= found.InputValues.Length) return false;

        (node, spec) = (found, def.Inputs[port]);

        return Linkable(spec)
            && patch.IncomingTo(nodeId, port) is null
            && !(ControlMap.Of(found, port) is { } link && patch.Control(link.Control) is not null);
    }

    /// <summary>Starts turning the input under <paramref name="graph"/>, and whether there was one to turn.</summary>
    private bool StartDial(Point graph, Point screen)
    {
        if (!Scene.HitPort(graph, out var nodeId, out var port, out var isOutput) || isOutput) return false;
        if (!Dialable(nodeId, port, out var node, out var spec)) return false;

        var value = node.InputValues[port];
        var min = Math.Min(spec.Min, value);
        var max = Math.Max(spec.Max, value);

        dialed = (nodeId, port, min, max, value, spec);
        dialAt = spec.Travel(value, min, max);
        dialLast = dialHome = screen;

        tipped = null;
        ToolTip.SetIsOpen(this, false);

        dialAnchor = KnobAnchor(this);
        Cursor = dialAnchor is null ? DialCursor : HiddenCursor;

        InvalidateVisual();
        return true;
    }

    private void MoveDial(Point screen, KeyModifiers modifiers)
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
            DropDialAnchor();
        }

        var value = d.Spec.At(dialAt, d.Min, d.Max);
        if (d.Spec.Stepped) value = MathF.Round(value);

        SetDialed(d.Node, d.Port, value);
    }

    private void SetDialed(Guid nodeId, int port, float value)
    {
        if (patch.Find(nodeId) is not { } node || port >= node.InputValues.Length) return;

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (node.InputValues[port] == value) return;

        node.InputValues[port] = value;

        NotifyPatchChanged($"{nodeId} input {port}");
        InputTurned?.Invoke(this, new SocketPick(nodeId, port));
    }

    /// <summary>Stops turning, putting the value back first when <paramref name="restore"/>.</summary>
    private bool EndDial(bool restore = false)
    {
        if (dialed is not { } d) return false;

        if (restore) SetDialed(d.Node, d.Port, d.Was);

        dialed = null;
        DropDialAnchor();
        Cursor = lastPointer is { } over ? CursorOver(over) : ArrowCursor;

        InvalidateVisual();
        InputLetGo?.Invoke(this, new SocketPick(d.Node, d.Port));
        return true;
    }

    private void DropDialAnchor()
    {
        dialAnchor?.Dispose();
        dialAnchor = null;

        if (dialed is not null) Cursor = DialCursor;
    }

    /// <summary>Draws the socket being turned as a pie of its value, over the socket it replaces.</summary>
    private void DrawDial(DrawingContext context)
    {
        if (dialed is not { } d
            || patch.Find(d.Node) is not { } node
            || NodeCatalog.Get(node.TypeId) is not { } def)
            return;

        var share = d.Max > d.Min ? d.Spec.Travel(node.InputValues[d.Port], d.Min, d.Max) : dialAt;

        NodeSkin.DrawPort(context, Scene.InputAnchor(node, def, d.Port), def.Inputs[d.Port].Kind, share, DialedBrush);
    }
}
