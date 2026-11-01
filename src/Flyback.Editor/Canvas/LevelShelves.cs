using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The shelf over each band an open box holds: a chip naming the level, on a thin rule
/// across the band. Pressing a chip selects every module in that level.
/// </summary>
internal sealed class LevelShelves(NodeGeometry geometry)
{
    private const double ChipPadding = 6;

    private static readonly IBrush ChipFill = new ImmutableSolidColorBrush(Colors.Separator, 0.35);
    private static readonly IBrush ChipFillSelected = new ImmutableSolidColorBrush(Colors.Attention, 0.35);
    private static readonly IPen Rule = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Separator, 0.6), 1);

    /// <summary>The bands the text's levels make, or none while they are not laid out as bands.</summary>
    public Func<IReadOnlyList<LayoutBand>> Bands { get; set; } = () => [];

    /// <summary>A shelf: the band it stands over, its chip and its rule, in graph space.</summary>
    public readonly record struct Shelf(LayoutBand Band, IReadOnlyList<Guid> Members, Rect Chip, FormattedText Label, Point RuleFrom, Point RuleTo);

    /// <summary>Every shelf in the open boxes of <paramref name="scene"/>.</summary>
    public IEnumerable<Shelf> Of(Patch patch, CanvasScene scene)
    {
        var bands = Bands();

        if (bands.Count == 0 || patch.Groups is not { } groups) yield break;

        foreach (var group in groups)
        {
            if (scene.OpenGroup(group) is not var (outline, _)) continue;
            if (LayoutBand.Matching(bands, group.Members) is not { } box) continue;

            foreach (var shelf in Inside(patch, box.Inside, group.Members.ToHashSet(), outline.Right - NodeGeometry.GroupPadding / 2))
                yield return shelf;
        }
    }

    private List<Shelf> Inside(Patch patch, IReadOnlyList<LayoutBand> bands, IReadOnlySet<Guid> box, double right)
    {
        var size = geometry.Metrics;
        var shelves = new List<Shelf>();

        foreach (var band in bands)
        {
            var members = band.All.Where(box.Contains).ToList();
            var drawn = members.Select(id => patch.Find(id)).OfType<NodeInstance>()
                .Select(node => NodeCatalog.Get(node.TypeId) is { } def ? geometry.Bounds(node, def) : (Rect?)null)
                .OfType<Rect>()
                .ToList();

            if (drawn.Count == 0) continue;

            // The shelves of the bands inside stand within this band, so its own sits over them.
            var inner = Inside(patch, band.Inside, box, right);
            var left = drawn.Min(r => r.X);
            var top = inner.Select(shelf => shelf.Chip.Y).Append(drawn.Min(r => r.Y)).Min();

            var label = CanvasText.Text(band.Label, CanvasText.RowSize, CanvasText.LabelBrush, right - left, true);
            var chip = new Rect(left, top - size.ShelfHeight, label.Width + ChipPadding * 2, NodeGeometry.GroupHandleHeight);
            var middle = chip.Y + chip.Height / 2;

            shelves.Add(new Shelf(band, members, chip, label, new Point(chip.Right + 4, middle), new Point(Math.Max(right, chip.Right + 4), middle)));
            shelves.AddRange(inner);
        }

        return shelves;
    }

    /// <summary>The shelf whose chip is under <paramref name="graph"/>, or null.</summary>
    /// <remarks>Taken a little taller than it is drawn, for a finger.</remarks>
    public Shelf? Hit(Patch patch, CanvasScene scene, Point graph)
    {
        foreach (var shelf in Of(patch, scene))
            if (shelf.Chip.Inflate(new Thickness(0, 8)).Contains(graph))
                return shelf;

        return null;
    }

    /// <summary>Draws every shelf, a chip filled brighter where its level is the selection.</summary>
    public void Draw(DrawingContext context, Patch patch, CanvasScene scene, CanvasSelection selection)
    {
        foreach (var shelf in Of(patch, scene))
        {
            var chosen = shelf.Members.Count > 0 && selection.Count == shelf.Members.Count && shelf.Members.All(selection.Contains);

            context.DrawLine(Rule, shelf.RuleFrom, shelf.RuleTo);
            context.DrawRectangle(chosen ? ChipFillSelected : ChipFill, null, new RoundedRect(shelf.Chip, shelf.Chip.Height / 2));
            context.DrawText(shelf.Label, new Point(shelf.Chip.X + ChipPadding, shelf.Chip.Y + (shelf.Chip.Height - shelf.Label.Height) / 2));
        }
    }
}
