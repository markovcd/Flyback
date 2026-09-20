using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Flyback.App.Controls;

/// <summary>
/// What a module is painted with, worked out from its category's accent: the
/// wash down its body, the band across its header, and the color its mark is
/// drawn in.
/// </summary>
/// <remarks>
/// Built once per category and kept, because painting asks for these per module
/// per frame and a gradient brush is not free to make. The canvas is one control
/// on one thread (ADR-0017), so the caches need no guard.
/// <para>
/// The tint is deliberately slight. A body in the accent at full strength is a
/// patch of fourteen colored rectangles with the labels lost in them; what is
/// wanted is the difference being legible at a glance and at a zoom where no
/// label is, which a few percent of hue over the node grey gives.
/// </para>
/// </remarks>
internal static class NodeSkin
{
    /// <summary>How much accent is in the body, under the header and at the floor.</summary>
    private const double TopTint = 0.24, FloorTint = 0.06;

    private static readonly Dictionary<(string Category, bool Selected), IBrush> bodies = [];
    private static readonly Dictionary<string, IBrush> headers = [];
    private static readonly Dictionary<string, IPen> marks = [];

    /// <summary>The wash down a module's body, tinted by what the module does.</summary>
    public static IBrush Body(string category, bool selected)
    {
        if (bodies.TryGetValue((category, selected), out var kept)) return kept;

        var ground = selected ? Colors.NodeSelected : Colors.Node;
        var accent = Colors.Accent(category);

        var made = Down(
            Colors.Blend(ground, accent, TopTint),
            Colors.Blend(ground, accent, FloorTint));

        bodies[(category, selected)] = made;

        return made;
    }

    /// <summary>The header band. Lit at the top and falling away, so it has a face.</summary>
    public static IBrush Header(string category)
    {
        if (headers.TryGetValue(category, out var kept)) return kept;

        var accent = Colors.Accent(category);
        var made = Down(Color.FromArgb(0xF2, accent.R, accent.G, accent.B), Colors.Shade(accent, 0.72));

        headers[category] = made;

        return made;
    }

    /// <summary>
    /// What the mark across the body is stroked with — see <see cref="ModuleGlyphs"/>.
    /// Thickness in the glyph's own units, so it grows with whatever the mark is
    /// scaled to.
    /// </summary>
    public static IPen Mark(string category)
    {
        if (marks.TryGetValue(category, out var kept)) return kept;

        var made = new ImmutablePen(
            new ImmutableSolidColorBrush(Colors.Accent(category), MarkOpacity),
            ModuleGlyphs.Thickness,
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);

        marks[category] = made;

        return made;
    }

    /// <summary>
    /// How strongly the mark shows. Far enough under the labels drawn over it
    /// that reading one is never a matter of looking past the other.
    /// </summary>
    private const double MarkOpacity = 0.17;

    /// <summary>A gradient from the top of whatever it fills to the bottom.</summary>
    public static IBrush Down(Color top, Color bottom) => new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, top), new ImmutableGradientStop(1, bottom)],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative),
        endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));
}
