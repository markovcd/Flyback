using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Flyback.App.Controls;

/// <summary>
/// A panel over the window, to be dealt with before anything else happens.
/// </summary>
/// <remarks>
/// Built here rather than asked of the platform, because Avalonia has no message
/// box and one made by hand is the same palette, the same theme and the same
/// font as everything else in the program — which a native one is not, on any of
/// the three platforms this ships to.
/// <para>
/// A panel and not a window. A second window is a second thing in the task
/// switcher, a second thing to place on a screen, and on three platforms three
/// different frames around the same three buttons — for a question that is about
/// this window and belongs on it. What makes it modal instead is what a modal
/// window is actually for: the shell behind is dimmed, cannot be clicked, and
/// does not hear the keyboard. See <see cref="ModalOverlay"/>.
/// </para>
/// <para>
/// Deliberately plain: a title, a way out, and whatever it was given. What a
/// dialog asks and what its answers are belong to whatever put it up, and the
/// three here have nothing in common but their frame.
/// </para>
/// </remarks>
internal static class Dialog
{
    extension(Window owner)
    {
        public Task ShowDialog(string title, Control content) =>
            owner.ShowDialog<object?>(title, content);

        /// <summary>
        /// Puts <paramref name="content"/> over the window and waits for it to be
        /// answered — by <see cref="Close{TResult}"/>, or by the two ways out the
        /// frame provides: the cross on it, and Escape.
        /// </summary>
        /// <remarks>
        /// Dismissing it comes back as <c>default</c>: the answer nobody gave
        /// should be the one that loses nothing, and an enum whose first member
        /// is Cancel gets that from the language rather than from a line of code
        /// remembering.
        /// </remarks>
        public async Task<TResult> ShowDialog<TResult>(string title, Control content)
        {
            // Avalonia's own layer for things drawn over a window — what a flyout
            // or a tooltip is put in. Using it rather than a panel of our own
            // means the shell's layout is not rearranged to make room for a
            // dialog it has nothing to do with. There is none before the window
            // has been shown, and nothing to show a dialog on either.
            if (OverlayLayer.GetOverlayLayer(owner) is not { } layer) return default!;

            // Where the keyboard was, so it can be put back. The overlay takes
            // the focus, and giving it to the canvas afterwards instead of to
            // whatever had it is its own small rudeness.
            var before = owner.FocusManager.GetFocusedElement();

            var overlay = new ModalOverlay(title, content);

            layer.Children.Add(overlay);
            overlay.Focus();

            try
            {
                return await overlay.Answered is TResult answer ? answer : default!;
            }
            finally
            {
                layer.Children.Remove(overlay);
                before?.Focus();
            }
        }
    }

    /// <summary>
    /// Answers the dialog <paramref name="from"/> is in, and takes it down.
    /// </summary>
    /// <remarks>
    /// The control finds its own dialog rather than being handed one, because
    /// the content is built before there is a dialog to hand it.
    /// </remarks>
    public static void Close<TResult>(Control from, TResult result) =>
        from.FindAncestorOfType<ModalOverlay>()?.Answer(result);
}

/// <summary>
/// The dimmed sheet a dialog sits on, and everything that makes it modal.
/// </summary>
/// <remarks>
/// Three things stand between the question and the shell, and all three are
/// needed. The sheet is painted rather than merely present, which is what makes
/// it take a click instead of letting one through to the patch underneath. It
/// takes the focus, or the canvas would still have it and Delete would still
/// delete. And it swallows every key that reaches it unhandled, because the
/// window is listening for Ctrl+Z above whatever has the focus and would
/// otherwise undo an edit while being asked whether to save it.
/// </remarks>
internal sealed class ModalOverlay : Border
{
    /// <summary>How much of the window a dialog may take before it scrolls.</summary>
    private const double Inset = 40;

    private const double Widest = 720;

    private readonly TaskCompletionSource<object?> answered = new();

    /// <summary>
    /// The layer this is standing in, kept only so the window can stop being
    /// watched when the dialog comes down.
    /// </summary>
    private Visual? layer;

    public ModalOverlay(string title, Control content)
    {
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
        Child = Frame(title, content);
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
    /// Stretching is not available here: the overlay layer is a
    /// <see cref="Canvas"/>, and a canvas gives every child the size the child
    /// asked for and puts it at a point. A sheet that came out the size of the
    /// dialog on it would leave the rest of the window clickable, which is the
    /// whole thing this is for — and it has to be re-taken rather than read
    /// once, because a window can be resized while a dialog is up.
    /// </remarks>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (Parent is not Visual host) return;

        layer = host;
        layer.PropertyChanged += Resized;

        Cover();
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
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape) Answer(null);

        // Anything still unhandled here was on its way to a window that is
        // listening for Ctrl+Z, Ctrl+L and Escape whatever has the focus. A key
        // typed into a box in the dialog never reaches this: the box handled it,
        // and a handled event does not raise this at all.
        e.Handled = true;
    }

    private Control Frame(string title, Control content)
    {
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // The only way out of the two dialogs that ask nothing and so have no
        // buttons of their own.
        var dismiss = new Button
        {
            Name = "dismiss",
            Content = "✕",
            Width = 28,
            Height = 24,
            Padding = new Thickness(0),
            FontSize = 12,
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

        // Scrolled rather than clipped. Everything shown this way today fits in
        // any window this one is allowed to be, but a settings panel grows a row
        // every time a plugin adds a setting, and a dialog whose buttons are off
        // the bottom of the screen cannot be answered at all.
        inside.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
        });

        return new Border
        {
            Name = "dialog",
            Background = new SolidColorBrush(Colors.Panel),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),

            // Centred and no bigger than it has to be.
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Inset),
            MaxWidth = Widest,

            // So a Tab does not walk out of the question and into the patch
            // behind it, which is the one thing left that a dimmed sheet cannot
            // stop on its own.
            [KeyboardNavigation.TabNavigationProperty] = KeyboardNavigationMode.Cycle,

            Child = inside,
        };
    }
}
