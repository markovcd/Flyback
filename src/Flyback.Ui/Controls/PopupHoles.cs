using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Flyback.App.Controls;

/// <summary>
/// Where popups open over a control cover it: what a native control drawn above the
/// window, a page's picture canvas, must leave open for them to show.
/// </summary>
public static class PopupHoles
{
    /// <summary>The popups open over <paramref name="target"/>, each clipped to it, in its coordinates.</summary>
    /// <remarks>A popup with no window of its own is hosted in one of the window's layers, and Avalonia keeps the popups' own layer internal.</remarks>
    public static IReadOnlyList<Rect> Over(Visual target)
    {
        if (target.FindAncestorOfType<VisualLayerManager>() is not { } layers) return [];

        var bounds = new Rect(target.Bounds.Size);
        var holes = new List<Rect>();

        foreach (var layer in layers.GetVisualChildren())
        foreach (var popup in layer.GetVisualChildren().OfType<OverlayPopupHost>())
        {
            if (!popup.IsVisible || popup.TransformToVisual(target) is not { } toTarget) continue;

            var hole = new Rect(popup.Bounds.Size).TransformToAABB(toTarget).Intersect(bounds);

            if (hole.Width > 0 && hole.Height > 0) holes.Add(hole);
        }

        return holes;
    }

    /// <summary>
    /// An even-odd SVG path that covers <paramref name="size"/> but for <paramref name="holes"/>,
    /// or null where there are none.
    /// </summary>
    public static string? ClipPath(Size size, IReadOnlyList<Rect> holes)
    {
        if (holes.Count == 0) return null;

        var path = new StringBuilder();

        Square(path, new Rect(size));
        foreach (var hole in holes) Square(path, hole);

        return path.ToString().TrimEnd();
    }

    private static void Square(StringBuilder path, Rect rect) =>
        path.Append(CultureInfo.InvariantCulture,
            $"M{rect.X:0.##} {rect.Y:0.##}H{rect.Right:0.##}V{rect.Bottom:0.##}H{rect.X:0.##}Z ");
}
