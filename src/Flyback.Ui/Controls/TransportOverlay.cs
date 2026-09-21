using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Flyback.App.Controls;

/// <summary>
/// Sound, pause, rewind and the knobs over a full-window picture, tucked behind three
/// dots in the bottom right corner. Bare glyphs on no bar, drawn the way the knobs are.
/// </summary>
/// <remarks>
/// Holds no state of its own: the owner sets <see cref="Paused"/>, <see cref="Muted"/>
/// and <see cref="Sounding"/>, and hears the clicks.
/// </remarks>
public sealed class TransportOverlay : TuckedAway
{
    private const double Size = 40;
    private const double Gap = 2;
    private const double Inset = 12;

    /// <summary>How far in from the right edge the open transport reaches.</summary>
    public const double Span = Inset + 4 * Size + 3 * Gap;

    private readonly Button muteButton;
    private readonly Button pauseButton;
    private readonly Button knobsButton;

    private bool paused;
    private bool muted;
    private bool knobsShown = true;

    public TransportOverlay()
        : this(new StackPanel { Orientation = Orientation.Horizontal, Spacing = Gap })
    {
    }

    private TransportOverlay(StackPanel buttons)
        : base(buttons, HorizontalAlignment.Right)
    {
        muteButton = Tool(Glyphs.Speaker(), "Sound on or off", () => MuteClicked?.Invoke());
        pauseButton = Tool(Glyphs.Pause(), "Pause or play", () =>
        {
            PauseClicked?.Invoke();
            Shown();
        });

        var rewind = Tool(Glyphs.Rewind(), "Back to the start", () => RewindClicked?.Invoke());

        knobsButton = Tool(Glyphs.Knob(), "Show or hide the knobs  (Ctrl+K)", () => KnobsClicked?.Invoke());
        knobsButton.IsVisible = false;

        buttons.Children.Add(muteButton);
        buttons.Children.Add(pauseButton);
        buttons.Children.Add(rewind);
        buttons.Children.Add(knobsButton);

        // The theme paints a hovered or pressed button a fill; over a picture that
        // is a grey box, so the glyph brightens instead.
        foreach (var state in new[] { ":pointerover", ":pressed" })
        {
            var lit = new Style(x => x
                .OfType<Button>().Class(state).Not(y => y.Class(":disabled"))
                .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));

            lit.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent));
            lit.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent));
            lit.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, Brushes.White));
            Styles.Add(lit);
        }

        Margin = new Thickness(Inset);
    }

    /// <summary>Raised when the sound button is pressed; the owner flips <see cref="Muted"/>.</summary>
    public event Action? MuteClicked;

    /// <summary>Raised when the pause button is pressed; the owner flips <see cref="Paused"/>.</summary>
    public event Action? PauseClicked;

    /// <summary>Raised when the rewind button is pressed.</summary>
    public event Action? RewindClicked;

    /// <summary>Raised when the knobs button is pressed; the owner flips <see cref="KnobsShown"/>.</summary>
    public event Action? KnobsClicked;

    /// <summary>Whether play is held. The pause button shows what a press does next.</summary>
    public bool Paused
    {
        get => paused;
        set
        {
            if (paused == value) return;

            paused = value;
            pauseButton.Content = Face(paused ? Glyphs.Play() : Glyphs.Pause());
        }
    }

    /// <summary>Whether the sound is turned off.</summary>
    public bool Muted
    {
        get => muted;
        set
        {
            if (muted == value) return;

            muted = value;
            muteButton.Content = Face(muted ? Glyphs.Muted() : Glyphs.Speaker());
        }
    }

    /// <summary>Whether there is sound to turn off.</summary>
    public bool Sounding
    {
        get => muteButton.IsEnabled;
        set => muteButton.IsEnabled = value;
    }

    /// <summary>Whether the patch has knobs, and so a button to show them.</summary>
    public bool HasKnobs
    {
        get => knobsButton.IsVisible;
        set => knobsButton.IsVisible = value;
    }

    /// <summary>Whether the knobs are over the picture. The button is dimmed while they are not.</summary>
    public bool KnobsShown
    {
        get => knobsShown;
        set
        {
            knobsShown = value;
            knobsButton.Opacity = value ? 1 : 0.45;
        }
    }

    /// <summary>The knobs button, for the tests that press it.</summary>
    internal Button KnobsButton => knobsButton;

    /// <summary>A glyph at the knobs' scale, so its strokes weigh what theirs do.</summary>
    private static Viewbox Face(Control glyph) => new() { Width = 22, Height = 22, Child = glyph };

    /// <summary>
    /// A bare glyph that stops the click there: the preview underneath answers a
    /// double-click, and a second press on a button is not one.
    /// </summary>
    private static Button Tool(Control glyph, string tip, Action act)
    {
        var button = new Button
        {
            Content = Face(glyph),
            Width = Size,
            Height = Size,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = new SolidColorBrush(Avalonia.Media.Colors.White, 0.8),
            Effect = Halo(),
        };

        ToolTip.SetTip(button, tip);

        button.Click += (_, e) =>
        {
            act();
            e.Handled = true;
        };

        button.DoubleTapped += (_, e) => e.Handled = true;

        return button;
    }
}
