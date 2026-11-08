using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Bars;

/// <summary>Five rising bars, like a phone's signal: the more lit, the further ahead of real time the sound is.</summary>
internal sealed class HeadroomRungs : Control
{
    private const double RungWidth = 3;
    private const double Gap = 2;
    private const double Lowest = 5;
    private const double Rise = 2;

    private static readonly IBrush Rest = new ImmutableSolidColorBrush(Colors.Label);
    private static readonly IBrush Unlit = new ImmutableSolidColorBrush(Colors.Separator);
    private static readonly IBrush Tight = new ImmutableSolidColorBrush(Colors.Warning);
    private static readonly IBrush Behind = new ImmutableSolidColorBrush(Colors.Fault);
    private static readonly IBrush Stepped = new ImmutableSolidColorBrush(Colors.Attention);

    private int lit;
    private Headroom headroom;
    private bool stepped;

    public HeadroomRungs()
    {
        var count = SoundHeadroom.Rungs.Length;
        Width = count * RungWidth + (count - 1) * Gap;
        Height = Lowest + (count - 1) * Rise;
    }

    /// <summary>How many rungs are lit and in what color, redrawn only when either changed.</summary>
    /// <param name="stepped">Whether the oversampling stepped down a moment ago.</param>
    public void Show(int lit, Headroom headroom, bool stepped)
    {
        if (lit == this.lit && headroom == this.headroom && stepped == this.stepped) return;

        (this.lit, this.headroom, this.stepped) = (lit, headroom, stepped);
        InvalidateVisual();
    }

    /// <summary>How many rungs are lit, for a test to read what is drawn.</summary>
    public int Lit => lit;

    public override void Render(DrawingContext context)
    {
        var on = headroom switch
        {
            Headroom.Behind => Behind,
            Headroom.Tight => Tight,
            _ when stepped => Stepped,
            _ => Rest,
        };

        // Behind lights the lowest rung red, so the bar never reads as simply empty.
        var shown = headroom == Headroom.Behind ? 1 : lit;

        for (var i = 0; i < SoundHeadroom.Rungs.Length; i++)
        {
            var height = Lowest + i * Rise;
            context.FillRectangle(i < shown ? on : Unlit, new Rect(i * (RungWidth + Gap), Bounds.Height - height, RungWidth, height));
        }
    }
}
