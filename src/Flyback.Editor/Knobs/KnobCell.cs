using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Knobs;

/// <summary>
/// One knob on the knob panel: its name, the knob or switch it is played with, the
/// controller it follows and its menu. Its reading is in the knob's tip, not under it.
/// </summary>
internal sealed class KnobCell
{
    private static readonly IBrush Heard = new ImmutableSolidColorBrush(Colors.Attention);

    private static readonly IBrush SwapTint = new ImmutableSolidColorBrush(Colors.Attention, 0.18);

    /// <summary>How far the name has to travel before a press is a move rather than a click.</summary>
    private const double DragThreshold = 5;

    /// <summary>How wide a knob's cell is.</summary>
    public const double Width = 76;

    private readonly ControlsPanel panel;
    private readonly PatchControl control;
    private readonly int count;
    private Point? pressed;
    private bool dragging;
    private readonly TextBlock name;
    private readonly TextBox renaming;
    private readonly TextBlock midi;
    private readonly Border dot;
    private readonly DispatcherTimer fade;

    public KnobCell(ControlsPanel panel, PatchControl control, int index, int count)
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

        ToolTip.SetTip(name, control.Held
            ? "Held: randomizing leaves this knob alone. Click to link sockets to it, drag it between knobs to move it or onto one to swap them, double-click to rename it."
            : "Click to link sockets to this knob, drag it between knobs to move it or onto one to swap them, double-click to rename it.");

        if (control.Held) name.Foreground = Text.Muted;

        renaming = new TextBox { FontSize = Text.Small, IsVisible = false, MinHeight = 0, Padding = new Thickness(2, 0) };

