using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
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

    /// <summary>Set from the dots opening until the pointer leaves where they were.</summary>
    private bool arriving;

    /// <summary>How big a bare glyph button over the picture is, either way.</summary>
    protected const double ToolSize = 40;

    /// <param name="contents">What the dots open to, in their place.</param>
    /// <param name="side">Where along the edge both sit.</param>
    /// <param name="edge">Which edge, the bottom or the top.</param>
    protected TuckedAway(Control contents, HorizontalAlignment side, VerticalAlignment edge = VerticalAlignment.Bottom)
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
            Content = new Viewbox { Width = 24, Height = 24, Child = Glyphs.Dots() },
        };

        contents.HorizontalAlignment = side;
        contents.Opacity = 0;
        contents.IsHitTestVisible = false;
        contents.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) } };

        var layout = new Panel();

        layout.Children.Add(contents);
        layout.Children.Add(dots);

        Child = layout;
        HorizontalAlignment = side;
        Edge = edge;

        dots.PointerEntered += (_, _) =>
        {
            Shown();
            arriving = GuardsArrival;
        };

        AddHandler(PointerMovedEvent, (_, e) => arriving &= OnDots(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (arriving && OnDots(e)) e.Handled = true;
        }, RoutingStrategies.Tunnel);

        PointerEntered += (_, _) => tuck.Stop();
        PointerExited += (_, _) => { if (!held && !IsPinned) tuck.Start(); };

        // A knob dragged past the edge keeps its place until it is let go.
        AddHandler(PointerPressedEvent, (_, _) => held = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => Released(), RoutingStrategies.Tunnel, handledEventsToo: true);

        tuck.Tick += (_, _) =>
        {
            tuck.Stop();
            Hidden();
        };

        // The theme paints a hovered or pressed button a fill; over a picture that
        // is a gray box, so the glyph brightens instead.
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

        // Nor a gray box for one that cannot be pressed: the glyph dims instead.
        var off = new Style(x => x
            .OfType<Button>().Class(":disabled")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));

        off.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent));
        off.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent));
        off.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, new SolidColorBrush(Avalonia.Media.Colors.White, 0.3)));
        Styles.Add(off);

        // Put away with the control, so it comes back the way it started.
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty || IsVisible) return;

            tuck.Stop();
            Hidden();
            dots.Opacity = Floor;
        };
    }

    /// <summary>
    /// Whether a click aimed at the dots is taken for nothing until the pointer has moved
    /// off where they were, for contents where a click alone does something.
    /// </summary>
    protected bool GuardsArrival { get; init; }

    /// <summary>Which edge of the picture the dots and what they open to stand at, the top or the bottom.</summary>
    public VerticalAlignment Edge
    {
        get => VerticalAlignment;
        set
        {
            VerticalAlignment = value;
            dots.VerticalAlignment = value;
            contents.VerticalAlignment = value;
        }
    }

    /// <summary>The dots, for the tests that steer a pointer at them.</summary>
    internal Control Dots => dots;

    /// <summary>How solid the dots are now.</summary>
    internal double DotsOpacity => dots.Opacity;

    /// <summary>Whether the contents are showing.</summary>
    internal bool IsOpen => contents.IsHitTestVisible;

    /// <summary>Whether the contents are out for good, with no dots.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>Opens the contents for good and drops the dots, for a window with no picture to keep clear of.</summary>
    public void Pin()
    {
        IsPinned = true;
        Shown();
        dots.IsVisible = false;
        contents.Transitions = null;
        contents.Opacity = 1;
    }

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

    /// <summary>A glyph at the knobs' scale, so its strokes weigh what theirs do.</summary>
    protected static Viewbox Face(Control glyph) => new() { Width = 22, Height = 22, Child = glyph };

    /// <summary>
    /// A bare glyph that stops the click there: the preview underneath answers a
    /// double-click, and a second press on a button is not one.
    /// </summary>
    protected static Button Tool(Control glyph, string tip, Action act)
    {
        var button = new Button
        {
            Content = Face(glyph),
            Width = ToolSize,
            Height = ToolSize,
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

    private bool OnDots(PointerEventArgs e) => new Rect(dots.Bounds.Size).Contains(e.GetPosition(dots));

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
        if (!IsPointerOver && !IsPinned) tuck.Start();
    }

    private void Hidden()
    {
        if (IsPinned) return;

        contents.Opacity = 0;
        contents.IsHitTestVisible = false;
        dots.Opacity = Floor;
        dots.IsHitTestVisible = true;
        Background = null;
        arriving = false;
    }
}
