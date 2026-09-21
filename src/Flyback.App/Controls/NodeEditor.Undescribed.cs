using Avalonia;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The mark on a module the assistant is not told about, and what hovering it says.
/// </summary>
/// <remarks>
/// A small dotted tag at the right of the header — an ellipsis, since what is
/// missing is the module's words, not the module — rather than a color or a
/// badge on the body: the header is the one part of a module always on screen,
/// and the mark is a footnote about the assistant, not about the patch. The words
/// are the tooltip's and the inspector's, because the canvas has no room for them.
/// </remarks>
public sealed partial class NodeEditor
{
    private const double TagWidth = 16, TagHeight = 10, TagInset = 7;

    private static readonly IPen TagPen = new Pen(new SolidColorBrush(Avalonia.Media.Colors.White, 0.75));
    private static readonly IBrush TagDots = new SolidColorBrush(Avalonia.Media.Colors.White, 0.75);

    private IReadOnlySet<string> undescribed = new HashSet<string>();

    /// <summary>
    /// Type ids whose descriptions the assistant's briefing leaves out — see
    /// <see cref="AssistantPanel.Undescribed"/>. Each module of one is tagged.
    /// </summary>
    public IReadOnlySet<string> Undescribed
    {
        get => undescribed;
        set
        {
            undescribed = value;
            InvalidateVisual();
        }
    }

    /// <summary>Whether <paramref name="def"/> is drawn with a tag.</summary>
    private bool Tagged(NodeDef def) => undescribed.Contains(def.TypeId);

    /// <summary>Where a module's tag sits, in graph units.</summary>
    private static Rect TagBounds(Rect bounds) => new(
        bounds.Right - TagInset - TagWidth,
        bounds.Y + (NodeGeometry.HeaderHeight - TagHeight) / 2,
        TagWidth,
        TagHeight);

    /// <summary>How much of the header a tag takes from the title, gap included.</summary>
    private const double TagRoom = TagWidth + TagInset;

    private static void DrawTag(DrawingContext context, Rect bounds)
    {
        var tag = TagBounds(bounds);

        context.DrawRectangle(null, TagPen, new RoundedRect(tag, 3));

        for (var i = -1; i <= 1; i++)
            context.DrawEllipse(TagDots, null, new Point(tag.Center.X + i * 4, tag.Center.Y), 1.1, 1.1);
    }

    /// <summary>
    /// The module whose tag is under <paramref name="graph"/>, which has to be the
    /// topmost module there too — a tag covered by another module is not being
    /// pointed at.
    /// </summary>
    private NodeInstance? HitTag(Point graph) =>
        Scene.HitNode(graph) is { } node
        && NodeCatalog.Get(node.TypeId) is { } def
        && Tagged(def)
        && TagBounds(NodeGeometry.Bounds(node, def)).Inflate(2).Contains(graph)
            ? node
            : null;
}