        Played = control.Kind == ControlKind.Knob
            ? new Knob { Name = "panel-knob", Value = control.Value, HorizontalAlignment = HorizontalAlignment.Center }
            : new PanelSwitch
            {
                Name = "panel-switch",
                Momentary = control.Kind == ControlKind.Button,
                Value = control.Value,
                HorizontalAlignment = HorizontalAlignment.Center,
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
            Content = Glyphs.Dots(12),
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

        var body = new StackPanel
        {
            Width = Width,
            Spacing = 1,
            Children =
            {
                new Panel { Children = { name, renaming } },
                Played,
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

        Knob.Turned += turned => panel.Turn(control.Id, (float)turned);
        Knob.Released += () => panel.LetGo(control.Id);

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
                pressed = e.GetPosition(panel.Strip);
                dragging = false;
                e.Pointer.Capture(name);
            }

            e.Handled = true;
        };

        name.PointerMoved += (_, e) =>
        {
            if (pressed is not { } from) return;

            var at = e.GetPosition(panel.Strip);

            if (!dragging && Math.Abs(at.X - from.X) + Math.Abs(at.Y - from.Y) < DragThreshold) return;

            dragging = true;
            body.Opacity = 0.45;

            panel.ClearDropMarks();
            if (panel.DropAt(at) is { } drop && (drop.After is not null || drop.Beside != this))
                drop.Beside.Mark(drop.After switch
                {
                    true => DropMark.After,
                    false => DropMark.Before,
                    null => DropMark.Swap,
                });
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
                panel.Requests?.ToggleLink(control.Id);
                return;
            }

            if (panel.DropAt(e.GetPosition(panel.Strip)) is not { } drop) return;

            if (drop.After is null)
            {
                if (drop.Beside != this) panel.Requests?.Swap(control.Id, drop.Beside.control.Id);
                return;
            }

            // Counted among the knobs as they stand, so one landing past its old
            // place is one fewer along once it has been taken out.
            var to = drop.Index > Index ? drop.Index - 1 : drop.Index;

            if (to != Index) panel.Requests?.Move(control.Id, to);
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

    /// <summary>
    /// Lights where a dragged knob would land: an edge to slip in at, or the whole cell
    /// to swap with, tinted and lit on both sides so the cell keeps its size.
    /// </summary>
    public void Mark(DropMark mark)
    {
        Root.BorderBrush = mark == DropMark.None ? Brushes.Transparent : Heard;
        Root.Background = mark == DropMark.Swap ? SwapTint : null;
        Root.BorderThickness = mark switch
        {
            DropMark.After => new Thickness(0, 0, 2, 0),
            DropMark.Before => new Thickness(2, 0, 0, 0),
            _ => new Thickness(2, 0),
        };
    }

    /// <summary>What the knob is played with: a knob, or a switch or a button.</summary>
    public Control Played { get; }

    public ITurnable Knob => (ITurnable)Played;

    public void Show(float at, string? reading)
    {
        Knob.Value = at;
        ToolTip.SetTip(Played, reading ?? StageKnobs.Position(control.Kind, at));
    }

    public void Footer(bool learning)
    {
        midi.Text = learning ? control.Kind == ControlKind.Knob ? "turn a knob…" : "press a button…" : control.Midi is { } bound ? panel.LabelOf(bound) : string.Empty;
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

        flyout.Items.Add(Item("Link sockets…", () => panel.Requests?.ToggleLink(control.Id)));

        flyout.Items.Add(control.Midi is null
            ? Item(control.Kind == ControlKind.Knob ? "Learn MIDI controller" : "Learn MIDI button or pad", () => panel.Requests?.Learn(control.Id, onward: false))
            : Item($"Forget {panel.ExplainOf(control.Midi)}", () => panel.Requests?.Forget(control.Id)));

        if (control.Midi is not null)
            flyout.Items.Add(Item("Learn another controller", () => panel.Requests?.Learn(control.Id, onward: false)));

        if (Index < count - 1)
            flyout.Items.Add(Item("Learn this and every knob after it", () => panel.Requests?.Learn(control.Id, onward: true)));

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
                            () => panel.Requests?.Bind(
                                control.Id, new MidiBinding(instrument.Id, track.Channel, knob.Controller)))))
                        .ToList(),
                }).ToList(),
            }).ToList();
        };

        flyout.Items.Add(new MenuItem
        {
            Header = "Play as",
            ItemsSource = new[] { (ControlKind.Knob, "Knob"), (ControlKind.Switch, "Switch"), (ControlKind.Button, "Button") }
                .Select(kind =>
                {
                    var item = Item(kind.Item2, () => panel.Requests?.PlayAs(control.Id, kind.Item1));
                    item.ToggleType = MenuItemToggleType.Radio;
                    item.IsChecked = control.Kind == kind.Item1;
                    return item;
                })
                .ToList(),
        });

        // Ticked as it opens: whether it is depends on the links, which change
        // without the cell being rebuilt.
        var log = new MenuItem { Header = "Logarithmic", ToggleType = MenuItemToggleType.CheckBox, IsVisible = control.Kind == ControlKind.Knob };
        log.Click += (_, _) => panel.Requests?.SweepInDecades(control.Id, panel.Logarithmic?.Invoke(control.Id) != true);
        flyout.Items.Add(log);

        flyout.Opening += (_, _) =>
        {
            var sweeps = panel.Logarithmic?.Invoke(control.Id);
            log.IsEnabled = sweeps is not null;
            log.IsChecked = sweeps == true;
        };

        var hold = new MenuItem { Header = "Hold when randomizing", ToggleType = MenuItemToggleType.CheckBox, IsChecked = control.Held };
        hold.Click += (_, _) => panel.Requests?.Hold(control.Id, !control.Held);
        flyout.Items.Add(hold);

        flyout.Items.Add(new Separator());

        var left = Item("Move left", () => panel.Requests?.Move(control.Id, Index - 1));
        var right = Item("Move right", () => panel.Requests?.Move(control.Id, Index + 1));
        left.IsEnabled = Index > 0;
        right.IsEnabled = Index < count - 1;

        flyout.Items.Add(left);
        flyout.Items.Add(right);
        flyout.Items.Add(Item("Rename", BeginRename));
        flyout.Items.Add(Item("Remove knob", () => panel.Requests?.Remove(control.Id)));

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
            panel.Requests?.Rename(control.Id, text);
    }
}
