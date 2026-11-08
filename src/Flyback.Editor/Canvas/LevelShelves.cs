using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The chips naming the levels the text opened: a shelf over each band an open box holds,
/// a chip on a thin rule across the band, and a strip of chips along the foot of a shut
/// box for the levels inside it. Pressing a chip selects every module in that level.
/// </summary>
/// <remarks>Zoomed far out, only each open box's outermost chips show, at a size that reads.</remarks>
internal sealed class LevelShelves
{
    private readonly NodeGeometry geometry;
    private readonly Viewport view;

    public LevelShelves(NodeGeometry geometry, Viewport view)
    {
        this.geometry = geometry;
        this.view = view;
        geometry.Rise = Rise;
        geometry.Strip = Strip;
    }

    private const double ChipPadding = 6;
    private const double StripInset = 8;
    private const double ChipGap = 4;

    private static readonly IBrush ChipFill = new ImmutableSolidColorBrush(Colors.Separator, 0.35);
    private static readonly IBrush ChipFillSelected = new ImmutableSolidColorBrush(Colors.Attention, 0.35);
    private static readonly IPen Rule = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Separator, 0.6), 1);

    /// <summary>The bands the text's levels make, or none while the canvas owns the patch.</summary>
    public Func<IReadOnlyList<LayoutBand>> Bands { get; set; } = () => [];

    /// <summary>A chip: what it reads, the modules pressing it selects, and where it is drawn in graph space.</summary>
    /// <param name="Depth">How many shelves stand over this one in its box; 0 for the outermost.</param>
    /// <param name="OnBox">Whether it is on a shut box's strip, drawn over the box, rather than a shelf in an open one.</param>
    public readonly record struct Shelf(
        string Name,
        IReadOnlyList<Guid> Members,
        Rect Chip,
        FormattedText Label,
        Point RuleFrom,
        Point RuleTo,
        int Depth,
        bool OnBox);

    /// <summary>Every chip shown on the canvas as it is now seen.</summary>
    public IEnumerable<Shelf> Of(Patch patch, CanvasScene scene)
    {
        var bands = Bands();

        if (bands.Count == 0 || patch.Groups is not { } groups) return [];

        var overview = ModuleOverview.At(view.Zoom);
        var shelves = new List<Shelf>();

        foreach (var group in groups)
        {
            if (LayoutBand.Matching(bands, group.Members) is not { } box) continue;

            if (scene.OpenGroup(group) is not null)
                shelves.AddRange(Inside(patch, box.Inside, group.Members.ToHashSet(), 0));
            else if (!overview && scene.Boxes().FirstOrDefault(b => b.Group == group) is { Bounds.Width: > 0 } shut)
                shelves.AddRange(Strip(box, group, shut.Bounds));
        }

        return overview ? shelves.Where(shelf => shelf.Depth == 0).Select(Readable) : shelves;
    }

    private List<Shelf> Inside(Patch patch, IReadOnlyList<LayoutBand> bands, IReadOnlySet<Guid> box, int depth)
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
            var inner = Inside(patch, band.Inside, box, depth + 1);
            var left = drawn.Min(r => r.X);
            var right = drawn.Max(r => r.Right);
            var top = inner.Select(shelf => shelf.Chip.Y).Append(drawn.Min(r => r.Y)).Min();

            var label = Text(band.Label, right - left);
            var chip = new Rect(left, top - size.ShelfHeight, label.Width + ChipPadding * 2, NodeGeometry.GroupHandleHeight);
            var middle = chip.Y + chip.Height / 2;

            shelves.Add(new Shelf(band.Label, members, chip, label, new Point(chip.Right + 4, middle), new Point(Math.Max(right, chip.Right + 4), middle), depth, OnBox: false));
            shelves.AddRange(inner);
        }

        return shelves;
    }

    /// <summary>
    /// The chips along a shut box's foot: the levels it came through, as a loop pass, then the
    /// first levels inside it. Those that do not fit share one chip, <c>+2</c>.
    /// </summary>
    private List<Shelf> Strip(LayoutBand box, NodeGroup group, Rect bounds)
    {
        var listed = box.Above.Select(name => (Name: name, Members: (IReadOnlyList<Guid>)[.. group.Members]))
            .Concat(box.Inside.Select(band => (Name: band.Label, Members: (IReadOnlyList<Guid>)[.. band.All.Where(group.Members.Contains)])))
            .ToList();

        var shelves = new List<Shelf>();
        var room = bounds.Width - StripInset * 2;
        var x = bounds.X + StripInset;
        var y = bounds.Bottom - geometry.Metrics.ChipStrip;

        for (var i = 0; i < listed.Count; i++)
        {
            var (name, members) = listed[i];
            var label = Text(name, room - ChipPadding * 2);
            var width = label.Width + ChipPadding * 2;
            var rest = listed.Count - i - 1;

            // Room is kept for the "+N" chip while anything is left after this one.
            var more = rest == 0 ? 0 : Text($"+{rest}", room).Width + ChipPadding * 2 + ChipGap;

            if (i > 0 && x + width + more > bounds.Right - StripInset)
            {
                var others = listed.Skip(i).ToList();
                label = Text($"+{others.Count}", room);
                shelves.Add(OnBox(string.Join(", ", others.Select(o => o.Name)), [.. others.SelectMany(o => o.Members).Distinct()], new Rect(x, y, label.Width + ChipPadding * 2, NodeGeometry.GroupHandleHeight), label));
                break;
            }

            shelves.Add(OnBox(name, members, new Rect(x, y, width, NodeGeometry.GroupHandleHeight), label));
            x += width + ChipGap;
        }

        return shelves;
    }

    private static Shelf OnBox(string name, IReadOnlyList<Guid> members, Rect chip, FormattedText label) =>
        new(name, members, chip, label, chip.TopLeft, chip.TopLeft, 0, OnBox: true);

    /// <summary>An outermost chip redrawn at a size that reads zoomed far out, standing on the same line.</summary>
    private Shelf Readable(Shelf shelf)
    {
        var scale = 1 / view.Zoom;
        var label = CanvasText.Text(shelf.Name, ModuleOverview.TextSize * scale, CanvasText.LabelBrush, double.PositiveInfinity, true);
        var height = NodeGeometry.GroupHandleHeight * scale;
        var chip = new Rect(shelf.Chip.X, shelf.Chip.Bottom - height, label.Width + ChipPadding * 2 * scale, height);

        return shelf with { Chip = chip, Label = label };
    }

    private static FormattedText Text(string name, double width) =>
        CanvasText.Text(name, CanvasText.RowSize, CanvasText.LabelBrush, width, true);

    /// <summary>How far the shelves in <paramref name="group"/> stand above its highest module.</summary>
    private double Rise(Patch patch, NodeGroup group)
    {
        var bands = Bands();

        if (bands.Count == 0 || LayoutBand.Matching(bands, group.Members) is not { Inside.Count: > 0 } box) return 0;

        var shelves = Inside(patch, box.Inside, group.Members.ToHashSet(), 0);

        if (shelves.Count == 0) return 0;

        var top = group.Members.Select(id => patch.Find(id)).OfType<NodeInstance>().Min(node => node.Y);

        return Math.Max(0, top - shelves.Min(shelf => shelf.Chip.Y));
    }

    /// <summary>How much taller a shut box is drawn for the strip listing the levels inside it.</summary>
    private double Strip(Patch patch, NodeGroup group)
    {
        if (!group.Collapsed) return 0;

        var bands = Bands();

        return bands.Count > 0 && LayoutBand.Matching(bands, group.Members) is { Listed: true } ? geometry.Metrics.ChipStrip : 0;
    }

    /// <summary>The chip under <paramref name="graph"/>, or null.</summary>
    /// <remarks>Taken a little taller than it is drawn, for a finger.</remarks>
    public Shelf? Hit(Patch patch, CanvasScene scene, Point graph)
    {
        foreach (var shelf in Of(patch, scene))
            if (shelf.Chip.Inflate(new Thickness(0, shelf.OnBox ? 2 : 8)).Contains(graph))
                return shelf;

        return null;
    }

    /// <summary>
    /// Draws the shelves in open boxes, or with <paramref name="onBoxes"/> the strips on shut
    /// ones, which go over the boxes. A chip is filled brighter where its level is the selection.
    /// </summary>
    public void Draw(DrawingContext context, Patch patch, CanvasScene scene, CanvasSelection selection, bool onBoxes)
    {
        foreach (var shelf in Of(patch, scene))
        {
            if (shelf.OnBox != onBoxes) continue;

            var chosen = shelf.Members.Count > 0 && selection.Count == shelf.Members.Count && shelf.Members.All(selection.Contains);

            if (!shelf.OnBox) context.DrawLine(Rule, shelf.RuleFrom, shelf.RuleTo);

            context.DrawRectangle(chosen ? ChipFillSelected : ChipFill, null, new RoundedRect(shelf.Chip, shelf.Chip.Height / 2));
            context.DrawText(shelf.Label, new Point(shelf.Chip.X + (shelf.Chip.Width - shelf.Label.Width) / 2, shelf.Chip.Y + (shelf.Chip.Height - shelf.Label.Height) / 2));
        }
    }
}
