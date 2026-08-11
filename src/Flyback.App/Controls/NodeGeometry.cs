using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Where every part of a node sits. The editor draws itself rather than
/// composing real controls, so these are the single source of truth for both
/// painting and hit-testing.
/// </summary>
internal static class NodeGeometry
{
    public const double Width = 196;
    public const double HeaderHeight = 26;
    public const double RowHeight = 20;
    public const double FooterPadding = 8;
    public const double PortRadius = 5.5;
    public const double HitPadding = 5;
    public const double CornerRadius = 6;

    /// <summary>Outputs are listed first, then inputs — the Blender convention.</summary>
    public static double Height(NodeDef def) =>
        HeaderHeight + (def.Inputs.Count + def.Outputs.Count) * RowHeight + FooterPadding;

    public static Rect Bounds(NodeInstance node, NodeDef def) =>
        new(node.X, node.Y, Width, Height(def));

    public static Point OutputPort(NodeInstance node, int index) =>
        new(node.X + Width, node.Y + HeaderHeight + (index + 0.5) * RowHeight);

    public static Point InputPort(NodeInstance node, NodeDef def, int index) =>
        new(node.X, node.Y + HeaderHeight + (def.Outputs.Count + index + 0.5) * RowHeight);

    public static Color Accent(string category) => category switch
    {
        "Output" => Color.FromRgb(0xE0, 0x5A, 0x5A),
        "Source" => Color.FromRgb(0x4A, 0x9E, 0xDE),
        "Oscillator" => Color.FromRgb(0x4F, 0xC3, 0x87),
        "Maths" => Color.FromRgb(0x7E, 0x86, 0x94),
        "Space" => Color.FromRgb(0xB4, 0x84, 0xE0),
        "Pattern" => Color.FromRgb(0xE0, 0xA8, 0x4A),
        "Colour" => Color.FromRgb(0xE0, 0x6A, 0xB8),
        "Feedback" => Color.FromRgb(0x3F, 0xC8, 0xC8),
        _ => Color.FromRgb(0x88, 0x88, 0x88),
    };

    public static Color PortColour(PortKind kind) => kind switch
    {
        PortKind.Colour => Color.FromRgb(0xE8, 0xC8, 0x60),
        PortKind.Any => Color.FromRgb(0x9E, 0xC8, 0x9E),
        _ => Color.FromRgb(0xB8, 0xBC, 0xC4),
    };
}
