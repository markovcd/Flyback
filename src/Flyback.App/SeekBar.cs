using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Flyback.App.Controls;

namespace Flyback.App;

/// <summary>
/// The patch's clock as a strip on the toolbar: where it is, and a thumb to drag it
/// anywhere from zero to a length typed in the box beside it.
/// </summary>
/// <remarks>
/// A patch that runs longer than the strip holds the thumb at the far end. The
/// length is the editor's rather than the patch's, so it is kept with the canvas
/// settings and not in the file.
/// </remarks>
internal sealed class SeekBar
{
    /// <summary>How often the thumb follows the clock.</summary>
    private static readonly TimeSpan Follow = TimeSpan.FromMilliseconds(100);

    private readonly Playback playback;
    private readonly PreviewHost preview;
    private readonly CanvasSection settings;

    /// <summary>Set while the thumb is being moved to follow the clock, so that move is not taken for a seek.</summary>
    private bool following;

    /// <summary>Set while the thumb is being dragged, when it is the hand's and not the clock's.</summary>
    private bool held;

    public SeekBar(Playback playback, PreviewHost preview, CanvasSection settings)
    {
        this.playback = playback;
        this.preview = preview;
        this.settings = settings;

        Track = new Slider
        {
            Name = "seek",
            Width = 180,
            Minimum = 0,
            Maximum = settings.SeekLength,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Track.AddHandler(Thumb.DragStartedEvent, (_, _) => held = true);
        Track.AddHandler(Thumb.DragCompletedEvent, (_, _) => held = false);

        ToolTip.SetTip(Track, "Drag to move the patch's clock, in the picture and in the sound.");

        Length = new TextBox
        {
            Name = "seekLength",
            Text = Say(settings.SeekLength),
            Width = 64,
            FontSize = Text.Body,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(Length, "How long the bar is: seconds, or minutes:seconds.");

        Track.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !following) playback.SeekTo(Track.Value);
        };

        Length.LostFocus += (_, _) => TakeLength();
        Length.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            TakeLength();
            e.Handled = true;
        };

        View = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Track, Length },
        };

        var ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = Follow };
        ticker.Tick += (_, _) => Update();
        ticker.Start();
    }

    /// <summary>The bar and its length.</summary>
    public StackPanel View { get; }

    /// <summary>The strip the thumb runs along.</summary>
    public Slider Track { get; }

    /// <summary>The box the strip's length is typed in.</summary>
    public TextBox Length { get; }

    /// <summary>Whether the bar can be used: not during a take, which is paced by its own samples.</summary>
    public bool IsEnabled
    {
        get => View.IsEnabled;
        set => View.IsEnabled = value;
    }

    /// <summary>Moves the thumb to where the clock is, unless it is in somebody's hand.</summary>
    public void Update()
    {
        if (held) return;

        following = true;
        Track.Value = Math.Min(preview.Time, Track.Maximum);
        following = false;
    }

    /// <summary>
    /// A length as typed, in whole seconds: seconds, or minutes and seconds with a colon
    /// between. Null for anything else, or for one outside what the bar can span.
    /// </summary>
    public static double? Parse(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        var parts = typed.Trim().Split(':');
        if (parts.Length > 2) return null;

        var seconds = 0d;

        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0) return null;

            seconds = seconds * 60 + number;
        }

        seconds = Math.Round(seconds);

        return seconds is >= CanvasSettings.MinSeekLength and <= CanvasSettings.MaxSeekLength ? seconds : null;
    }

    /// <summary>A length as the box shows it: minutes and seconds.</summary>
    public static string Say(double seconds)
    {
        var whole = (long)Math.Round(seconds);

        return string.Create(CultureInfo.InvariantCulture, $"{whole / 60}:{whole % 60:00}");
    }

    /// <summary>Takes what was typed as the length, or puts the one it had back.</summary>
    private void TakeLength()
    {
        if (Parse(Length.Text) is { } seconds)
        {
            Track.Maximum = seconds;
            settings.SaveSeekLength(seconds);
            Update();
        }

        Length.Text = Say(Track.Maximum);
    }
}
