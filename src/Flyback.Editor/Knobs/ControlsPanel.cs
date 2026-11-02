using Flyback.Editor.Midi;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Flyback.Ui.Controls;
using Flyback.Core.Graph;
using Colors = Flyback.Ui.Controls.Colors;
using Flyback.Host;

namespace Flyback.Editor.Knobs;

/// <summary>
/// The patch's knobs under the canvas, wrapping onto more rows as they run out of
/// width or standing in a fixed grid, with a button that adds another.
/// </summary>
/// <remarks>
/// Draws what it is shown and reports what the hand does; the window decides what
/// that does to the patch. Clicking a knob's name starts linking sockets to it, and
/// dragging the name moves the knob: between two knobs to slip it in there, onto
/// one to swap the two.
/// </remarks>
internal sealed class ControlsPanel : Border
{
    private readonly KnobArrangement strip = new() { ItemSpacing = 2, LineSpacing = 4 };

    /// <summary>The add button and the randomize cell, beside a grid rather than in it.</summary>
    private readonly StackPanel beside = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };

    private readonly Grid columns = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

    private readonly ScrollViewer scroller;

    private readonly Dictionary<Guid, KnobCell> cells = [];

    private string? shape;

    private Guid? linking;
    private Guid? learning;

    private readonly RollCell roll;

    public ControlsPanel()
    {
        Name = "controls-panel";
        Background = new SolidColorBrush(Colors.Panel);
        BorderBrush = new SolidColorBrush(Colors.Edge);
        BorderThickness = new Thickness(0, 1, 0, 0);

        Grid.SetColumn(beside, 1);
        columns.Children.Add(strip);
        columns.Children.Add(beside);

        Child = scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = columns,
            Padding = new Thickness(8, 6),
        };

        roll = new RollCell(ExplainOf);
    }

    /// <summary>The randomize cell at the end of the panel.</summary>
    public RollCell Roll => roll;

    /// <summary>Shows how a randomize is set: its reach, its glide, its controller and whether there is a back.</summary>
    public void ShowRoll(double amount, double glide, MidiBinding? trigger, bool canGoBack, bool learning) =>
        roll.Show(amount, glide, trigger, canGoBack, learning);

    /// <summary>A knob is being turned on screen: its id and where it now is.</summary>
    public event Action<Guid, float>? Turning;

    /// <summary>The hand came off a knob it had turned.</summary>
    public event Action<Guid>? TurnEnded;

    /// <summary>What is done with what the hand asks of a knob; nothing is, unset.</summary>
    public IKnobRequests? Requests { get; set; }

    /// <summary>What a knob's position on the panel is measured against while it is dragged.</summary>
    internal Visual Strip => strip;

    internal void Turn(Guid id, float value) => Turning?.Invoke(id, value);

    internal void LetGo(Guid id) => TurnEnded?.Invoke(id);

    /// <summary>Whether a knob sweeps its sockets in decades, given its id; null where it drives none.</summary>
    public Func<Guid, bool?>? Logarithmic { get; set; }

    /// <summary>
    /// What a knob's value reads as, given its id and position — the socket's own
    /// units where it drives exactly one. Unset, the position itself.
    /// </summary>
    public Func<Guid, float, string?>? Reading { get; set; }

    /// <summary>What the tip on a knob says, given its id — which sockets follow it.</summary>
    public Func<Guid, string>? Describe { get; set; }

    /// <summary>What a binding is called under its knob, short enough to fit: "T3 · Filter Frequency".</summary>
    public Func<MidiBinding, string>? Label { get; set; }

    /// <summary>What a binding is called in full, for the tooltip and the menu: "Syntakt · Track 3 · Filter Frequency".</summary>
    public Func<MidiBinding, string>? Explain { get; set; }

    /// <summary>
    /// The instruments plugged in that Flyback knows by name, for binding a knob
    /// from a list rather than by turning. Asked as the menu opens, because they
    /// come and go.
    /// </summary>
    public Func<IReadOnlyList<PanelInstrument>>? Instruments { get; set; }

    internal string LabelOf(MidiBinding binding) => Label?.Invoke(binding) ?? binding.Label;

    internal string ExplainOf(MidiBinding binding) => Explain?.Invoke(binding) ?? binding.Label;

    /// <summary>The knob whose sockets are being linked, tinted so it can be found.</summary>
    public Guid? Linking
    {
        get => linking;
        set
        {
            linking = value;
            foreach (var (id, cell) in cells) cell.Knob.Lit = id == value;
        }
    }

    /// <summary>The knob waiting for a controller to move, whose footer says so.</summary>
    public Guid? Learning
    {
        get => learning;
        set
        {
            learning = value;
            foreach (var (id, cell) in cells) cell.Footer(id == value);
        }
    }

    /// <summary>The fixed grid the knobs stand in, or null to wrap them to the panel's width.</summary>
    public KnobGrid? KnobGrid
    {
        get => strip.Grid;
        set
        {
            strip.Grid = value;

            var gridded = strip.Grid is not null;

            // A grid wider than the panel scrolls rather than wraps, which would move a knob.
            scroller.HorizontalScrollBarVisibility = gridded
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            columns.ColumnDefinitions[0].Width = gridded ? GridLength.Auto : GridLength.Star;

            if (shown is { } controls)
            {
                shape = null;
                Show(controls);
            }
        }
    }

    private IReadOnlyList<PatchControl>? shown;

    /// <summary>Shows <paramref name="controls"/>, rebuilding only where a knob came, went or was renamed.</summary>
    public void Show(IReadOnlyList<PatchControl> controls)
    {
        shown = controls;

        var now = string.Join('|', controls.Select(c => $"{c.Id:N}{c.Name}{c.Held}{c.Kind}{(c.Midi is { } bound ? LabelOf(bound) : null)}"));

        if (now != shape)
        {
            shape = now;
            Rebuild(controls);
        }

        foreach (var control in controls)
            if (cells.TryGetValue(control.Id, out var cell))
            {
                cell.Show(control.Value, Reading?.Invoke(control.Id, control.Value));
                ToolTip.SetTip(cell.Played, Describe?.Invoke(control.Id));
            }
    }

    /// <summary>
    /// Moves a knob without reporting it as turned, flashing its light where a
    /// controller moved it.
    /// </summary>
    public void Move(Guid id, float value, bool heard)
    {
        if (!cells.TryGetValue(id, out var cell)) return;

        cell.Show(value, Reading?.Invoke(id, value));
        if (heard) cell.Flash();
    }

    private void Rebuild(IReadOnlyList<PatchControl> controls)
    {
        strip.Children.Clear();
        beside.Children.Clear();
        cells.Clear();

        var after = strip.Grid is null ? strip.Children : beside.Children;

        for (var i = 0; i < controls.Count; i++)
        {
            var cell = new KnobCell(this, controls[i], i, controls.Count);
            cells[controls[i].Id] = cell;
            strip.Children.Add(cell.Root);
        }

        var add = new Button
        {
            Name = "add-knob",
            Content = "+",
            Width = 44,
            Height = 44,
            FontSize = Text.Title,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };

        ToolTip.SetTip(add, "Add a knob. Click a knob's name, then sockets on the canvas, to link them to it.");
        add.Click += (_, _) => Requests?.Add();

        after.Add(add);

        if (controls.Count > 0) after.Add(roll.Root);

        if (controls.Count == 0)
            after.Add(new TextBlock
            {
                Text = "No knobs yet. Add one, then link sockets to it or learn a MIDI controller for it.",
                Foreground = Text.Muted,
                FontSize = Text.Body,
                VerticalAlignment = VerticalAlignment.Center,
            });

        Linking = linking;
        Learning = learning;
    }

    /// <summary>How much of a cell's width, around its middle, swaps rather than slips in beside.</summary>
    private const double SwapShare = 0.6;

    /// <summary>
    /// Where a knob dragged to <paramref name="point"/> (in the strip's coordinates)
    /// lands: onto the middle of a cell, which it swaps with, or else as an index among
    /// all the knobs, with the cell edge that marks it.
    /// </summary>
    internal (int Index, KnobCell Beside, bool? After)? DropAt(Point point)
    {
        var ordered = cells.Values.OrderBy(c => c.Index).ToList();

        if (ordered.Count == 0) return null;

        // The nearest cell rather than the one under the pointer, so a drop in a gap
        // or past the end of a wrapped row still lands somewhere.
        var nearest = ordered.MinBy(c => Distance(c.Root.Bounds, point))!;
        var bounds = nearest.Root.Bounds;

        if (bounds.Contains(point) && Math.Abs(point.X - bounds.Center.X) < bounds.Width * SwapShare / 2)
            return (nearest.Index, nearest, null);

        var after = point.X > bounds.Center.X;

        return (nearest.Index + (after ? 1 : 0), nearest, after);

        static double Distance(Rect r, Point p)
        {
            var dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right);
            var dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
            return dx * dx + dy * dy;
        }
    }

    internal void ClearDropMarks()
    {
        foreach (var cell in cells.Values) cell.Mark(DropMark.None);
    }

}
