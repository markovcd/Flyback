using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// A strip from nought to <see cref="Maximum"/> seconds with a thumb at <see cref="Value"/>,
/// pressed or dragged anywhere along it to say where the clock should be. Drawn the way
/// the knobs are: in the toolbar's colors, or over a picture as light strokes on dark ones.
/// </summary>
internal sealed class SeekTrack : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SeekTrack, double>(nameof(Value));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SeekTrack, double>(nameof(Maximum), Patch.DefaultLength);

    /// <summary>How far in from either end the thumb's middle stops.</summary>
    private const double Inset = 7;

    private const double Thumb = 5;

    private static readonly IPen BarTrack = new Pen(new SolidColorBrush(Colors.Separator), 3.5, lineCap: PenLineCap.Round);
    private static readonly IPen BarTravel = new Pen(new SolidColorBrush(Colors.Attention), 3.5, lineCap: PenLineCap.Round);
    private static readonly IBrush BarThumb = new SolidColorBrush(Colors.Label);

    private static readonly IPen StageTrack = new Pen(new SolidColorBrush(Avalonia.Media.Colors.White, 0.45), 2, lineCap: PenLineCap.Round);
    private static readonly IPen StageTrackUnder = new Pen(new SolidColorBrush(Avalonia.Media.Colors.Black, 0.4), 5, lineCap: PenLineCap.Round);
    private static readonly IPen StageTravel = new Pen(Brushes.White, 2.5, lineCap: PenLineCap.Round);
    private static readonly IPen StageTravelUnder = new Pen(new SolidColorBrush(Avalonia.Media.Colors.Black, 0.65), 5.5, lineCap: PenLineCap.Round);
    private static readonly IBrush StageThumbUnder = new SolidColorBrush(Avalonia.Media.Colors.Black, 0.65);

    private readonly bool stage;

    static SeekTrack()
    {
        AffectsRender<SeekTrack>(ValueProperty, MaximumProperty);
    }

    /// <param name="stage">Drawn to stand over a picture rather than on the toolbar.</param>
    public SeekTrack(bool stage = false)
    {
        this.stage = stage;

        Height = stage ? 40 : 30;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>Where the thumb is, in seconds. Setting it moves the thumb and says nothing.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>How many seconds the strip spans.</summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Whether a hand has the thumb, so the clock should not move it.</summary>
    public bool Held { get; private set; }

    /// <summary>A hand put the thumb at this many seconds.</summary>
    public event Action<double>? Sought;

    /// <summary>
    /// Where along the strip <paramref name="seconds"/> falls, for a hand or a test
    /// aiming at it.
    /// </summary>
    public Point At(double seconds)
    {
        var along = Maximum > 0 ? Math.Clamp(seconds / Maximum, 0, 1) : 0;

        return new Point(Inset + Math.Max(0, Bounds.Width - 2 * Inset) * along, Bounds.Height / 2);
    }

    public override void Render(DrawingContext context)
    {
        // Filled with nothing, so a press anywhere on the strip lands on it.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var from = At(0);
        var to = At(Maximum);
        var at = At(Value);

        if (stage)
        {
            // Every dark stroke before any light one, so no halo cuts across a line.
            context.DrawLine(StageTrackUnder, from, to);
            context.DrawLine(StageTravelUnder, from, at);
            context.DrawEllipse(StageThumbUnder, null, at, Thumb + 1.5, Thumb + 1.5);

            context.DrawLine(StageTrack, from, to);
            context.DrawLine(StageTravel, from, at);
            context.DrawEllipse(Brushes.White, null, at, Thumb, Thumb);

            return;
        }

        context.DrawLine(BarTrack, from, to);
        context.DrawLine(BarTravel, from, at);
        context.DrawEllipse(BarThumb, null, at, Thumb, Thumb);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        Held = true;
        e.Pointer.Capture(this);
        Seek(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (Held) Seek(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!Held) return;

        Held = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        Held = false;
    }

    private void Seek(Point to)
    {
        var width = Bounds.Width - 2 * Inset;
        var seconds = width > 0 ? Math.Clamp((to.X - Inset) / width, 0, 1) * Maximum : 0;

        Value = seconds;
        Sought?.Invoke(seconds);
    }
}
