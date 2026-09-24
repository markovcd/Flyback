using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Compact modules (<see cref="NodeGeometry.Compact"/>): an input and the output of
/// the same index share a row, and what an unwired input rests at is said by its
/// tooltip (<see cref="SocketTips"/>) rather than written on the row.
/// </summary>
public sealed partial class NodeEditor
{
    /// <summary>How wide a socket's name may be drawn on a shared row: half the module, less the socket and margins.</summary>
    private static double HalfRow(Rect bounds) => bounds.Width / 2 - 18;

    /// <summary>
    /// A compact input row: its name, a dot where it follows a panel knob, and a
    /// flag where an Auto remap's range has nothing at the far end to take from.
    /// </summary>
    private void DrawCompactInput(
        DrawingContext context, NodeInstance node, PortSpec port, int i, Rect bounds, Point center,
        bool connected, bool follow, RemapSpans? spans, Func<double, double, double, IBrush, IBrush> ink)
    {
        DrawLinkWash(context, node, port, i, bounds, center, connected);

        var brush = ink(center.Y, RowInk, 0, CanvasText.LabelBrush);
        var label = CanvasText.Text(port.Name, CanvasText.RowSize, brush, HalfRow(bounds), true);
        var at = new Point(bounds.X + 14, center.Y - label.Height / 2);

        context.DrawText(label, at);

        if (!connected && ControlMap.Of(node, i) is { } found && patch.Control(found.Control) is not null)
            context.DrawEllipse(follow ? brush : LinkedBrush, null, new Point(at.X + label.Width + 6, center.Y), 2.5, 2.5);
        else if (!connected && i < node.InputValues.Length && SocketTips.RemapValue(spans, i, node.InputValues[i]) is (_, true))
            context.DrawRectangle(null, FlagPen, new Rect(at.X - 3, at.Y - 1, label.Width + 6, label.Height + 2), 3, 3);

        NodeSkin.DrawPort(context, center, port.Kind);
    }

    /// <summary>A socket's tooltip: its help, with what it rests at first on a compact module.</summary>
    private string? SocketTip(NodeInstance node, NodeDef def, int port, bool isOutput) =>
        NodeGeometry.Compact
            ? SocketTips.Say(patch, node, def, port, isOutput)
            : (isOutput ? def.Outputs : def.Inputs)[port].Help;
}
