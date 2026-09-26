using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Knobs;

/// <summary>
/// The patch's knobs under the canvas, wrapping onto more rows as they run out of
/// width, with a button that adds another.
/// </summary>
/// <remarks>
/// Draws what it is shown and reports what the hand does; the window decides what
/// that does to the patch. Clicking a knob's name starts linking sockets to it, and
/// dragging the name moves the knob.
/// </remarks>
internal sealed class ControlsPanel : Border
{
    private const double CellWidth = 76;

    private readonly WrapPanel strip = new() { Orientation = Orientation.Horizontal, ItemSpacing = 2, LineSpacing = 4 };

    private readonly Dictionary<Guid, Cell> cells = [];

    private string? shape;

    private Guid? linking;
    private Guid? learning;

    public ControlsPanel()
    {
        Name = "controls-panel";
        Background = new SolidColorBrush(Colors.Panel);
        BorderBrush = new SolidColorBrush(Colors.Edge);
        BorderThickness = new Thickness(0, 1, 0, 0);

        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = strip,
            Padding = new Thickness(8, 6),
        };
    }

    /// <summary>Somebody asked for another knob.</summary>
    public event Action? AddRequested;

    /// <summary>A knob is being turned on screen: its id and where it now is.</summary>
    public event Action<Guid, float>? Turning;

    /// <summary>The hand came off a knob it had turned.</summary>
    public event Action<Guid>? TurnEnded;

    /// <summary>Linking sockets to this knob was asked for, or asked to stop.</summary>
    public event Action<Guid>? LinkRequested;

    public event Action<Guid>? LearnRequested;

    /// <summary>Learn this knob, then each one after it in turn.</summary>
    public event Action<Guid>? LearnOnwardRequested;

    public event Action<Guid>? ForgetRequested;

    public event Action<Guid, string>? Renamed;

    public event Action<Guid>? RemoveRequested;

    /// <summary>A knob's sockets were asked to follow it in decades, or evenly.</summary>
    public event Action<Guid, bool>? LogarithmicRequested;

    /// <summary>Whether a knob sweeps its sockets in decades, given its id; null where it drives none.</summary>
    public Func<Guid, bool?>? Logarithmic { get; set; }

    /// <summary>A knob was moved: its id, and the place it should have once taken out of its old one.</summary>
    public event Action<Guid, int>? MoveRequested;

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

    /// <summary>A knob was bound from the list, to this.</summary>
    public event Action<Guid, MidiBinding>? BindRequested;

    private string LabelOf(MidiBinding binding) => Label?.Invoke(binding) ?? binding.Label;

    private string ExplainOf(MidiBinding binding) => Explain?.Invoke(binding) ?? binding.Label;

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

    /// <summary>Shows <paramref name="controls"/>, rebuilding only where a knob came, went or was renamed.</summary>
    public void Show(IReadOnlyList<PatchControl> controls)
    {
        var now = string.Join('|', controls.Select(c => $"{c.Id:N}{c.Name}{(c.Midi is { } bound ? LabelOf(bound) : null)}"));

        if (now != shape)
        {
            shape = now;
            Rebuild(controls);
        }

        foreach (var control in controls)
            if (cells.TryGetValue(control.Id, out var cell))
            {
                cell.Show(control.Value, Reading?.Invoke(control.Id, control.Value));
                ToolTip.SetTip(cell.Knob, Describe?.Invoke(control.Id));
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
        cells.Clear();

        for (var i = 0; i < controls.Count; i++)
        {
            var cell = new Cell(this, controls[i], i, controls.Count);
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
        add.Click += (_, _) => AddRequested?.Invoke();

        strip.Children.Add(add);

        if (controls.Count == 0)
            strip.Children.Add(new TextBlock
            {
                Text = "No knobs yet. Add one, then link sockets to it or learn a MIDI controller for it.",
                Foreground = Text.Muted,
                FontSize = Text.Body,
                VerticalAlignment = VerticalAlignment.Center,
            });

        Linking = linking;
        Learning = learning;
    }

    /// <summary>
    /// Where a knob dragged to <paramref name="point"/> (in the strip's coordinates)
    /// lands, as an index among all the knobs, and which cell edge marks it.
    /// </summary>
    private (int Index, Cell Beside, bool After)? DropAt(Point point)
    {
        var ordered = cells.Values.OrderBy(c => c.Index).ToList();

        if (ordered.Count == 0) return null;

        // The nearest cell rather than the one under the pointer, so a drop in a gap
        // or past the end of a wrapped row still lands somewhere.
        var nearest = ordered.MinBy(c => Distance(c.Root.Bounds, point))!;
        var after = point.X > nearest.Root.Bounds.Center.X;

        return (nearest.Index + (after ? 1 : 0), nearest, after);

        static double Distance(Rect r, Point p)
        {
            var dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right);
            var dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
            return dx * dx + dy * dy;
        }
    }

    private void ClearDropMarks()
    {
        foreach (var cell in cells.Values) cell.Mark(null);
    }

    private sealed class Cell
    {
        private static readonly IBrush Heard = new SolidColorBrush(Colors.Attention);

        /// <summary>How far the name has to travel before a press is a move rather than a click.</summary>
        private const double DragThreshold = 5;

        private readonly ControlsPanel panel;
        private readonly PatchControl control;
        private readonly int count;
        private readonly StackPanel body;
        private Point? pressed;
        private bool dragging;
        private readonly TextBlock name;
        private readonly TextBox renaming;
        private readonly TextBlock value;
        private readonly TextBlock midi;
        private readonly Border dot;
        private readonly DispatcherTimer fade;

        public Cell(ControlsPanel panel, PatchControl control, int index, int count)
        {
            this.panel = panel;
            this.control = control;
            this.count = count;
            Index = index;

            name = new TextBlock
            {
                Name = "knob-name",
                Text = control.Name,
                FontSize = Text.Small,
                Foreground = new SolidColorBrush(Colors.Label),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
            };

            ToolTip.SetTip(name, "Click to link sockets to this knob, drag to move it, double-click to rename it.");

            renaming = new TextBox { FontSize = Text.Small, IsVisible = false, MinHeight = 0, Padding = new Thickness(2, 0) };

            Knob = new Knob { Value = control.Value, HorizontalAlignment = HorizontalAlignment.Center };

            value = new TextBlock
            {
                FontSize = Text.Caption,
                Foreground = new SolidColorBrush(Colors.Value),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            dot = new Border
            {
                Width = 5,
                Height = 5,
                CornerRadius = new CornerRadius(2.5),
                Background = Heard,
                Opacity = 0,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 3, 0),
            };

            // Cut short with an ellipsis rather than run into the next cell: a
            // binding named by its instrument is longer than a cell is wide.
            midi = new TextBlock
            {
                FontSize = Text.Micro,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var more = new Button
            {
                Name = "knob-menu",
                Content = "⋯",
                Padding = new Thickness(4, 0),
                MinHeight = 0,
                Height = 16,
                FontSize = Text.Small,
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
            };

            more.Flyout = Menu();

            var footer = new DockPanel { LastChildFill = true, Height = 16 };
            DockPanel.SetDock(more, Dock.Right);
            DockPanel.SetDock(dot, Dock.Left);
            footer.Children.Add(more);
            footer.Children.Add(dot);
            footer.Children.Add(midi);

            body = new StackPanel
            {
                Width = CellWidth,
                Spacing = 1,
                Children =
                {
                    new Panel { Children = { name, renaming } },
                    Knob,
                    value,
                    footer,
                },
            };

            // An edge on each side, lit only while a dragged knob would land there.
            Root = new Border
            {
                BorderThickness = new Thickness(2, 0),
                BorderBrush = Brushes.Transparent,
                Child = body,
            };

            Footer(learning: false);

            fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            fade.Tick += (_, _) =>
            {
                dot.Opacity = 0;
                fade.Stop();
            };

            Knob.Turned += turned => panel.Turning?.Invoke(control.Id, (float)turned);
            Knob.Released += () => panel.TurnEnded?.Invoke(control.Id);

            // A click links and a drag moves, so which it was is only known once the
            // button comes up or the pointer has traveled.
            name.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(name).Properties.IsLeftButtonPressed) return;

                if (e.ClickCount == 2)
                {
                    pressed = null;
                    BeginRename();
                }
                else
                {
                    pressed = e.GetPosition(panel.strip);
                    dragging = false;
                    e.Pointer.Capture(name);
                }

                e.Handled = true;
            };

            name.PointerMoved += (_, e) =>
            {
                if (pressed is not { } from) return;

                var at = e.GetPosition(panel.strip);

                if (!dragging && Math.Abs(at.X - from.X) + Math.Abs(at.Y - from.Y) < DragThreshold) return;

                dragging = true;
                body.Opacity = 0.45;

                panel.ClearDropMarks();
                if (panel.DropAt(at) is { } drop) drop.Beside.Mark(drop.After);
            };

            name.PointerReleased += (_, e) =>
            {
                if (pressed is null) return;

                var wasDragging = dragging;
                pressed = null;
                dragging = false;
                e.Pointer.Capture(null);
                body.Opacity = 1;
                panel.ClearDropMarks();

                if (!wasDragging)
                {
                    panel.LinkRequested?.Invoke(control.Id);
                    return;
                }

                if (panel.DropAt(e.GetPosition(panel.strip)) is not { } drop) return;

                // Counted among the knobs as they stand, so one landing past its old
                // place is one fewer along once it has been taken out.
                var to = drop.Index > Index ? drop.Index - 1 : drop.Index;

                if (to != Index) panel.MoveRequested?.Invoke(control.Id, to);
            };

            name.PointerCaptureLost += (_, _) =>
            {
                if (pressed is null) return;

                pressed = null;
                dragging = false;
                body.Opacity = 1;
                panel.ClearDropMarks();
            };

            renaming.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) EndRename(keep: true);
                else if (e.Key == Key.Escape) EndRename(keep: false);
                else return;

                e.Handled = true;
            };

            renaming.LostFocus += (_, _) => EndRename(keep: true);
        }

        public Border Root { get; }

        /// <summary>Where this knob stands on the panel.</summary>
        public int Index { get; }

        /// <summary>Lights the edge a dragged knob would land at: after, before, or neither.</summary>
        public void Mark(bool? after)
        {
            Root.BorderBrush = after is null ? Brushes.Transparent : Heard;
            Root.BorderThickness = after switch
            {
                true => new Thickness(0, 0, 2, 0),
                false => new Thickness(2, 0, 0, 0),
                null => new Thickness(2, 0),
            };
        }

        public Knob Knob { get; }

        public void Show(float at, string? reading)
        {
            Knob.Value = at;
            value.Text = reading ?? at.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        public void Footer(bool learning)
        {
            midi.Text = learning ? "turn a knob…" : control.Midi is { } bound ? panel.LabelOf(bound) : string.Empty;
            midi.Foreground = learning ? Heard : Text.Muted;
            ToolTip.SetTip(midi, !learning && control.Midi is { } explained ? panel.ExplainOf(explained) : null);
        }

        public void Flash()
        {
            dot.Opacity = 1;
            fade.Stop();
            fade.Start();
        }

        /// <summary>
        /// The knob's menu, filled before it is ever opened: a flyout sizes itself as
        /// it opens, so items added then show as an empty sliver. A cell is rebuilt
        /// whenever its binding changes, so the items never go stale.
        /// </summary>
        private MenuFlyout Menu()
        {
            var flyout = new MenuFlyout();

            flyout.Items.Add(Item("Link sockets…", () => panel.LinkRequested?.Invoke(control.Id)));

            flyout.Items.Add(control.Midi is null
                ? Item("Learn MIDI controller", () => panel.LearnRequested?.Invoke(control.Id))
                : Item($"Forget {panel.ExplainOf(control.Midi)}", () => panel.ForgetRequested?.Invoke(control.Id)));

            if (control.Midi is not null)
                flyout.Items.Add(Item("Learn another controller", () => panel.LearnRequested?.Invoke(control.Id)));

            if (Index < count - 1)
                flyout.Items.Add(Item("Learn this and every knob after it", () => panel.LearnOnwardRequested?.Invoke(control.Id)));

            // Filled as it opens, since which instruments are plugged in changes;
            // hidden rather than empty where none of them is known by name.
            var bind = new MenuItem { Header = "Bind to" };
            flyout.Items.Add(bind);

            flyout.Opening += (_, _) =>
            {
                var instruments = panel.Instruments?.Invoke() ?? [];

                bind.IsVisible = instruments.Count > 0;
                bind.ItemsSource = instruments.Select(instrument => new MenuItem
                {
                    Header = instrument.Profile.Name,
                    ItemsSource = instrument.Profile.Tracks.Select(track => new MenuItem
                    {
                        Header = track.Name,
                        ItemsSource = instrument.Profile.PagesOf(track)
                            .SelectMany(page => page.Controls.Select(knob => Item(
                                $"{page.Name} {knob.Name}",
                                () => panel.BindRequested?.Invoke(
                                    control.Id, new MidiBinding(instrument.Id, track.Channel, knob.Controller)))))
                            .ToList(),
                    }).ToList(),
                }).ToList();
            };

            // Ticked as it opens: whether it is depends on the links, which change
            // without the cell being rebuilt.
            var log = new MenuItem { Header = "Logarithmic", ToggleType = MenuItemToggleType.CheckBox };
            log.Click += (_, _) => panel.LogarithmicRequested?.Invoke(control.Id, panel.Logarithmic?.Invoke(control.Id) != true);
            flyout.Items.Add(log);

            flyout.Opening += (_, _) =>
            {
                var sweeps = panel.Logarithmic?.Invoke(control.Id);
                log.IsEnabled = sweeps is not null;
                log.IsChecked = sweeps == true;
            };

            flyout.Items.Add(new Separator());

            var left = Item("Move left", () => panel.MoveRequested?.Invoke(control.Id, Index - 1));
            var right = Item("Move right", () => panel.MoveRequested?.Invoke(control.Id, Index + 1));
            left.IsEnabled = Index > 0;
            right.IsEnabled = Index < count - 1;

            flyout.Items.Add(left);
            flyout.Items.Add(right);
            flyout.Items.Add(Item("Rename", BeginRename));
            flyout.Items.Add(Item("Remove knob", () => panel.RemoveRequested?.Invoke(control.Id)));

            return flyout;
        }

        private static MenuItem Item(string header, Action act)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => act();
            return item;
        }

        private void BeginRename()
        {
            renaming.Text = control.Name;
            name.IsVisible = false;
            renaming.IsVisible = true;
            renaming.Focus();
            renaming.SelectAll();
        }

        private void EndRename(bool keep)
        {
            if (!renaming.IsVisible) return;

            renaming.IsVisible = false;
            name.IsVisible = true;

            if (keep && renaming.Text is { } text && text.Trim() != control.Name)
                panel.Renamed?.Invoke(control.Id, text);
        }
    }
}