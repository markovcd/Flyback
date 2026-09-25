using Avalonia;
using Flyback.Core.Graph;

namespace Flyback.App.Canvas;

/// <summary>A socket clicked while linking sockets to a knob.</summary>
public readonly record struct SocketPick(Guid Node, int Port);

/// <summary>
/// The mode in which clicking a socket on the canvas links it to the panel knob being
/// linked, rather than starting a wire or a drag.
/// </summary>
internal sealed class KnobLinking(CanvasSelection selection, Repaint repaint, NodeGeometry geometry)
{
    /// <summary>
    /// The knob sockets are being linked to, or null. While set, every socket that can
    /// follow a knob is tinted, and a click on one is a <see cref="SocketPicked"/>.
    /// </summary>
    public Guid? Control
    {
        get;
        set
        {
            field = value;
            repaint.Request();
        }
    }

    /// <summary>A socket was clicked while <see cref="Control"/> was set.</summary>
    public event EventHandler<SocketPick>? SocketPicked;

    /// <summary>
    /// Whether a socket can follow a knob: one that would otherwise rest on its own
    /// knob, rather than on a wire it needs or a signal it is normalled to.
    /// </summary>
    public static bool Linkable(PortSpec spec) =>
        !spec.NeedsAWire && spec.NormalledFrom < 0 && NodeCatalog.Normalled(spec) is null;

    /// <summary>Answers a press while linking, and whether it did.</summary>
    public bool Pick(Point graph)
    {
        if (Control is null || !HitInputRow(graph, out var node, out var port)) return false;

        SocketPicked?.Invoke(this, new SocketPick(node, port));
        repaint.Request();
        return true;
    }

    /// <summary>The input row under <paramref name="graph"/>, on a module that is drawn.</summary>
    private bool HitInputRow(Point graph, out Guid nodeId, out int port)
    {
        if (selection.Scene.HitNode(graph) is { } node && NodeCatalog.Get(node.TypeId) is { } def)
        {
            for (var i = 0; i < def.Inputs.Count; i++)
            {
                var center = geometry.InputPort(node, def, i);

                if (Math.Abs(graph.Y - center.Y) > NodeGeometry.RowHeight / 2) continue;

                // A shared row's right half is its output's.
                if (geometry.Compact && graph.X >= geometry.Bounds(node, def).Center.X) break;

                (nodeId, port) = (node.Id, i);
                return true;
            }
        }

        (nodeId, port) = (Guid.Empty, -1);
        return false;
    }
}
