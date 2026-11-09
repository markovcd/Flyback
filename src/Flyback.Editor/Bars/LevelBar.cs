using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Bars;

/// <summary>
/// How close the sound comes to clipping: a short bar of its peak with a tick where it peaked
/// lately, and a dot that lights on a clip and stays lit until cleared.
/// </summary>
internal sealed class LevelBar : Control
{
    private const double BarWidth = 44;
    private const double BarHeight = 6;
    private const double Gap = 8;
    private const double Dot = 6;

    /// <summary>How long the tick holds the loudest peak before it falls to the level.</summary>
    public static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(1.5);

    private static readonly IBrush Track = new ImmutableSolidColorBrush(Colors.Toolbar);
    private static readonly IBrush Rest = new ImmutableSolidColorBrush(Colors.Muted);
    private static readonly IBrush Hot = new ImmutableSolidColorBrush(Colors.Warning);
    private static readonly IBrush Fault = new ImmutableSolidColorBrush(Colors.Fault);
    private static readonly IBrush Tick = new ImmutableSolidColorBrush(Colors.Label);
    private static readonly IBrush DotOff = new ImmutableSolidColorBrush(Colors.Separator);

    private double level = double.NegativeInfinity;
    private double held = double.NegativeInfinity;
    private DateTime heldAt;

    public LevelBar()
    {
        Width = BarWidth + Gap + Dot;
        Height = Dot;
    }

    /// <summary>The level, in dB, now.</summary>
    public double Level => level;

    /// <summary>The loudest level of the last <see cref="HoldFor"/>, in dB.</summary>
    public double Held => held;

    /// <summary>Whether a clip has happened since it was last cleared.</summary>
    public bool ClipLit { get; private set; }

    /// <summary>Shows <paramref name="decibels"/> as the level at <paramref name="now"/>, and whether a clip is waiting to be cleared.</summary>
    public void Show(double decibels, bool clipLit, DateTime now)
    {
        if (decibels >= held || now - heldAt > HoldFor)
        {
            held = decibels;
            heldAt = now;
        }

        if (decibels == level && clipLit == ClipLit) return;

        level = decibels;
        ClipLit = clipLit;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var top = (Bounds.Height - BarHeight) / 2;

        context.FillRectangle(Track, new Rect(0, top, BarWidth, BarHeight));

        var reach = SoundLevel.Reach(level) * BarWidth;
        var ink = level >= 0 ? Fault : SoundLevel.Hot(level) ? Hot : Rest;
        if (reach > 0) context.FillRectangle(ink, new Rect(0, top, reach, BarHeight));

        if (SoundLevel.Reach(held) > 0)
        {
            var at = Math.Min(SoundLevel.Reach(held) * BarWidth, BarWidth - 1);
            context.FillRectangle(held >= 0 ? Fault : Tick, new Rect(at, top, 1, BarHeight));
        }

        var dot = new Rect(BarWidth + Gap, (Bounds.Height - Dot) / 2, Dot, Dot);
        context.DrawEllipse(ClipLit ? Fault : DotOff, null, dot);
    }
}
