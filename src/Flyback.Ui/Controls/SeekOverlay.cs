using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// The seek bar over a full-window picture, tucked behind three dots top center: the
/// strip, the loop switch and the time against the patch's length.
/// </summary>
/// <remarks>
/// Holds no state of its own: the owner sets where the clock is with <see cref="Follow"/>
/// and hears a hand move it through <see cref="Sought"/>.
/// </remarks>
public sealed class SeekOverlay : TuckedAway
{
    private const double Inset = 12;

    private static readonly IBrush Lit = new SolidColorBrush(Avalonia.Media.Colors.White, 0.8);
    private static readonly IBrush Unlit = new SolidColorBrush(Avalonia.Media.Colors.White, 0.35);

    /// <summary>How wide the strip is over a picture with room for it.</summary>
    private const double Wide = 360;

    /// <summary>What the loop switch and the time beside the strip take.</summary>
    private const double Beside = 190;

    private readonly SeekTrack track;
    private readonly Button loop;
    private readonly TextBlock time;

    private bool looped;

    public SeekOverlay()
        : this(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 })
    {
    }

    private SeekOverlay(StackPanel row)
        : base(row, HorizontalAlignment.Center, VerticalAlignment.Top)
    {
        track = new SeekTrack(stage: true) { Width = Wide };

        loop = Tool(Glyphs.Loop(), "Play the patch's length round and round", () => LoopClicked?.Invoke());
        loop.Foreground = Unlit;

        time = new TextBlock
        {
            Name = "seekTime",
            FontSize = Text.Body,
            Foreground = Lit,
            Effect = Halo(),
            VerticalAlignment = VerticalAlignment.Center,
            Text = Said(0, Patch.DefaultLength),
        };

        track.Sought += seconds => Sought?.Invoke(seconds);
        track.DoubleTapped += (_, e) => e.Handled = true;

        ToolTip.SetTip(track, "Drag to move the patch's clock, in the picture and in the sound.");

        row.Children.Add(loop);
        row.Children.Add(track);
        row.Children.Add(time);

        Margin = new Thickness(Inset);
    }

    /// <summary>A picture too narrow for the whole strip gets a shorter one.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Math.Clamp(availableSize.Width - Beside, 60, Wide);

        if (track.Width != width) track.Width = width;

        return base.MeasureOverride(availableSize);
    }

    /// <summary>A hand put the clock at this many seconds.</summary>
    public event Action<double>? Sought;

    /// <summary>The loop switch was pressed; the owner flips it and says so through <see cref="Follow"/>.</summary>
    public event Action? LoopClicked;

    /// <summary>The strip, for the tests that aim a pointer along it.</summary>
    internal SeekTrack Track => track;

    /// <summary>Whether the patch comes round to nought at its end.</summary>
    public bool Looped => looped;

    /// <summary>Moves the thumb to <paramref name="seconds"/> of <paramref name="length"/>, unless a hand has it.</summary>
    public void Follow(double seconds, double length, bool loops)
    {
        track.Maximum = length;
        if (!track.Held) track.Value = Math.Min(seconds, length);

        time.Text = Said(track.Value, length);

        if (looped == loops) return;

        looped = loops;
        loop.Foreground = loops ? Lit : Unlit;
    }

    private static string Said(double seconds, double length) => $"{StatusClock.Text(seconds)} / {PatchLength.Say(length)}";
}
