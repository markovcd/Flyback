using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// How far through its length the patch is, as a line the width of the window. Drawn
/// only: it stands in for the transport row while that is hidden, and takes no press.
/// </summary>
internal sealed class PlayheadLine : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<PlayheadLine, double>(nameof(Value));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<PlayheadLine, double>(nameof(Maximum), Patch.DefaultLength);

    private static readonly IBrush Track = new ImmutableSolidColorBrush(Colors.Separator);
    private static readonly IBrush Travel = new ImmutableSolidColorBrush(Colors.Attention);

    static PlayheadLine()
    {
        AffectsRender<PlayheadLine>(ValueProperty, MaximumProperty);
    }

    public PlayheadLine()
    {
        Height = 2;
        IsHitTestVisible = false;
    }

    /// <summary>Where the clock is, in seconds.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>How many seconds the line spans.</summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var along = Maximum > 0 ? Math.Clamp(Value / Maximum, 0, 1) : 0;

        context.FillRectangle(Track, new Rect(Bounds.Size));
        context.FillRectangle(Travel, new Rect(0, 0, Bounds.Width * along, Bounds.Height));
    }
}
