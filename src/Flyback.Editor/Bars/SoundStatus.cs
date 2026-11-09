using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Editor.Hubs;
using Flyback.Editor.Shell;
using Flyback.Ui;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Bars;

/// <summary>
/// The status bar's view of the playing sound: how close it peaks to clipping, its
/// oversampling, and how far ahead of real time it renders, each with exact figures as a tip.
/// </summary>
internal sealed class SoundStatus
{
    private readonly Playback playback;

    /// <summary>The oversampling factor, short, beside the rungs.</summary>
    private readonly TextBlock oversample = new()
    {
        Name = "soundOversample",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>How far ahead of real time the sound is worked out.</summary>
    public HeadroomRungs Rungs { get; } = new() { Name = "soundRungs", VerticalAlignment = VerticalAlignment.Center };

    /// <summary>The factor and the rungs together, with the exact figures as their tip.</summary>
    private readonly StackPanel headroom;

    /// <summary>How close the sound comes to clipping, left of the oversampling.</summary>
    public LevelBar Level { get; } = new() { Name = "soundLevel", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), IsVisible = false };

    /// <summary>How many buffers had clipped when the clip dot was last cleared.</summary>
    private long clearedAt;

    /// <summary>How long a step down in oversampling stays marked.</summary>
    private static readonly TimeSpan SteppedFor = TimeSpan.FromSeconds(4);

    private static readonly IBrush SteppedInk = new ImmutableSolidColorBrush(Colors.Attention);
    private static readonly IBrush BehindInk = new ImmutableSolidColorBrush(Colors.Fault);

    private int lastOversample;
    private DateTime steppedUntil;

    /// <summary>The level bar, the oversampling and the rungs, left to right.</summary>
    public Control View { get; }

    public SoundStatus(Playback playback, ReportLine report)
    {
        this.playback = playback;

        headroom = new StackPanel
        {
            Name = "soundHeadroom",
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Margin = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            IsVisible = false,
            Children = { oversample, Rungs },
        };

        // A touch screen shows no tip, so a tap says the figures on the report line.
        headroom.Tapped += (_, _) => report.Say(Figures());

        // A tap or click clears a clip, and says what the level was.
        Level.Tapped += (_, _) =>
        {
            report.Say(LevelFigures());
            clearedAt = playback.SoundClipped;
            ShowLevel();
        };

        View = new StackPanel { Orientation = Orientation.Horizontal, Children = { Level, headroom } };
    }

    /// <summary>Shows or hides it all by whether the patch has sound, and redraws the oversampling and rungs.</summary>
    public void Update()
    {
        // Nothing is shown of the sound of a patch that has none.
        headroom.IsVisible = Level.IsVisible = playback.HasSound;

        if (playback.HasSound) ShowHeadroom();
    }

    /// <summary>The oversampling and the rungs, marked for a few seconds after a step down.</summary>
    private void ShowHeadroom()
    {
        var factor = playback.Oversample;
        var now = DateTime.UtcNow;

        if (factor < lastOversample) steppedUntil = now + SteppedFor;
        lastOversample = factor;

        var speed = playback.SoundSpeed;
        var state = SoundHeadroom.Of(speed, factor);
        var stepped = now < steppedUntil;

        oversample.Text = string.Create(CultureInfo.InvariantCulture, $"{factor}×");
        if (state == Headroom.Behind) oversample.Foreground = BehindInk;
        else if (stepped) oversample.Foreground = SteppedInk;
        else oversample.ClearValue(TextBlock.ForegroundProperty);
        Rungs.Show(SoundHeadroom.Shown(speed, state), state, stepped);

        // Written while closed only, so an open tip holds still to be read.
        if (!ToolTip.GetIsOpen(headroom)) ToolTip.SetTip(headroom, Figures());
    }

    /// <summary>The level bar as the sound plays now, with its exact figures as the tip.</summary>
    public void ShowLevel()
    {
        if (!Level.IsVisible) return;

        var clipped = playback.SoundClipped;

        // A clip counted before this sound began is not this sound's.
        if (clipped < clearedAt) clearedAt = clipped;

        Level.Show(SoundLevel.Decibels(playback.SoundPeak), clipped > clearedAt, DateTime.UtcNow);

        if (!ToolTip.GetIsOpen(Level)) ToolTip.SetTip(Level, LevelFigures());
    }

    /// <summary>The exact figures behind the level bar.</summary>
    private string LevelFigures()
    {
        var peak = Level.Held;
        var clips = playback.SoundClipped - clearedAt;
        var said = double.IsNegativeInfinity(peak)
            ? "Silent"
            : string.Create(CultureInfo.InvariantCulture, $"Peak {peak:0.0} dB");

        return clips > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{said}; clipped in {clips} buffers since last cleared. Click to clear.")
            : $"{said}; no clipping.";
    }

    /// <summary>The exact figures behind the rungs.</summary>
    private string Figures()
    {
        var factor = OversamplingText.Of(playback.Oversample);
        var speed = playback.SoundSpeed;

        return speed > 0
            ? string.Create(CultureInfo.InvariantCulture, $"Sound renders {speed:0.00}× faster than it plays, with {factor}.")
            : $"Sound with {factor}; how fast it renders is not timed yet.";
    }
}
