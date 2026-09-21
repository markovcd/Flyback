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
/// Something over a full-window picture that keeps out of its way: three dots that
/// solidify as the pointer nears them, and give way to the thing itself when reached.
/// </summary>
/// <remarks>
/// Dots and contents are one control, so the pointer never leaves the region on its
/// way from the first to the second, which a flyout would get wrong.
/// </remarks>
public abstract class TuckedAway : Border
{
    /// <summary>Closer than this and the dots are fully there.</summary>
    private const double Near = 40;

    /// <summary>Farther than this and they are at their faintest.</summary>
    private const double Far = 260;

    /// <summary>Faint but never absent, so the way in can be found.</summary>
    private const double Floor = 0.06;

    /// <summary>How long the contents stay once the pointer has left them.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(900);

    /// <summary>A dark rim under the light strokes, so they read on a white frame and a black one alike.</summary>
    protected static DropShadowEffect Halo() => new() { OffsetX = 0, OffsetY = 0, BlurRadius = 3, Color = Avalonia.Media.Colors.Black, Opacity = 0.9 };

    private readonly Control dots;
    private readonly Control contents;
    private readonly DispatcherTimer tuck = new() { Interval = Grace };

    private TopLevel? host;
    private bool held;

    /// <param name="contents">What the dots open to, in their place.</param>
    /// <param name="side">Where along the bottom both sit.</param>
    protected TuckedAway(Control contents, HorizontalAlignment side)
    {
        this.contents = contents;

        dots = new ContentControl
        {
            Width = 40,
            Height = 36,
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            Opacity = Floor,
            Effect = Halo(),
            HorizontalAlignment = side,
            VerticalAlignment = VerticalAlignment.Bottom,
            Content = new Viewbox { Width = 24, Height = 24, Child = Glyphs.Dots() },
        };

        contents.HorizontalAlignment = side;
        contents.VerticalAlignment = VerticalAlignment.Bottom;
        contents.Opacity = 0;
        contents.IsHitTestVisible = false;
        contents.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) } };

        var layout = new Panel();

        layout.Children.Add(contents);
        layout.Children.Add(dots);

        Child = layout;
        HorizontalAlignment = side;
        VerticalAlignment = VerticalAlignment.Bottom;

        dots.PointerEntered += (_, _) => Shown();
        PointerEntered += (_, _) => tuck.Stop();
        PointerExited += (_, _) => { if (!held) tuck.Start(); };

        // A knob dragged past the edge keeps its place until it is let go.
        AddHandler(PointerPressedEvent, (_, _) => held = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => Released(), RoutingStrategies.Tunnel, handledEventsToo: true);

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

    /// <summary>The dots, for the tests that steer a pointer at them.</summary>
    internal Control Dots => dots;

    /// <summary>How solid the dots are now.</summary>
    internal double DotsOpacity => dots.Opacity;

    /// <summary>Whether the contents are showing.</summary>
    internal bool IsOpen => contents.IsHitTestVisible;

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

    /// <summary>Opens the contents and holds them open while the pointer is over them.</summary>
    protected void Shown()
    {
        tuck.Stop();
        contents.Opacity = 1;
        contents.IsHitTestVisible = true;
        dots.Opacity = 0;
        dots.IsHitTestVisible = false;

        // Solid only while open, so the gaps between the contents hold the pointer,
        // and the tucked-away contents' place takes no clicks from the picture.
        Background = Brushes.Transparent;
    }

    // Tunnelled at the window, so the dots know how near the pointer is however
    // far from them it is, and whatever else has taken the event.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        host = TopLevel.GetTopLevel(this);
        host?.AddHandler(PointerMovedEvent, Approached, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (host is not null) host.PointerExited += Left;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        host?.RemoveHandler(PointerMovedEvent, Approached);
        if (host is not null) host.PointerExited -= Left;
        host = null;
        tuck.Stop();
    }

    private void Left(object? sender, PointerEventArgs e) => dots.Opacity = Floor;

    private void Approached(object? sender, PointerEventArgs e)
    {
        if (!IsVisible || IsOpen) return;

        var at = e.GetPosition(dots);
        var middle = new Point(dots.Bounds.Width / 2, dots.Bounds.Height / 2);

        dots.Opacity = Proximity(Math.Sqrt(Math.Pow(at.X - middle.X, 2) + Math.Pow(at.Y - middle.Y, 2)));
    }

    private void Released()
    {
        if (!held) return;

        held = false;
        if (!IsPointerOver) tuck.Start();
    }

    private void Hidden()
    {
        contents.Opacity = 0;
        contents.IsHitTestVisible = false;
        dots.Opacity = Floor;
        dots.IsHitTestVisible = true;
        Background = null;
    }
}
