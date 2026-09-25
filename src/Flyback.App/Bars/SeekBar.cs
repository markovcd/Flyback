using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.Core.Graph;

namespace Flyback.App.Bars;

/// <summary>
/// The patch's clock as a strip on the toolbar: where it is, and a thumb to drag it
/// anywhere from zero to the patch's length, typed in the box beside it. At the end
/// the patch stops, or comes round to zero where the loop switch is on.
/// </summary>
/// <remarks>
/// The length is the patch's, an edit like any other; the switch is the editor's, kept
/// with the canvas settings. The end is caught on the thumb's own tick, so it lands
/// within a tenth of a second. A seek bar over a full-screen picture follows this one.
/// </remarks>
internal sealed class SeekBar
{
    /// <summary>How often the thumb follows the clock.</summary>
    private static readonly TimeSpan Follow = TimeSpan.FromMilliseconds(100);

    private readonly Playback playback;
    private readonly PreviewHost preview;
    private readonly NodeEditor editor;
    private readonly Document document;

    private readonly List<SeekOverlay> overlays = [];

    public SeekBar(Playback playback, PreviewHost preview, CanvasSection settings, NodeEditor editor, Document document)
    {
        this.playback = playback;
        this.preview = preview;
        this.editor = editor;
        this.document = document;

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
            Text = PatchLength.Say(playback.Length),
            Width = 76,
            FontSize = Text.Body,
            VerticalAlignment = VerticalAlignment.Center,
        };

        ToolTip.SetTip(Length, "How long the patch plays for: seconds, or minutes:seconds, to a hundredth.");

        Loop = ToolbarButtons.Toggle("seekLoop", "⟲", "Play the patch's length round and round, from zero again at its end.");
        Loop.IsChecked = settings.SeekLoop;
        Loop.IsCheckedChanged += (_, _) =>
        {
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

        // An open, an undo or the text built again can each bring another length.
        playback.Compiled += (_, _) => Measure();

        View = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Track, Length, Loop },
        };

        var ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = Follow };
        ticker.Tick += (_, _) => Update();
        ticker.Start();
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
        set => View.IsEnabled = value;
    }

    /// <summary>Has a seek bar over a picture follow this one, and move the clock and switch the loop through it.</summary>
    public void Drive(SeekOverlay overlay)
    {
        if (overlays.Contains(overlay)) return;

        overlays.Add(overlay);
        overlay.Sought += Seek;
        overlay.LoopClicked += FlipLoop;
        Update();
    }

    /// <summary>Lets go of a seek bar over a picture that is going away.</summary>
    public void Drop(SeekOverlay overlay)
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
    private void Measure()
    {
        Track.Maximum = playback.Length;
        if (!Length.IsFocused) Length.Text = PatchLength.Say(playback.Length);
    }

    /// <summary>Gives the patch what was typed as its length, as an edit, or puts the one it had back.</summary>
    private void TakeLength()
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (PatchLength.Read(Length.Text) is { } seconds && seconds != playback.Length)
        {
            editor.History.Patch.Length = seconds;
            document.Relaid();
            editor.History.Record();
            document.HandCameOff();
        }

        Length.Text = PatchLength.Say(playback.Length);
        Track.Maximum = playback.Length;
        Update();
    }
}
