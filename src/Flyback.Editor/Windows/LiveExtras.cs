using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Editor.Canvas;
using Flyback.Editor.Hubs;
using Flyback.Editor.Inspect;
using Flyback.Editor.Notices;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Windows;

/// <summary>
/// What the selected module carries, on a card over the live picture, opened from the transport's
/// [ ] button or Ctrl+I, since the inspector is put away while the picture has the window.
/// </summary>
/// <remarks>
/// The card hangs under the module it edits, or beside it where there is no room below, or at
/// the caret's line on the right over the text, and follows it as the view moves.
/// </remarks>
/// <remarks>
/// Offered only where the module's rows are drawn: zoomed out past reading, a module is a
/// name and nothing else is reached through it. Over the text the selection is the call the
/// caret stands in. The card holds the inspector's own controls, so an edit goes through the
/// same write-back.
/// </remarks>
internal sealed class LiveExtras : IReactTo<SelectionChanged>, IReactTo<PatchChanged>
{
    /// <summary>Wide enough for a keyboard's octave; a card of choices is wider, to fit more buttons a row.</summary>
    private const double Narrow = 380;

    private const double Wide = 440;

    private double Width => Card.Width;

    /// <summary>The gap between the card and what it hangs from, and the least it keeps from the picture's edges.</summary>
    private const double Gap = 12;

    private readonly LivePatching live;
    private readonly FullScreenPreview fullScreen;
    private readonly NodeEditor editor;
    private readonly SourceView source;
    private readonly ExtraEditors extras;

