using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Inspect;

/// <summary>
/// A level, drawn as how full it is rather than as a thumb on a track. Avalonia
/// has no knob or dial — its only "knob" is the puck inside a ToggleSwitch — and
/// a column of Sliders reads as a column of controls, where a column of these
/// reads as the shape of a pattern.
/// </summary>
internal sealed class LevelBar : Control
{
    private static readonly IBrush Track = new SolidColorBrush(Colors.GridMajor);
    private static readonly IBrush Empty = new SolidColorBrush(Colors.Inactive);

    /// <summary>How full it is drawn, the module's own accent rather than a fixed color — see <see cref="StepList"/>.</summary>
    private readonly IBrush fill;

    private double level;

    public LevelBar(IBrush fill)
    {
        this.fill = fill;

        Height = 12;
        MinWidth = 40;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public event Action<double>? ValueChanged;

    public double Value
    {
        get => level;
        set
        {
            var next = Math.Clamp(value, 0d, 1d);
            if (Math.Abs(next - level) < 1e-6) return;

            level = next;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var full = new Rect(Bounds.Size);
        var radius = full.Height / 2;

        context.DrawRectangle(Track, null, new RoundedRect(full, radius));

        if (level <= 0)
        {
            // A rest still shows something, or an empty row looks like a row
            // that failed to draw rather than one deliberately silenced.
            var dot = new Rect(0, 0, full.Height, full.Height).Deflate(3);
            context.DrawEllipse(Empty, null, dot.Center, dot.Width / 2, dot.Height / 2);
            return;
        }

        var width = Math.Max(full.Width * level, full.Height);
        context.DrawRectangle(fill, null, new RoundedRect(full.WithWidth(width), radius));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Track_(e.GetPosition(this).X);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Equals(e.Pointer.Captured, this)) Track_(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void Track_(double x)
    {
        if (Bounds.Width <= 0) return;

        Value = x / Bounds.Width;
        ValueChanged?.Invoke(level);
    }
}