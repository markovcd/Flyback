using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Editor.Canvas;
using Flyback.Editor.Hubs;
using Flyback.Editor.Notices;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Sequence;

/// <summary>
/// The pane under the canvas that shows the patch's Song: every part a row of its own
/// length, with its own playhead, scrolled and zoomed along the song (ADR-0229).
/// Collapsed, it is one strip of thin lines.
/// </summary>
internal sealed class SequencePane : IReactTo<PatchCompiled>, IReactTo<DocumentArrived>
{
    /// <summary>How tall the pane stands collapsed.</summary>
    public const double StripHeight = 58;

    private const double HeaderHeight = 34;

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(33);

    private readonly NodeEditor editor;
    private readonly TextWriteBack writeBack;
    private readonly PreviewHost preview;
    private readonly Playback playback;

    private readonly SongRows rows = new() { Name = "sequenceRows" };
    private readonly Avalonia.Controls.Canvas overlay = new();
    private readonly Grid header;
    private readonly TextBlock title = new() { FontSize = Text.Body, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock summary = new() { FontSize = Text.Small, Foreground = Text.Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button add;
    private readonly DispatcherTimer ticker;

    private NodeInstance? song;
    private SongClock? clock;

    public SequencePane(NodeEditor editor, TextWriteBack writeBack, PreviewHost preview, Playback playback)
    {
        this.editor = editor;
        this.writeBack = writeBack;
        this.preview = preview;
        this.playback = playback;

        add = Button("+ part", "Add a part, at nought in every section", () => Edit(parts => parts.Add(), "song add part"));

        header = new Grid
        {
            Height = HeaderHeight,
            Background = new SolidColorBrush(Colors.Toolbar),
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto,Auto"),
        };

        Place(header, 0, new TextBlock
        {
            Text = "SEQUENCE",
            FontSize = Text.Micro,
            FontWeight = FontWeight.SemiBold,
            Foreground = Text.Muted,
            Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Place(header, 1, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { title, summary } });
        Place(header, 3, add);
        Place(header, 4, Button("Fit", "Zoom so the longest part is all in view", Fit));
        Place(header, 5, Button("−", "Zoom out", () => Zoom(1 / 1.4)));
        Place(header, 6, Button("+", "Zoom in", () => Zoom(1.4)));
        Place(header, 7, Button("⌄", "Collapse the pane to one strip", () => Collapsed = true));

        var body = new Panel { Children = { rows, overlay } };

        View = new DockPanel
        {
            Name = "sequencePane",
            IsVisible = false,
            Children = { header, body },
        };
        DockPanel.SetDock(header, Dock.Top);

        rows.Edited += because => Save(because);
        rows.Sought += Seek;
        rows.RenameAsked += Rename;
        rows.LengthAsked += Length;
        rows.MenuAsked += Menu;
        rows.Opened += () => Collapsed = false;
        rows.PointerMoved += (_, e) => ToolTip.SetTip(rows, rows.Describe(e.GetPosition(rows)));

        ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = Tick };
        ticker.Tick += (_, _) => Follow();
        View.AttachedToVisualTree += (_, _) => ticker.Start();
        View.DetachedFromVisualTree += (_, _) => ticker.Stop();
    }

    /// <summary>The pane, header and rows, hidden while the patch has no Song.</summary>
    public DockPanel View { get; }

    /// <summary>Whether the patch has a Song for the pane to show.</summary>
    public bool HasSong => song is not null;

    /// <summary>Whether the pane is down to its strip.</summary>
    public bool Collapsed
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            header.IsVisible = !value;
            rows.Mini = value;
            LayoutChanged?.Invoke();
        }
    }

    /// <summary>The pane appeared, went, or collapsed or opened, so the window gives it its height.</summary>
    public event Action? LayoutChanged;

    /// <summary>What the rows draw, for the tests.</summary>
    internal SongRows Rows => rows;

    public Task On(PatchCompiled notice)
    {
        Refresh();
        return Task.CompletedTask;
    }

    public Task On(DocumentArrived notice)
    {
        Refresh();
        return Task.CompletedTask;
    }

    /// <summary>Finds the patch's Song again and draws it.</summary>
    public void Refresh()
    {
        var patch = editor.History.Patch;
        var was = song is not null;

        song = patch.FirstOf(SongModule.TypeId);
        View.IsVisible = song is not null;

        if (song is { } node && NodeCatalog.Get(node.TypeId) is { } def)
        {
            rows.Accent = Colors.Palette(def).Accent;
            if (!rows.Pressed) rows.Parts = new SongParts(SongExtra.Of(node));
            clock = SongClock.Of(patch, node.Id);
            title.Text = node.Title(def).ToLowerInvariant();
            Summarize();
        }
        else
        {
            clock = null;
        }

        if (was != (song is not null)) LayoutChanged?.Invoke();
    }

