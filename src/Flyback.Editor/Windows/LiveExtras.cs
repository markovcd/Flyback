using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Graph;
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
/// Offered only where the module's rows are drawn: zoomed out past reading, a module is a
/// name and nothing else is reached through it. Over the text the selection is the call the
/// caret stands in. The card holds the inspector's own controls, so an edit goes through the
/// same write-back.
/// </remarks>
internal sealed class LiveExtras : IReactTo<SelectionChanged>, IReactTo<PatchChanged>
{
    private const double Width = 360;

    private readonly LivePatching live;
    private readonly FullScreenPreview fullScreen;
    private readonly NodeEditor editor;
    private readonly SourceView source;
    private readonly ExtraEditors extras;

    private readonly StackPanel body = new() { Spacing = 4 };
    private readonly TextBlock title = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };

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
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        close.Click += (_, _) => Close();
        ToolTip.SetTip(close, "Shut  (Esc)");

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
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
            Width = Width,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 64, 16, 64),
            Padding = new Thickness(14, 10, 14, 14),
            Background = new SolidColorBrush(Colors.Panel, 0.94),
            BorderBrush = new SolidColorBrush(Colors.Separator),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Child = content,
        };

        live.Changed += Sync;
        editor.View.OverviewChanged += Sync;
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
        title.Text = node.Name;
        body.Children.Clear();

        foreach (var control in extras.Of(node, def, reading: false)) body.Children.Add(control);
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

        Sync();
        return Task.CompletedTask;
    }
}
