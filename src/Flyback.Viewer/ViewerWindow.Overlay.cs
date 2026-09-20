using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Controls;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.Viewer;

/// <summary>The dots, the proximity curve and the toolbar behind them.</summary>
internal sealed partial class ViewerWindow
{
    /// <summary>Closer than this and the dots are fully there.</summary>
    private const double Near = 40;

    /// <summary>Farther than this and they are at their faintest.</summary>
    private const double Far = 260;

    /// <summary>Faint but never absent, so the way to the toolbar can be found.</summary>
    private const double Floor = 0.06;

    /// <summary>How long the toolbar stays once the pointer has left it.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(900);

    private Control dots = null!;
    private Control toolbar = null!;
    private Border overlay = null!;
    private Button muteButton = null!;
    private Button pauseButton = null!;
    private readonly DispatcherTimer tuck = new() { Interval = Grace };

    /// <summary>
    /// How solid the dots are for a pointer <paramref name="distance"/> from them.
    /// Squared for the reason the audition fade is: a straight line reads as a
    /// jump at the quiet end.
    /// </summary>
    internal static double Proximity(double distance)
    {
        var t = Math.Clamp((Far - distance) / (Far - Near), 0, 1);

        return Floor + (1 - Floor) * t * t;
    }

    /// <summary>The dots, for the tests that steer a pointer at them.</summary>
    internal Control Dots => dots;

    /// <summary>How solid the dots are now.</summary>
    internal double DotsOpacity => dots.Opacity;

    /// <summary>Whether the toolbar is showing.</summary>
    internal bool ToolbarShown => toolbar.Opacity > 0;

    /// <summary>
    /// Bottom right, and one control: the dots and the toolbar in a single panel, so
    /// the pointer never leaves the one region on its way from the first to the
    /// second, which a flyout would get wrong.
    /// </summary>
    private Control BuildOverlay(ViewerOptions options)
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

        muteButton = Tool(Glyphs.Speaker(), "Sound on or off", () => Sounded(!player.Muted));
        pauseButton = Tool(Glyphs.Pause(), "Pause or play", () =>
        {
            player.Toggle();
            PauseChanged();
            Shown();
        });

        var rewind = Tool(Glyphs.Rewind(), "Back to the start", player.Rewind);

        muteButton.IsEnabled = player.Sounding;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        buttons.Children.Add(muteButton);
        buttons.Children.Add(pauseButton);
        buttons.Children.Add(rewind);

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

        overlay = new Border
        {
            Child = row,
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
        };

        Sounded(player.Muted);
        PauseChanged();

        // Tunnelled at the window, so the dots know how near the pointer is however
        // far from them it is, and whatever else has taken the event.
        AddHandler(PointerMovedEvent, Approached, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerExited += (_, _) => dots.Opacity = Floor;

        dots.PointerEntered += (_, _) => Shown();
        overlay.PointerEntered += (_, _) => tuck.Stop();
        overlay.PointerExited += (_, _) => tuck.Start();

        tuck.Tick += (_, _) =>
        {
            tuck.Stop();
            Hidden();
        };

        return overlay;
    }

    private void Approached(object? sender, PointerEventArgs e)
    {
        if (dots is null) return;

        var at = e.GetPosition(dots);
        var centre = new Point(dots.Bounds.Width / 2, dots.Bounds.Height / 2);

        dots.Opacity = Proximity(Math.Sqrt(Math.Pow(at.X - centre.X, 2) + Math.Pow(at.Y - centre.Y, 2)));
    }

    private void Shown()
    {
        tuck.Stop();
        toolbar.Opacity = 1;
        toolbar.IsHitTestVisible = true;
    }

    private void Hidden()
    {
        toolbar.Opacity = 0;
        toolbar.IsHitTestVisible = false;
    }

    private void Sounded(bool muted)
    {
        player.Mute(muted);
        muteButton.Content = muted ? Glyphs.Muted() : Glyphs.Speaker();
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

    /// <summary>Switches the pause button's glyph to say what a press does next.</summary>
    private void PauseChanged() =>
        pauseButton.Content = player.Paused ? Glyphs.Play() : Glyphs.Pause();
}
