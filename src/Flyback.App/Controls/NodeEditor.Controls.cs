using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>A socket clicked while linking sockets to a knob.</summary>
public readonly record struct SocketPick(Guid Node, int Port);

/// <summary>
/// The panel's knobs as the canvas shows them: which sockets follow one, and the
/// mode in which clicking a socket links it to the knob being linked.
/// </summary>
public sealed partial class NodeEditor
{
    private static readonly IBrush LinkedBrush = new SolidColorBrush(Colors.Attention, 0.85);
    private static readonly IBrush LinkableWash = new SolidColorBrush(Colors.Attention, 0.07);
    private static readonly IBrush LinkedWash = new SolidColorBrush(Colors.Attention, 0.24);

    /// <summary>
    /// The knob sockets are being linked to, or null. While set, every socket that
    /// can follow a knob is tinted, and a click on one is a <see cref="SocketPicked"/>
    /// rather than the start of a wire or a drag.
    /// </summary>
    public Guid? LinkingControl
    {
        get;
        set
        {
            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>A socket was clicked while <see cref="LinkingControl"/> was set.</summary>
    public event EventHandler<SocketPick>? SocketPicked;

    /// <summary>
    /// Whether a socket can follow a knob: one that would otherwise rest on its own
    /// knob, rather than on a wire it needs or on a signal it is normalled to.
    /// </summary>
    public static bool Linkable(PortSpec spec) =>
        !spec.NeedsAWire && spec.NormalledFrom < 0 && NodeCatalog.Normalled(spec) is null;

    /// <summary>The input row under <paramref name="graph"/>, on a module that is drawn.</summary>
    private bool HitInputRow(Point graph, out Guid nodeId, out int port)
    {
        if (HitNode(graph) is { } node && NodeCatalog.Get(node.TypeId) is { } def)
        {
            for (var i = 0; i < def.Inputs.Count; i++)
            {
                var centre = NodeGeometry.InputPort(node, def, i);

                if (Math.Abs(graph.Y - centre.Y) > NodeGeometry.RowHeight / 2) continue;

                (nodeId, port) = (node.Id, i);
                return true;
            }
        }

        (nodeId, port) = (Guid.Empty, -1);
        return false;
    }

    /// <summary>Answers a press while linking, and whether it did.</summary>
    private bool PickSocket(Point graph)
    {
        if (LinkingControl is null || !HitInputRow(graph, out var node, out var port)) return false;

        SocketPicked?.Invoke(this, new SocketPick(node, port));
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Tints a row while linking, and draws a linked socket's value in the knob's
    /// color. Returns whether it drew the value, so the ordinary one is not drawn too.
    /// </summary>
    private bool DrawLinkedRow(DrawingContext context, NodeInstance node, PortSpec port, int index, Rect bounds, Point centre, bool connected)
    {
        var link = ControlMap.Of(node, index);
        var control = link is { } l ? patch.Control(l.Control) : null;

        if (LinkingControl is { } linking && !connected && Linkable(port))
        {
            var row = new Rect(bounds.X, centre.Y - NodeGeometry.RowHeight / 2, bounds.Width, NodeGeometry.RowHeight);
            context.FillRectangle(link?.Control == linking ? LinkedWash : LinkableWash, row);
        }

        if (connected || control is null || link is not { } found) return false;

        var value = CanvasText.Text(port.Format(found.At(control.Value)), 11.5, LinkedBrush, bounds.Width * 0.4, true);
        var right = bounds.Right - 12;

        context.DrawText(value, new Point(right - value.Width, centre.Y - value.Height / 2));
        context.DrawEllipse(LinkedBrush, null, new Point(right - value.Width - 6, centre.Y), 2.5, 2.5);

        return true;
    }
}
