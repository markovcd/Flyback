using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Flyback.Ui.Controls;

/// <summary>
/// A panel knob played as a switch, which a click flips, or as a button, on only
/// while it is held. Space and Enter do the same from the keyboard.
/// </summary>
internal class PanelSwitch : Control, ITurnable
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<PanelSwitch, double>(nameof(Value), coerce: (_, v) => v >= 0.5 ? 1d : 0d);

    public static readonly StyledProperty<bool> LitProperty =
        AvaloniaProperty.Register<PanelSwitch, bool>(nameof(Lit));

    /// <summary>The dots round a button, which all light while it is held.</summary>
    private const int Dots = 20;

    /// <summary>Where a switch's two lamps stand, off and on, in degrees clockwise from the right.</summary>
    private const double OffLamp = 225;

    private const double OnLamp = 315;

    private static readonly IBrush Face = new ImmutableSolidColorBrush(Colors.Node);
    private static readonly IBrush LitFace = new ImmutableSolidColorBrush(Colors.Attention, 0.18);
    private static readonly IBrush Lamp = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IBrush Unlit = new ImmutableSolidColorBrush(Colors.Inactive);
    private static readonly IBrush Halo = new ImmutableSolidColorBrush(Colors.Attention, 0.25);
    private static readonly IPen Glow = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Attention, 0.18), 3);
    private static readonly IPen Pointer = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Label), 2, lineCap: PenLineCap.Round);
    private static readonly IPen OnPointer = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Bright), 2, lineCap: PenLineCap.Round);

    private bool holding;

    static PanelSwitch()
    {
        AffectsRender<PanelSwitch>(ValueProperty, LitProperty);
        FocusableProperty.OverrideDefaultValue<PanelSwitch>(true);
    }

    public PanelSwitch()
    {
        Width = 44;
        Height = 44;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>Whether it is a button, on only while held, rather than a switch.</summary>
    public bool Momentary { get; init; }

    /// <summary>Off at 0 or on at 1; anything else is taken as the nearer.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <inheritdoc />
    public bool Lit
    {
        get => GetValue(LitProperty);
        set => SetValue(LitProperty, value);
    }

    /// <inheritdoc />
    public event Action<double>? Turned;

    /// <inheritdoc />
    public event Action? Released;

    /// <summary>Whether it is on.</summary>
    protected bool IsOn => Value >= 0.5;

    /// <summary>
    /// Lamps like the knob's ring: a button is a ring of dots that all light while it is
    /// held, a switch a short pointer turned to one of two lamps, off and on.
    /// </summary>
    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 3;

        if (Momentary)
        {
            context.DrawEllipse(Lit ? LitFace : Face, null, center, radius - 6, radius - 6);

            if (IsOn) context.DrawEllipse(null, Glow, center, radius - 2, radius - 2);

            for (var i = 0; i < Dots; i++)
            {
                var dot = Along(center, radius, 2 * Math.PI * i / Dots);

                if (IsOn) context.DrawEllipse(Lamp, null, dot, 1.7, 1.7);
                else context.DrawEllipse(Unlit, null, dot, 1, 1);
            }

            return;
        }

        var hub = center + new Vector(0, 3);

        context.DrawEllipse(Lit ? LitFace : Face, null, hub, radius - 8, radius - 8);

        foreach (var (at, on) in new[] { (OffLamp, !IsOn), (OnLamp, IsOn) })
        {
            var lamp = Along(center, radius - 1, Radians(at));

            if (!on)
            {
                context.DrawEllipse(Unlit, null, lamp, 1.2, 1.2);
                continue;
            }

            context.DrawEllipse(Halo, null, lamp, 4, 4);
            context.DrawEllipse(Lamp, null, lamp, 2, 2);
        }

        var angle = Radians(IsOn ? OnLamp : OffLamp);

        context.DrawLine(IsOn ? OnPointer : Pointer, Along(hub, 2, angle), Along(hub, radius - 11, angle));
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static Point Along(Point center, double distance, double angle) =>
        new(center.X + distance * Math.Cos(angle), center.Y + distance * Math.Sin(angle));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        e.Handled = true;
        Press();

        if (Momentary) e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!holding) return;

        e.Pointer.Capture(null);
        Lift();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        Lift();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is not (Key.Space or Key.Enter) || holding) return;

        e.Handled = true;
        Press();
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.Key is not (Key.Space or Key.Enter)) return;

        e.Handled = true;
        Lift();
    }

    /// <summary>A switch flips and lets go at once; a button goes on until <see cref="Lift"/>.</summary>
    private void Press()
    {
        if (!Momentary)
        {
            Set(IsOn ? 0 : 1);
            Released?.Invoke();
            return;
        }

        holding = true;
        Set(1);
    }

    private void Lift()
    {
        if (!holding) return;

        holding = false;
        Set(0);
        Released?.Invoke();
    }

    private void Set(double to)
    {
        var before = Value;

        Value = to;

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (Value != before) Turned?.Invoke(Value);
    }
}
