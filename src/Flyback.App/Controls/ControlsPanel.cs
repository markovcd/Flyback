using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The patch's knobs in a strip under the canvas, with a button that adds another.
/// </summary>
/// <remarks>
/// Draws what it is shown and reports what the hand does; the window decides what
/// that does to the patch. Clicking a knob's name starts linking sockets to it.
/// </remarks>
internal sealed class ControlsPanel : Border
{
    private const double CellWidth = 76;

    private readonly StackPanel strip = new() { Orientation = Orientation.Horizontal, Spacing = 2 };

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
        Height = 112;

        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
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

    public event Action<Guid>? ForgetRequested;

    public event Action<Guid, string>? Renamed;

    public event Action<Guid>? RemoveRequested;

    /// <summary>
    /// What a knob's value reads as, given its id and position — the socket's own
    /// units where it drives exactly one. Unset, the position itself.
    /// </summary>
    public Func<Guid, float, string?>? Reading { get; set; }

    /// <summary>What the tip on a knob says, given its id — which sockets follow it.</summary>
    public Func<Guid, string>? Describe { get; set; }

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
        var now = string.Join('|', controls.Select(c => $"{c.Id:N}{c.Name}{c.Midi?.Label}"));

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

        foreach (var control in controls)
        {
            var cell = new Cell(this, control);
            cells[control.Id] = cell;
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

    private sealed class Cell
    {
        private static readonly IBrush Heard = new SolidColorBrush(Colors.Attention);

        private readonly ControlsPanel panel;
        private readonly PatchControl control;
        private readonly TextBlock name;
        private readonly TextBox renaming;
        private readonly TextBlock value;
        private readonly TextBlock midi;
        private readonly Border dot;
        private readonly DispatcherTimer fade;

        public Cell(ControlsPanel panel, PatchControl control)
        {
            this.panel = panel;
            this.control = control;

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

            ToolTip.SetTip(name, "Click to link sockets to this knob. Double-click to rename it.");

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

            midi = new TextBlock
            {
                FontSize = Text.Micro,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
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
            footer.Children.Add(more);
            footer.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { dot, midi },
            });

            Root = new StackPanel
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

            Footer(learning: false);

            fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            fade.Tick += (_, _) =>
            {
                dot.Opacity = 0;
                fade.Stop();
            };

            Knob.Turned += turned => panel.Turning?.Invoke(control.Id, (float)turned);
            Knob.Released += () => panel.TurnEnded?.Invoke(control.Id);

            name.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(name).Properties.IsLeftButtonPressed) return;

                if (e.ClickCount == 2) BeginRename();
                else panel.LinkRequested?.Invoke(control.Id);

                e.Handled = true;
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

        public StackPanel Root { get; }

        public Knob Knob { get; }

        public void Show(float at, string? reading)
        {
            Knob.Value = at;
            value.Text = reading ?? at.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        public void Footer(bool learning)
        {
            midi.Text = learning ? "turn a knob…" : control.Midi?.Label ?? string.Empty;
            midi.Foreground = learning ? Heard : Text.Muted;
        }

        public void Flash()
        {
            dot.Opacity = 1;
            fade.Stop();
            fade.Start();
        }

        private MenuFlyout Menu()
        {
            var flyout = new MenuFlyout();

            flyout.Opening += (_, _) =>
            {
                flyout.Items.Clear();

                flyout.Items.Add(Item("Link sockets…", () => panel.LinkRequested?.Invoke(control.Id)));

                flyout.Items.Add(control.Midi is null
                    ? Item("Learn MIDI controller", () => panel.LearnRequested?.Invoke(control.Id))
                    : Item($"Forget {control.Midi.Label}", () => panel.ForgetRequested?.Invoke(control.Id)));

                if (control.Midi is not null)
                    flyout.Items.Add(Item("Learn another controller", () => panel.LearnRequested?.Invoke(control.Id)));

                flyout.Items.Add(new Separator());
                flyout.Items.Add(Item("Rename", BeginRename));
                flyout.Items.Add(Item("Remove knob", () => panel.RemoveRequested?.Invoke(control.Id)));
            };

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
