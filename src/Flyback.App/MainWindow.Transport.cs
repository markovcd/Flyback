using Avalonia.Controls;
using Flyback.App.Audio;
using Flyback.App.Controls;

namespace Flyback.App;

/// <summary>
/// Play and pause, and the mute that goes with them, on the toolbar and on the
/// full-screen preview.
/// </summary>
/// <remarks>
/// Paused is the same as in the viewer: the device stops and the picture is timed by a
/// clock that holds still, so an edit still redraws and a resume adds one frame rather
/// than the whole pause. Rewinding while paused stays paused, on the first frame.
/// </remarks>
public sealed partial class MainWindow
{
    private const string PauseTip = "Pause the patch, in the picture and in the sound.  (Ctrl+P)";

    private const string PlayTip = "Play the patch on from where it stopped.  (Ctrl+P)";

    private readonly Button pauseButton = new();

    /// <summary>The dots and toolbar over a full-screen preview, or null before the layout is built.</summary>
    private TransportOverlay? transportOverlay;

    private bool paused;
    private bool muted;
    private bool pauseShowsPlay;

    /// <summary>Where the picture is held while <see cref="paused"/>.</summary>
    private double frozenAt;

    /// <summary>Whether the speakers would be heard: there is a device, and the Output's Volume is up.</summary>
    private bool Audible => sound.Output is not null && !audioBlocked && Sound.VolumeIsUp(editor.Patch);

    internal bool Paused => paused;

    internal bool Muted => muted;

    private void TogglePause()
    {
        // A take is paced by the samples it is handed, so pausing under one would stop the file.
        if (Recording.InHand || Recording.Counting) return;

        if (paused) Resume();
        else Pause();
    }

    private void Pause()
    {
        if (paused) return;

        frozenAt = preview.Time;
        paused = true;

        SetAudioEnabled(false);
        SyncTransport();
    }

    private void Resume()
    {
        if (!paused) return;

        paused = false;

        // The device puts the audio clock back when it starts; with none, the picture runs on its own.
        preview.Clock = null;

        SyncAudioToVolume();
    }

    /// <summary>Silences the speakers without stopping the device, so the clock does not drift.</summary>
    private void ToggleMute()
    {
        muted = !muted;
        audio.Gain = muted ? 0f : 1f;

        SyncTransport();
    }

    /// <summary>Puts the toolbar button and the full-screen overlay in step with the transport.</summary>
    private void SyncTransport()
    {
        // Recompiles call this on every knob frame, so the glyph is only swapped when it changes.
        if (pauseShowsPlay != paused)
        {
            pauseShowsPlay = paused;
            pauseButton.Content = paused ? Glyphs.Play() : Glyphs.Pause();
        }

        pauseButton.IsEnabled = !Recording.InHand && !Recording.Counting;

        ToolTip.SetTip(pauseButton, paused ? PlayTip : PauseTip);

        foreach (var overlay in Transports)
        {
            overlay.Paused = paused;
            overlay.Muted = muted;
            overlay.Sounding = Audible;
        }
    }

    /// <summary>Every transport over a picture: the window's own, and the other monitor's while it has one.</summary>
    private IEnumerable<TransportOverlay> Transports =>
        new[] { transportOverlay, pictureTransport }.OfType<TransportOverlay>();
}
