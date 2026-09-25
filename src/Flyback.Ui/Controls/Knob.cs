using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>
/// A rotary knob from 0 to 1, turned by dragging up and down. Shift turns it finely,
/// the wheel steps it, and a double-click puts it back to the middle. The pointer is held still and
/// hidden while it turns, so the edge of the screen never stops a turn.
/// </summary>
internal class Knob : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Knob, double>(nameof(Value), 0.5, coerce: (_, v) => Math.Clamp(v, 0d, 1d));

    public static readonly StyledProperty<bool> LitProperty =
        AvaloniaProperty.Register<Knob, bool>(nameof(Lit));

    /// <summary>How far a drag has to travel to turn the knob end to end.</summary>
    private const double Travel = 160;

    /// <summary>Where the sweep starts and how far it goes, in degrees clockwise from the right.</summary>
    protected const double Start = 135;

    protected const double Sweep = 270;

    private static readonly IPen Track = new Pen(new SolidColorBrush(Colors.Separator), 3.5, lineCap: PenLineCap.Round);
    private static readonly IPen Arc = new Pen(new SolidColorBrush(Colors.Attention), 3.5, lineCap: PenLineCap.Round);
    private static readonly IPen Pointer = new Pen(new SolidColorBrush(Colors.Label), 2, lineCap: PenLineCap.Round);
    private static readonly IBrush Face = new SolidColorBrush(Colors.Node);
    private static readonly IBrush LitFace = new SolidColorBrush(Colors.Attention, 0.18);

    private static readonly Cursor Upright = new(StandardCursorType.SizeNorthSouth);
    private static readonly Cursor Hidden = new(StandardCursorType.None);

    private Point? grabbed;
    private Point last;
    private IPointerAnchor? anchor;

    static Knob()
    {
        AffectsRender<Knob>(ValueProperty, LitProperty);
        FocusableProperty.OverrideDefaultValue<Knob>(true);
    }

    public Knob()
    {
        Width = 44;
        Height = 44;
        Cursor = Upright;
    }

    /// <summary>Holds the pointer where a turn begins. Null leaves the pointer free.</summary>
    internal IPointerAnchors Anchors { get; set; } = PlatformAnchors.Instance;

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Whether the face is tinted, for the knob whose sockets are being linked.</summary>
    public bool Lit
    {
        get => GetValue(LitProperty);
        set => SetValue(LitProperty, value);
    }

    /// <summary>A hand turned it. Not raised when <see cref="Value"/> is set from code.</summary>
    public event Action<double>? Turned;

    /// <summary>The hand came off after turning it.</summary>
    public event Action? Released;

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = size / 2 - 3;

        context.DrawEllipse(Lit ? LitFace : Face, null, center, radius - 5, radius - 5);
        context.DrawGeometry(null, Track, ArcGeometry(center, radius, Start, Sweep));

        if (Value > 0.001) context.DrawGeometry(null, Arc, ArcGeometry(center, radius, Start, Sweep * Value));

        var angle = Radians(Start + Sweep * Value);
        context.DrawLine(
            Pointer,
            Along(center, radius * 0.25, angle),
            Along(center, radius - 6, angle));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            Turn(0.5);
            Released?.Invoke();
            e.Handled = true;
            return;
        }

        grabbed = last = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;

        anchor = Anchors.Take(this);
        if (anchor is not null) Cursor = Hidden;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (grabbed is not { } home) return;

        var at = e.GetPosition(this);

        // The warp's own echo, which would otherwise warp again.
        if (anchor is not null && at == home) return;

        var fine = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 5d : 1d;

        Turn(Value + (last.Y - at.Y) / (Travel * fine));

        if (anchor?.Return() == true)
        {
            last = home;
        }
        else
        {
            last = at;
            LetGo();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (grabbed is null) return;

        grabbed = null;
        LetGo();
        e.Pointer.Capture(null);
        Released?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (grabbed is null) return;

        grabbed = null;
        LetGo();
        Released?.Invoke();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 0.005 : 0.02;

        Turn(Value + Math.Sign(e.Delta.Y) * step);
        Released?.Invoke();
        e.Handled = true;
    }

    private void LetGo()
    {
        anchor?.Dispose();
        anchor = null;
        Cursor = Upright;
    }

    private void Turn(double to)
    {
        var before = Value;

        Value = to;

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (Value != before) Turned?.Invoke(Value);
    }

    protected static Geometry ArcGeometry(Point center, double radius, double from, double sweep)
    {
        var geometry = new StreamGeometry();

        using var sink = geometry.Open();

        sink.BeginFigure(Along(center, radius, Radians(from)), false);
        sink.ArcTo(
            Along(center, radius, Radians(from + sweep)),
            new Size(radius, radius),
            0,
            sweep > 180,
            SweepDirection.Clockwise);
        sink.EndFigure(false);

        return geometry;
    }

    protected static double Radians(double degrees) => degrees * Math.PI / 180;

    protected static Point Along(Point center, double distance, double angle) =>
        new(center.X + distance * Math.Cos(angle), center.Y + distance * Math.Sin(angle));
}
