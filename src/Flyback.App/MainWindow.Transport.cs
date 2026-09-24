using Avalonia.Controls;
using Flyback.App.Controls;

namespace Flyback.App;

/// <summary>
/// Play and pause, and the mute that goes with them, on the toolbar and on the
/// full-screen preview.
/// </summary>
public sealed partial class MainWindow
{
    private const string PauseTip = "Pause the patch, in the picture and in the sound.  (Ctrl+P)";

    private const string PlayTip = "Play the patch on from where it stopped.  (Ctrl+P)";

    private readonly Button pauseButton = new();

    /// <summary>The dots and toolbar over a full-screen preview, or null before the layout is built.</summary>
    private TransportOverlay? transportOverlay;

    private bool pauseShowsPlay;

    internal bool Paused => playback.Paused;

    internal bool Muted => playback.Muted;

    private void TogglePause()
    {
        // A take is paced by the samples it is handed, so pausing under one would stop the file.
        if (Recording.InHand || Recording.Counting) return;

        if (playback.Paused) playback.Resume();
        else playback.Pause();
    }

    /// <summary>Puts the toolbar button and the full-screen overlay in step with the transport.</summary>
    private void SyncTransport()
    {
        // Recompiles call this on every knob frame, so the glyph is only swapped when it changes.
        var paused = playback.Paused;

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
            overlay.Muted = playback.Muted;
            overlay.Sounding = playback.Audible;
        }
    }

    /// <summary>Every transport over a picture: the window's own, and the other monitor's while it has one.</summary>
    private IEnumerable<TransportOverlay> Transports =>
        new[] { transportOverlay, pictureTransport }.OfType<TransportOverlay>();
}
