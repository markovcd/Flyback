using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.App.Notices;
using Flyback.App.Statistics;
using Flyback.Core.Graph;

namespace Flyback.App.Bars;

/// <summary>
/// The patch's clock as a strip on the toolbar: where it is, and a thumb to drag it
/// anywhere from zero to the patch's length, typed in the box beside it. At the end
/// the patch stops, or comes round to zero where the loop switch is on. An empty box
/// is no length, which the editor plays as <see cref="Patch.DefaultLength"/>.
/// </summary>
/// <remarks>
/// The length is the patch's, an edit like any other; the switch is the editor's, kept
/// with the canvas settings. The end is caught on the thumb's own tick, so it lands
/// within a tenth of a second. The transport over a full-screen picture follows this bar.
/// </remarks>
internal sealed class SeekBar : IReactTo<PatchCompiled>
{
    /// <summary>How often the thumb follows the clock.</summary>
    private static readonly TimeSpan Follow = TimeSpan.FromMilliseconds(100);

    private readonly Playback playback;
    private readonly PreviewHost preview;
    private readonly NodeEditor editor;
    private readonly Document document;
    private readonly Usage usage;

    private readonly List<TransportOverlay> overlays = [];

    public SeekBar(Playback playback, PreviewHost preview, CanvasSection settings, NodeEditor editor, Document document, Usage usage)
    {
        this.playback = playback;
        this.preview = preview;
        this.editor = editor;
        this.document = document;
        this.usage = usage;

        Track = new SeekTrack
        {
            Name = "seek",
            Width = 180,
            Maximum = playback.Length,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Track.Sought += Seek;

        ToolTip.SetTip(Track, "Drag to move the patch's clock, in the picture and in the sound.");

        Length = new TextBox
        {
            Name = "seekLength",
            Text = Said(),
            PlaceholderText = PatchLength.Say(Patch.DefaultLength),
            Width = 76,
            FontSize = Text.Body,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(Length, "How long the patch plays for: seconds, or minutes:seconds, to a hundredth. Empty for no length, which plays three minutes here and plays on in the viewer.");

        Loop = ToolbarButtons.Marked(new ToggleButton(), "seekLoop", Glyphs.Loop(), "Play the patch's length round and round, from zero again at its end.");
        Loop.IsChecked = settings.SeekLoop;
        Loop.IsCheckedChanged += (_, _) =>
        {
            if (Loop.IsChecked == true) usage.Count(Used.Looped);
            settings.SaveSeekLoop(Loop.IsChecked == true);
            Update();
        };

        Length.LostFocus += (_, _) => TakeLength();
        Length.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            TakeLength();
            e.Handled = true;

            // Back to the canvas, so the next Ctrl+Z takes the length back rather than the typing.
            editor.Focus();
        };

        View = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Track, Length, Loop },
        };

        var ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = Follow };
        ticker.Tick += (_, _) => Update();
        View.AttachedToVisualTree += (_, _) => ticker.Start();
        View.DetachedFromVisualTree += (_, _) => ticker.Stop();
    }

    /// <summary>The bar and its length.</summary>
    public StackPanel View { get; }

    /// <summary>The strip the thumb runs along.</summary>
    public SeekTrack Track { get; }

    /// <summary>The box the patch's length is typed in.</summary>
    public TextBox Length { get; }

    /// <summary>The switch that brings the patch round to zero at the end of its length.</summary>
    public ToggleButton Loop { get; }

    /// <summary>Whether the bar can be used: not during a take, which is paced by its own samples.</summary>
    public bool IsEnabled
    {
        get => View.IsEnabled;
        set
        {
            View.IsEnabled = value;
            foreach (var overlay in overlays) overlay.CanSeek = value;
        }
    }

    /// <summary>Has the transport over a picture follow this bar, and move the clock and switch the loop through it.</summary>
    public void Drive(TransportOverlay overlay)
    {
        if (overlays.Contains(overlay)) return;

        overlays.Add(overlay);
        overlay.CanSeek = View.IsEnabled;
        overlay.Sought += Seek;
        overlay.LoopClicked += FlipLoop;
        Update();
    }

    /// <summary>Lets go of the transport over a picture that is going away.</summary>
    public void Drop(TransportOverlay overlay)
    {
        if (!overlays.Remove(overlay)) return;

        overlay.Sought -= Seek;
        overlay.LoopClicked -= FlipLoop;
    }

    /// <summary>
    /// Stops a patch that has reached the end of its length, or brings it round to
    /// zero where it loops, then moves every thumb to where the clock is, unless it is
    /// in somebody's hand.
    /// </summary>
    public void Update()
    {
        if (Held) return;

        if (View.IsEnabled && !playback.Paused && preview.Time >= Track.Maximum)
        {
            if (Loop.IsChecked == true) playback.Rewind();
            else playback.Pause();
        }

        Track.Value = Math.Min(preview.Time, Track.Maximum);

        foreach (var overlay in overlays) overlay.Follow(preview.Time, Track.Maximum, Loop.IsChecked == true);
    }

    /// <summary>Whether a hand has any of the thumbs.</summary>
    private bool Held => Track.Held || overlays.Any(overlay => overlay.Track.Held);

    private void Seek(double seconds)
    {
        if (View.IsEnabled) playback.SeekTo(seconds);
    }

    private void FlipLoop() => Loop.IsChecked = Loop.IsChecked != true;

    /// <summary>Puts the patch's length on the strip, and in the box unless somebody is typing there.</summary>
    public Task On(PatchCompiled notice)
    {
        Measure();
        return Task.CompletedTask;
    }

    private void Measure()
    {
        Track.Maximum = playback.Length;
        if (!Length.IsFocused) Length.Text = Said();
    }

    /// <summary>The patch's length as the box shows it, and empty where it has none.</summary>
    private string Said() => editor.History.Patch.Length is { } seconds ? PatchLength.Say(seconds) : string.Empty;

    /// <summary>Gives the patch what was typed as its length, as an edit, or puts the one it had back.</summary>
    private void TakeLength()
    {
        var patch = editor.History.Patch;
        var cleared = string.IsNullOrWhiteSpace(Length.Text);
        var typed = cleared ? null : PatchLength.Read(Length.Text);

        if ((cleared || typed is not null) && typed != patch.Length)
        {
            patch.Length = typed;
            document.Relaid();
            editor.History.Record();
            document.HandCameOff();
        }

        Length.Text = Said();
        Track.Maximum = playback.Length;
        Update();
    }
}
