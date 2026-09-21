using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Flyback.App.Controls;

/// <summary>
/// Sound, pause, rewind and the knobs over a full-window picture: three dots bottom right that
/// solidify as the pointer nears them, and a toolbar that opens from them.
/// </summary>
/// <remarks>
/// Dots and toolbar are one control, so the pointer never leaves the region on its way
/// from the first to the second, which a flyout would get wrong. It holds no state of
/// its own: the owner sets <see cref="Paused"/>, <see cref="Muted"/> and
/// <see cref="Sounding"/>, and hears the three clicks.
/// </remarks>
public sealed class TransportOverlay : Border
{
    /// <summary>Closer than this and the dots are fully there.</summary>
    private const double Near = 40;

    /// <summary>Farther than this and they are at their faintest.</summary>
    private const double Far = 260;

    /// <summary>Faint but never absent, so the way to the toolbar can be found.</summary>
    private const double Floor = 0.06;

    /// <summary>How long the toolbar stays once the pointer has left it.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(900);

    private readonly Control dots;
    private readonly Control toolbar;
    private readonly Button muteButton;
    private readonly Button pauseButton;
    private readonly Button knobsButton;
    private readonly DispatcherTimer tuck = new() { Interval = Grace };

    private bool paused;
    private bool muted;
    private bool knobsShown = true;

    /// <param name="host">The window whose pointer the dots follow, wherever in it that is.</param>
    public TransportOverlay(TopLevel host)
    {
        dots = new ContentControl
        {
            Width = 40,
            Height = 36,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            Opacity = Floor,
            Content = new Viewbox { Width = 24, Height = 24, Child = Glyphs.Dots() },
        };

        muteButton = Tool(Glyphs.Speaker(), "Sound on or off", () => MuteClicked?.Invoke());
        pauseButton = Tool(Glyphs.Pause(), "Pause or play", () =>
        {
            PauseClicked?.Invoke();
            Shown();
        });

        var rewind = Tool(Glyphs.Rewind(), "Back to the start", () => RewindClicked?.Invoke());

        knobsButton = Tool(Glyphs.Knob(), "Show or hide the knobs  (Ctrl+K)", () => KnobsClicked?.Invoke());
        knobsButton.IsVisible = false;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        buttons.Children.Add(muteButton);
        buttons.Children.Add(pauseButton);
        buttons.Children.Add(rewind);
        buttons.Children.Add(knobsButton);

        toolbar = new Border
        {
            Background = new SolidColorBrush(Colors.Toolbar, 0.88),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Opacity = 0,
            IsHitTestVisible = false,
            Child = buttons,
            Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) } },
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
        };

        row.Children.Add(toolbar);
        row.Children.Add(dots);

        Child = row;
        Margin = new Thickness(12);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;

        // Tunnelled at the window, so the dots know how near the pointer is however
        // far from them it is, and whatever else has taken the event.
        host.AddHandler(PointerMovedEvent, Approached, RoutingStrategies.Tunnel, handledEventsToo: true);
        host.PointerExited += (_, _) => dots.Opacity = Floor;

        dots.PointerEntered += (_, _) => Shown();
        PointerEntered += (_, _) => tuck.Stop();
        PointerExited += (_, _) => tuck.Start();

        tuck.Tick += (_, _) =>
        {
            tuck.Stop();
            Hidden();
        };

        // Put away with the control, so it comes back the way it started.
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty || IsVisible) return;

            tuck.Stop();
            Hidden();
            dots.Opacity = Floor;
        };
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
            pauseButton.Content = paused ? Glyphs.Play() : Glyphs.Pause();
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
            muteButton.Content = muted ? Glyphs.Muted() : Glyphs.Speaker();
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

    /// <summary>The dots, for the tests that steer a pointer at them.</summary>
    internal Control Dots => dots;

    /// <summary>How solid the dots are now.</summary>
    internal double DotsOpacity => dots.Opacity;

    /// <summary>Whether the toolbar is showing.</summary>
    internal bool ToolbarShown => toolbar.Opacity > 0;

    /// <summary>
    /// How solid the dots are for a pointer <paramref name="distance"/> from them.
    /// Squared for the reason the audition fade is: a straight line reads as a
    /// jump at the quiet end.
    /// </summary>
    public static double Proximity(double distance)
    {
        var t = Math.Clamp((Far - distance) / (Far - Near), 0, 1);

        return Floor + (1 - Floor) * t * t;
    }

    private void Approached(object? sender, PointerEventArgs e)
    {
        if (!IsVisible) return;

        var at = e.GetPosition(dots);
        var middle = new Point(dots.Bounds.Width / 2, dots.Bounds.Height / 2);

        dots.Opacity = Proximity(Math.Sqrt(Math.Pow(at.X - middle.X, 2) + Math.Pow(at.Y - middle.Y, 2)));
    }

    private void Shown()
    {
        tuck.Stop();
        toolbar.Opacity = 1;
        toolbar.IsHitTestVisible = true;

        // Solid only while open, so the gap between toolbar and dots holds the
        // pointer, and the tucked-away toolbar's place takes no clicks from the knobs.
        Background = Brushes.Transparent;
    }

    private void Hidden()
    {
        toolbar.Opacity = 0;
        toolbar.IsHitTestVisible = false;
        Background = null;
    }

    /// <summary>
    /// A toolbar button that stops the click there: the preview underneath answers a
    /// double-click, and a second press on a button is not one.
    /// </summary>
    private static Button Tool(Control glyph, string tip, Action act)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 34,
            Height = 30,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Colors.Label),
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
