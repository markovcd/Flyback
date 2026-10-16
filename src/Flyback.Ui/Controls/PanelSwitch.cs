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

    private static readonly IBrush Off = new ImmutableSolidColorBrush(Colors.Node);
    private static readonly IBrush On = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IBrush LitFace = new ImmutableSolidColorBrush(Colors.Attention, 0.18);
    private static readonly IBrush Thumb = new ImmutableSolidColorBrush(Colors.Label);
    private static readonly IPen Edge = new ImmutablePen(new ImmutableSolidColorBrush(Colors.Separator), 2);

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

    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

        if (Momentary)
        {
            var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 6;
            context.DrawEllipse(IsOn ? On : Lit ? LitFace : Off, Edge, center, radius, radius);
            return;
        }

        var track = new Rect(center.X - 16, center.Y - 9, 32, 18);
        var thumb = new Point(IsOn ? track.Right - 9 : track.Left + 9, center.Y);

        context.DrawRectangle(IsOn ? On : Lit ? LitFace : Off, Edge, track, 9, 9);
        context.DrawEllipse(Thumb, null, thumb, 6, 6);
    }

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
