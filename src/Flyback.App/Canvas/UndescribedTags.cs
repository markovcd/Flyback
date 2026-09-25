using Avalonia;
using Avalonia.Media;
using Flyback.App.Assist;
using Flyback.Core.Graph;

namespace Flyback.App.Canvas;

/// <summary>
/// The mark on a module the assistant is not told about: a small dotted tag at the
/// right of its header, whose words are the tooltip's and the inspector's.
/// </summary>
/// <remarks>
/// An ellipsis, since what is missing is the module's words, and on the header because
/// that is the one part of a module always on screen. A footnote about the assistant,
/// not about the patch, so it is neither a color nor a badge on the body.
/// </remarks>
internal sealed class UndescribedTags(CanvasSelection selection, Repaint repaint, NodeGeometry geometry)
{
    private const double TagWidth = 16, TagHeight = 10, TagInset = 7;

    /// <summary>How much of the header a tag takes from the title, gap included.</summary>
    public const double TagRoom = TagWidth + TagInset;

    private static readonly IPen TagPen = new Pen(new SolidColorBrush(Avalonia.Media.Colors.White, 0.75));
    private static readonly IBrush TagDots = new SolidColorBrush(Avalonia.Media.Colors.White, 0.75);

    /// <summary>
    /// Type ids whose descriptions the assistant's briefing leaves out (see
    /// <see cref="AssistantPanel.Undescribed"/>). Each module of one is tagged.
    /// </summary>
    public IReadOnlySet<string> Types
    {
        get;
        set
        {
            field = value;
            repaint.Request();
        }
    } = new HashSet<string>();

    /// <summary>Whether <paramref name="def"/> is drawn with a tag.</summary>
    public bool Tagged(NodeDef def) => Types.Contains(def.TypeId);

    /// <summary>
    /// The module whose tag is under <paramref name="graph"/>, which has to be the
    /// topmost module there too: a tag covered by another module is not pointed at.
    /// </summary>
    public NodeInstance? Hit(Point graph) =>
        selection.Scene.HitNode(graph) is { } node
        && NodeCatalog.Get(node.TypeId) is { } def
        && Tagged(def)
        && TagBounds(geometry.Bounds(node, def)).Inflate(2).Contains(graph)
            ? node
            : null;

    /// <param name="ink">
    /// The title's own ink, for a module drawn with <see cref="ModuleSkin.ContrastText"/>
    /// on; null draws the ordinary white.
    /// </param>
    public static void Draw(DrawingContext context, Rect bounds, IBrush? ink)
    {
        var tag = TagBounds(bounds);
        var pen = ink is null ? TagPen : new Pen(ink);
        var dots = ink ?? TagDots;

        context.DrawRectangle(null, pen, new RoundedRect(tag, 3));

        for (var i = -1; i <= 1; i++)
            context.DrawEllipse(dots, null, new Point(tag.Center.X + i * 4, tag.Center.Y), 1.1, 1.1);
    }

    /// <summary>Where a module's tag sits, in graph units.</summary>
    private static Rect TagBounds(Rect bounds) => new(
        bounds.Right - TagInset - TagWidth,
        bounds.Y + (NodeGeometry.HeaderHeight - TagHeight) / 2,
        TagWidth,
        TagHeight);
}
