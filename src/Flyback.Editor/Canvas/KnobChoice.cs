using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;
using Flyback.Editor.Notices;

namespace Flyback.Editor.Canvas;

/// <summary>
/// The list of panel knobs that drops from a socket for Alt+L, or from a linked
/// socket's knob chip: the arrows and Enter or a click pick one, New knob adds one,
/// and Unlink lets a linked socket go.
/// </summary>
internal sealed class KnobChoice(CanvasHistory history, NodeGeometry geometry, Viewport view, Repaint repaint, Reactions reactions)
{
    public const double Width = 240, RowHeight = 32, Padding = 6;

    private const double Gap = 6, Margin = 8;

    /// <summary>One line of the list.</summary>
    public sealed record Row(string Tag, string Text, bool Taken, Guid? Control, bool Unlink = false, bool Current = false);

    /// <summary>The socket the list is open for, or null while it is shut.</summary>
    public SocketPick? For { get; private set; }

    /// <summary>The line Enter would pick.</summary>
    public int Index { get; private set; }

    /// <summary>The lines of the list: each knob on the panel, New knob, and Unlink where the socket follows one.</summary>
    public IReadOnlyList<Row> Rows
    {
        get
        {
            if (For is not { } pick || history.Patch.Find(pick.Node) is not { } node) return [];

            var patch = history.Patch;
            var linked = ControlMap.Of(node, pick.Port) is { } link && patch.Control(link.Control) is not null ? link.Control : (Guid?)null;
            var rows = new List<Row>();
            var knobs = patch.Controls ?? [];

            for (var i = 0; i < knobs.Count; i++)
            {
                var knob = knobs[i];
                var others = ControlMap.Following(patch, knob.Id).Where(f => f.Node.Id != pick.Node || f.Port != pick.Port).ToList();
                var said = knob.Id == linked ? "linked here"
                    : others.Count == 0 ? "free"
                    : Socket(others[0].Node, others[0].Port) + (others.Count > 1 ? $" +{others.Count - 1}" : "");

                rows.Add(new Row(Tag(i), $"{knob.Name} · {said}", others.Count > 0 && knob.Id != linked, knob.Id, Current: knob.Id == linked));
            }

            rows.Add(new Row("+", "New knob", false, null));
            if (linked is { } id) rows.Add(new Row("×", "Unlink", false, id, Unlink: true));

            return rows;
        }
    }

    /// <summary>What a knob is called on a socket's chip: its place on the panel.</summary>
    public static string Tag(int index) => "K" + (index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Opens the list for a socket, on the knob it follows if it follows one.</summary>
    public void Open(SocketPick pick)
    {
        For = pick;

        var linked = Rows.ToList().FindIndex(row => row.Current);
        Index = Math.Max(linked, 0);
        repaint.Request();
    }

    public void Close()
    {
        if (For is null) return;

        For = null;
        repaint.Request();
    }

    /// <summary>Answers a key while the list is open, and says whether it was the list's.</summary>
    public bool Key(Key key, KeyModifiers modifiers)
    {
        if (For is null) return false;

        var count = Rows.Count;

        switch (key)
        {
            case Avalonia.Input.Key.Up:
                Index = (Index - 1 + count) % count;
                break;

            case Avalonia.Input.Key.Down:
                Index = (Index + 1) % count;
                break;

            case Avalonia.Input.Key.Enter:
                Choose(Index);
                return true;

            case Avalonia.Input.Key.Escape:
            case Avalonia.Input.Key.L when (modifiers & KeyModifiers.Alt) != 0:
                Close();
                return true;

            default:
                return false;
        }

        repaint.Request();
        return true;
    }

    /// <summary>Picks a line of the list and shuts it.</summary>
    public void Choose(int index)
    {
        if (For is not { } pick || index < 0 || index >= Rows.Count) return;

        var row = Rows[index];

        Close();
        reactions.Raise(new KnobChosen(pick, row.Control, row.Unlink));
    }

    /// <summary>Where the list stands on screen: under the socket's row, or over it where the readout is below the module.</summary>
    public Rect? Box
    {
        get
        {
            if (For is not { } pick || history.Patch.Find(pick.Node) is not { } node || NodeCatalog.Get(node.TypeId) is not { } def) return null;

            var bounds = geometry.Bounds(node, def);
            var socket = geometry.InputPort(node, def, pick.Port);
            var well = view.OnScreen(geometry.Compact ? new Rect(socket, socket) : ValueWell.At(bounds, socket));
            var (_, above) = TurnReadout.Place(view.OnScreen(bounds), well, view.Size);
            var height = Rows.Count * RowHeight + 2 * Padding;
            var x = Math.Clamp(well.Left - Padding, Margin, Math.Max(Margin, view.Size.Width - Width - Margin));
            var y = above ? well.Bottom + Gap : well.Top - Gap - height;

            return new Rect(x, Math.Clamp(y, Margin, Math.Max(Margin, view.Size.Height - height - Margin)), Width, height);
        }
    }

    /// <summary>The line under a point on screen, if the list is open and the point is on one.</summary>
    public int? RowAt(Point screen)
    {
        if (Box is not { } box || !box.Contains(screen)) return null;

        var index = (int)((screen.Y - box.Top - Padding) / RowHeight);

        return index >= 0 && index < Rows.Count ? index : null;
    }

    public void Draw(DrawingContext context)
    {
        if (Box is not { } box) return;

        context.DrawRectangle(Ground, Edge, box, 10, 10);

        var rows = Rows;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = new Rect(box.Left + Padding, box.Top + Padding + i * RowHeight, box.Width - 2 * Padding, RowHeight);

            if (i == Index) context.DrawRectangle(Picked, PickedEdge, row.Deflate(0.75), 6, 6);

            var tag = Text(rows[i].Tag, MonoBold, 12, Bright, 24);
            context.DrawText(tag, new Point(row.Left + 10, row.Center.Y - tag.Height / 2));

            var text = Text(rows[i].Text, Typeface.Default, 12.5, rows[i].Taken ? Dim : Light, row.Width - 54);
            context.DrawText(text, new Point(row.Left + 44, row.Center.Y - text.Height / 2));
        }
    }

    private static string Socket(NodeInstance node, int port) =>
        NodeCatalog.Get(node.TypeId) is { } def && port < def.Inputs.Count ? $"{def.Inputs[port].Name} · {node.Title(def)}" : "a socket";

    private static FormattedText Text(string text, Typeface face, double size, IBrush brush, double width) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush)
        {
            MaxTextWidth = width,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

    private static readonly Typeface MonoBold = new(Flyback.Ui.Controls.Text.Mono, weight: FontWeight.Bold);
    private static readonly IBrush Ground = new ImmutableSolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x13), 0.96);
    private static readonly IPen Edge = new ImmutablePen(new ImmutableSolidColorBrush(Avalonia.Media.Colors.White, 0.30));
    private static readonly IBrush Picked = new ImmutableSolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x46));
    private static readonly IPen PickedEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF0)), 1.5);
    private static readonly IBrush Bright = new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8));
    private static readonly IBrush Light = new ImmutableSolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF0));
    private static readonly IBrush Dim = new ImmutableSolidColorBrush(Color.FromRgb(0xB4, 0xBB, 0xC8));
}