    private readonly StackPanel body = new() { Spacing = 12 };
    private readonly TextBlock title = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Colors.Bright), VerticalAlignment = VerticalAlignment.Center };

    private TransportOverlay? overlay;

    /// <summary>The module the card was built for, so an edit to another one leaves it standing.</summary>
    private NodeInstance? shown;

    public LiveExtras(LivePatching live, FullScreenPreview fullScreen, NodeEditor editor, SourceView source, ExtraEditors extras)
    {
        this.live = live;
        this.fullScreen = fullScreen;
        this.editor = editor;
        this.source = source;
        this.extras = extras;

        var close = new Button
        {
            Name = "liveExtrasClose",
            Content = Glyphs.Cross(),
            Background = Brushes.Transparent,
            Width = 32,
            Height = 32,
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        close.Click += (_, _) => Close();
        ToolTip.SetTip(close, "Shut  (Esc)");

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(title);

        var content = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        content.Children.Add(head);
        content.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

        Card = new Border
        {
            Name = "liveExtrasCard",
            IsVisible = false,
            Width = Narrow,
            MaxHeight = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(16, 10, 10, 16),
            Background = new SolidColorBrush(Colors.Window, 0.9),
            BorderBrush = new SolidColorBrush(Colors.Value, 0.3),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = content,
        };

        live.Changed += Sync;
        editor.View.OverviewChanged += Sync;
        editor.View.Moved += Hang;
        source.Moved += (_, _) => Hang();
    }

    /// <summary>The card itself, for the tests.</summary>
    internal Border Card { get; }

    /// <summary>Whether the card is over the picture.</summary>
    public bool IsOpen => Card.IsVisible;

    /// <summary>Puts the [ ] button on the transport over the picture, and listens for Ctrl+I and Escape.</summary>
    public void Attach(TransportOverlay transport)
    {
        overlay = transport;
        transport.ExtrasClicked += Toggle;
        transport.AttachedToVisualTree += (_, _) =>
            TopLevel.GetTopLevel(transport)?.AddHandler(InputElement.KeyDownEvent, KeyPressed, RoutingStrategies.Tunnel);

        Sync();
    }

    /// <summary>
    /// The module whose card can be opened now: the focused one, while it carries anything and
    /// its rows are drawn, or null.
    /// </summary>
    internal NodeInstance? Offered
    {
        get
        {
            if (!live.IsOn || (!live.Coding && editor.View.Overview)) return null;

            return editor.Selection.Focused is { } node && NodeCatalog.Get(node.TypeId) is { } def && ExtraEditors.Has(def) ? node : null;
        }
    }

    /// <summary>Opens the card for the module on offer, or shuts it.</summary>
    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    private void Open()
    {
        if (Offered is not { } node || NodeCatalog.Get(node.TypeId) is not { } def) return;

        Build(node, def);
        Place();
        Card.IsVisible = true;
        Hang();
        Sync();
    }

    public void Close()
    {
        if (!IsOpen) return;

        Card.IsVisible = false;
        shown = null;
        body.Children.Clear();
        (Card.Parent as Panel)?.Children.Remove(Card);
        Sync();
    }

    private void Build(NodeInstance node, NodeDef def)
    {
        shown = node;
        Card.Width = def.Extras.All(extra => extra is ScaleExtra) ? Narrow : Wide;
        title.Text = def.Extras.Count == 1 ? $"{node.Name ?? def.Name} · {def.Extras[0].Key}" : node.Name ?? def.Name;
        body.Children.Clear();

        foreach (var extra in def.Extras)
        {
            if (extras.Large(extra, node, def) is not { } control) continue;

            if (def.Extras.Count > 1)
                body.Children.Add(new TextBlock { Text = extra.Key, FontSize = 12, Foreground = new SolidColorBrush(Colors.Muted) });

            body.Children.Add(control);
        }
    }

    /// <summary>
    /// Hangs the card under the module, or under the caret's line over the text, or over it
    /// where there is no room below, kept inside the picture.
    /// </summary>
    private void Hang()
    {
        if (!IsOpen || shown is null || fullScreen.PreviewBox is not { } picture || NodeCatalog.Get(shown.TypeId) is not { } def) return;

        var room = picture.Bounds.Size;
        Rect? under = null;

        if (live.Coding)
        {
            if (source.CaretBottom() is { } caret && source.TranslatePoint(caret, picture) is { } at)
                under = new Rect(at.X, at.Y - 24, 0, 24);
        }
        else
        {
            var box = editor.View.OnScreen(editor.Geometry.Bounds(shown, def));
            if (editor.TranslatePoint(box.TopLeft, picture) is { } at) under = new Rect(at, box.Size);
        }

        if (under is not { } from) return;

        Card.Measure(Size.Infinity);
        var height = Card.DesiredSize.Height;

        // Over the text the card keeps to the right, clear of the lines it would cover. Under a
        // module it hangs below where there is room, and otherwise stands beside it.
        double left, top;

        if (live.Coding)
            (left, top) = (room.Width - Width - Gap * 2, Below(from) ?? from.Top - Gap - height);
        else if (Below(from) is { } below)
            (left, top) = (from.Center.X - Width / 2, below);
        else
            (left, top) = (from.Right + Gap + Width <= room.Width - Gap ? from.Right + Gap : from.Left - Gap - Width, from.Top);

        Card.Margin = new Thickness(
            Math.Clamp(left, Gap, Math.Max(Gap, room.Width - Width - Gap)),
            Math.Clamp(top, Gap, Math.Max(Gap, room.Height - height - Gap)),
            0,
            0);

        double? Below(Rect over) => over.Bottom + Gap + height <= room.Height - Gap ? over.Bottom + Gap : null;
    }

    /// <summary>Over the picture, above the patch lifted onto it and under the transport.</summary>
    private void Place()
    {
        if (Card.Parent is not null || fullScreen.Columns is not { } columns || fullScreen.PreviewBox is not { } picture) return;

        Control view = live.Coding ? source : editor;
        var under = view.Parent == columns ? view : picture;

        Grid.SetColumn(Card, Grid.GetColumn(picture));
        Grid.SetRow(Card, Grid.GetRow(picture));
        Grid.SetRowSpan(Card, Grid.GetRowSpan(picture));
        columns.Children.Insert(columns.Children.IndexOf(under) + 1, Card);
    }

    /// <summary>Shows the button while there is something to open, and shuts a card left with nothing to show.</summary>
    private void Sync()
    {
        if (IsOpen && (Offered is not { } node || node.Id != shown?.Id))
        {
            Close();
            return;
        }

        if (overlay is null) return;

        overlay.Extras = Offered is not null;
        overlay.ExtrasOpen = IsOpen;
    }

    private void KeyPressed(object? sender, KeyEventArgs e)
    {
        if (!live.IsOn) return;

        if (e.Key == Key.I && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 && (e.KeyModifiers & KeyModifiers.Shift) == 0)
        {
            Toggle();
            e.Handled = true;
        }
        else if (e is { Key: Key.Escape, KeyModifiers: KeyModifiers.None } && IsOpen)
        {
            Close();
            e.Handled = true;
        }
    }

    public Task On(SelectionChanged notice)
    {
        Sync();
        return Task.CompletedTask;
    }

    /// <summary>A module replaced under the card, by an undo or an apply, is built again from its new self.</summary>
    public Task On(PatchChanged notice)
    {
        if (IsOpen && editor.History.Patch.Find(shown!.Id) is { } now && !ReferenceEquals(now, shown) && NodeCatalog.Get(now.TypeId) is { } def)
            Build(now, def);

        Hang();
        Sync();
        return Task.CompletedTask;
    }
}
