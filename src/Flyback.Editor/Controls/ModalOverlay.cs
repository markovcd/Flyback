using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Controls;

/// <summary>
/// The dimmed sheet a dialog sits on, and everything that makes it modal.
/// </summary>
/// <remarks>
/// Three things stand between the question and the shell. The sheet is painted
/// rather than merely present, which is what makes it take a click. It takes the
/// focus, or the canvas would still have it and Delete would still delete. And it
/// swallows every key that reaches it unhandled, because the window listens for
/// Ctrl+Z above whatever has the focus and would otherwise undo an edit while
/// asking whether to save it.
/// </remarks>
internal sealed class ModalOverlay : Border
{
    /// <summary>How much of the window a dialog may take before it scrolls.</summary>
    private const double Inset = 40;

    private const double Usual = 720;

    private const double Wide = 1280;

    private readonly double widest;

    private readonly TaskCompletionSource<object?> answered = new();

    /// <summary>
    /// The layer this is standing in, kept only so the window can stop being
    /// watched when the dialog comes down.
    /// </summary>
    private Visual? layer;

    private Border? frame;

    private Control[] parts = [];

    private ScrollViewer? scroller;

    public ModalOverlay(string title, Func<Action<object?>, Control> content, Control? header = null, bool fill = false, bool wide = false, bool top = false)
    {
        widest = wide ? Wide : Usual;

        Name = "modal";
        Background = new SolidColorBrush(Colors.Scrim);

        // So it can hold the keyboard rather than merely block the mouse.
        Focusable = true;

        // A click on the sheet does nothing at all, and deliberately. It is
        // painted, so the click stops here rather than reaching the patch — but
        // stopping it is the whole of the job: a dialog that also went away
        // when the sheet was clicked would be one a missed button press could
        // dismiss, and the two dialogs that are read rather than answered are
        // exactly the ones somebody clicks around in while reading.
        Child = Frame(title, content(Answer), header, fill, wide, top);
    }

    /// <summary>Completes when the dialog has been answered or dismissed.</summary>
    public Task<object?> Answered => answered.Task;

    /// <summary>
    /// The answer, and the end of it. Nothing after the first: a dialog with
    /// three buttons on it can be double-clicked like anything else.
    /// </summary>
    public void Answer(object? result) => answered.TrySetResult(result);

    /// <summary>
    /// Takes the size of the layer it is put in, and keeps taking it.
    /// </summary>
    /// <remarks>
    /// Stretching is not available: the overlay layer is a <see cref="Canvas"/>,
    /// which gives every child the size it asked for. A sheet the size of the dialog
    /// on it would leave the rest of the window clickable, and it has to be re-taken
    /// because a window can be resized while a dialog is up.
    /// </remarks>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Parent is not Visual host) return;

        layer = host;
        layer.PropertyChanged += Resized;

        Cover();

        // Every dialog passes through here, which makes this the one place
        // that needs to know a question was just put up rather than the place
        // that asked it — see Attention.
        if (this.FindAncestorOfType<Window>() is { } window) Attention.Request(window);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (layer is not null) layer.PropertyChanged -= Resized;

        layer = null;
    }

    private void Resized(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty) Cover();
    }

    private void Cover()
    {
        if (layer is null) return;

        Width = layer.Bounds.Width;
        Height = layer.Bounds.Height;

        Fit(layer.Bounds.Width);
    }

    /// <summary>
    /// Gives up margin before content: the sides shrink to nothing as the window
    /// narrows to the dialog's minimum, and below that the dialog scrolls sideways.
    /// </summary>
    private void Fit(double window)
    {
        if (frame is null || scroller is null) return;

        var narrowest = Math.Min(parts.Max(Needs) + 2, widest);
        var side = Math.Clamp((window - narrowest) / 2, 0, Inset);

        frame.Margin = new Thickness(side, Inset, side, Inset);
        frame.MinWidth = Math.Min(narrowest, window);

        scroller.HorizontalScrollBarVisibility = narrowest > window
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape) Answer(null);

        // A key typed into a box is left unhandled. A text box does not mark an
        // ordinary key press handled, and Windows delivers the character only for
        // a key press nobody handled, so swallowing it here would leave the box
        // unable to be typed into. The window ignores keys while a dialog is up
        // (see EditorView.OnKeyDown), which is what the swallowing is for.
        if (e.Source is TextBox) return;

        // Anything still unhandled here was on its way to a window that is
        // listening for Ctrl+Z, Ctrl+L and Escape whatever has the focus.
        e.Handled = true;
    }

    /// <summary>
    /// The width <paramref name="part"/> cannot be drawn narrower than: what it asks
    /// for through <see cref="Layoutable.MinWidth"/> or <see cref="Layoutable.Width"/>,
    /// with its margin. Measuring would not say, because a measure is clamped to the room
    /// it is offered.
    /// </summary>
    private static double Needs(Control part)
        => Math.Max(part.MinWidth, double.IsNaN(part.Width) ? 0 : part.Width) + part.Margin.Left + part.Margin.Right;

    private Control Frame(string title, Control content, Control? header, bool fill, bool wide, bool top)
    {
        var heading = new TextBlock
        {
            Text = title,
            FontSize = Text.Emphasis,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // The only way out of the two dialogs that ask nothing and so have no
        // buttons of their own.
        var dismiss = new Button
        {
            Name = "dismiss",
            Content = Glyphs.Cross(14),
            Width = 28,
            Height = 24,
            Padding = new Thickness(0),
            FontSize = Text.Body,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        dismiss.Click += (_, _) => Answer(null);
        ToolTip.SetTip(dismiss, "Close  (Esc)");

        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(16, 8, 8, 0),
        };

        Grid.SetColumn(dismiss, 1);
        bar.Children.Add(heading);
        bar.Children.Add(dismiss);

        var inside = new DockPanel();

        DockPanel.SetDock(bar, Dock.Top);
        inside.Children.Add(bar);

        if (header is not null)
        {
            DockPanel.SetDock(header, Dock.Top);
            inside.Children.Add(header);
        }

        // Scrolled rather than clipped. Everything shown this way today fits in
        // any window this one is allowed to be, but a settings panel grows a row
        // every time a plugin adds a setting, and a dialog whose buttons are off
        // the bottom of the screen cannot be answered at all.
        scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = wide ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            Content = content,
        };

        inside.Children.Add(scroller);

        parts = header is null ? [bar, content] : [bar, header, content];

        return frame = new Border
        {
            Name = "dialog",
            Background = new SolidColorBrush(Colors.Panel),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),

            // Lifted off the sheet rather than printed on it, so the edge of the
            // question reads at a glance against a dimmed patch that is busy.
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetY = 10,
                Blur = 32,
                Spread = 2,
                Color = Colors.DialogShadow,
            }),

            // Centered and no bigger than it has to be, unless asked to hold its size or to keep
            // to the top, out of an on-screen keyboard's way.
            HorizontalAlignment = fill ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
            VerticalAlignment = fill ? VerticalAlignment.Stretch : top ? VerticalAlignment.Top : VerticalAlignment.Center,
            Margin = new Thickness(Inset),
            MaxWidth = widest,

            // So a Tab does not walk out of the question and into the patch
            // behind it, which is the one thing left that a dimmed sheet cannot
            // stop on its own.
            [KeyboardNavigation.TabNavigationProperty] = KeyboardNavigationMode.Cycle,

            Child = inside,
        };
    }
}