    private void Summarize()
    {
        var parts = rows.Parts;
        summary.Text = parts.Count == 0
            ? "no parts yet"
            : string.Create(CultureInfo.InvariantCulture, $"{parts.Count} part{(parts.Count == 1 ? "" : "s")} · longest {parts.Longest} sections");
        add.IsEnabled = parts.Count < NodeCatalog.MaxParts;
    }

    private void Follow()
    {
        if (clock is null || !View.IsVisible) return;
        rows.Played = clock.Played(preview.Time);
    }

    /// <summary>Puts the edited parts on the Song and files the change in the history.</summary>
    private void Save(string? because)
    {
        if (song is not { } node) return;

        SongExtra.Set(node, rows.Parts.Parts);
        rows.Parts = new SongParts(SongExtra.Of(node));
        Summarize();
        writeBack.Edited(node, because);
    }

    private void Edit(Action<SongParts> change, string because)
    {
        change(rows.Parts);
        Save(because);
    }

    private void Seek(double section)
    {
        if (clock?.SecondsAt(section, playback.Length) is { } seconds) playback.SeekTo(seconds);
    }

    private void Fit()
    {
        rows.Layout.Fit();
        rows.InvalidateVisual();
    }

    private void Zoom(double by)
    {
        rows.Layout.Zoom(rows.Layout.Cell * by, RowLayout.NameWidth + rows.Layout.Window / 2);
        rows.InvalidateVisual();
    }

    private void Rename(int part, Rect at) =>
        Ask(at, rows.Parts.Name(part), "Part name", typed =>
        {
            if (rows.Parts.Rename(part, typed)) Save($"song rename part {part}");
        });

    private void Length(int part, Rect at) =>
        Ask(at, rows.Parts.Levels(part).Count.ToString(CultureInfo.InvariantCulture), "Sections this part loops over", typed =>
        {
            if (!int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sections)) return;

            rows.Parts.Resize(part, sections);
            Save($"song part {part} length");
        });

    /// <summary>A box over the rows to type into: Enter or leaving it takes what is typed, Escape does not.</summary>
    private void Ask(Rect at, string text, string tip, Action<string> took)
    {
        overlay.Children.Clear();

        var box = new TextBox
        {
            Text = text,
            Width = Math.Max(at.Width, 60),
            Height = at.Height,
            MinHeight = 0,
            Padding = new Thickness(4, 0),
            FontSize = Text.Body,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(box, tip);
        Avalonia.Controls.Canvas.SetLeft(box, at.X);
        Avalonia.Controls.Canvas.SetTop(box, at.Y - (box.Height - at.Height) / 2);

        var done = false;

        void Finish(bool take)
        {
            if (done) return;
            done = true;

            overlay.Children.Clear();
            if (take) took(box.Text ?? "");
            rows.Focus();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;

            Finish(e.Key == Key.Enter);
            e.Handled = true;
        };
        box.LostFocus += (_, _) => Finish(take: true);

        overlay.Children.Add(box);
        box.AttachedToVisualTree += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
    }

    private void Menu(int part)
    {
        var count = rows.Parts.Count;

        var menu = new ContextMenu
        {
            ItemsSource = new[]
            {
                Item("Move up", part > 0, () => Edit(parts => parts.Move(part, -1), $"song move part {part}")),
                Item("Move down", part < count - 1, () => Edit(parts => parts.Move(part, 1), $"song move part {part}")),
                Item("Duplicate", count < NodeCatalog.MaxParts, () => Edit(parts => parts.Duplicate(part), $"song duplicate part {part}")),
                Item("Remove", true, () => Edit(parts => parts.Remove(part), $"song remove part {part}")),
            },
        };

        menu.Open(rows);
    }

    private static MenuItem Item(string header, bool enabled, Action act)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => act();
        return item;
    }

    private static Button Button(string text, string tip, Action act)
    {
        var button = new Button
        {
            Content = text,
            FontSize = Text.Small,
            Padding = new Thickness(8, 1),
            Margin = new Thickness(0, 0, 6, 0),
            MinHeight = 22,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => act();
        return button;
    }

    private static void Place(Grid grid, int column, Control child)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }
}
