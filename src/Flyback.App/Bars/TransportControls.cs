using Avalonia.Controls;
using Flyback.App.Capture;
using Flyback.App.Controls;
using Flyback.App.Windows;

namespace Flyback.App.Bars;

/// <summary>
/// Play and pause, and the mute that goes with them, on the toolbar and on the
/// pictures that have a window of their own or all of this one.
/// </summary>
internal sealed class TransportControls(Playback playback, Toolbar toolbar, TakeRecording recording)
{
    private bool pauseShowsPlay;

    /// <summary>Whether the full-screen picture carries the stats line, wherever it is shown. F3 says.</summary>
    private bool statsShown;

    /// <summary>The dots and toolbar over a full-screen preview, or null before the layout is built.</summary>
    public TransportOverlay? Overlay { get; set; }

    /// <summary>The line saying how the picture is drawn, over the preview's cell while it has the window.</summary>
    public StatsOverlay? Stats { get; set; }

    /// <summary>The window holding the preview on another monitor, while it is there.</summary>
    public PictureWindow? PictureWindow { get; set; }

    /// <summary>Every transport over a picture: the window's own, and the other monitor's while it has one.</summary>
    public IEnumerable<TransportOverlay> Overlays =>
        new[] { Overlay, PictureWindow?.Transport }.OfType<TransportOverlay>();

    public void TogglePause()
    {
        // A take is paced by the samples it is handed, so pausing under one would stop the file.
        if (recording.InHand || recording.Counting) return;

        if (playback.Paused) playback.Resume();
        else playback.Pause();
    }

    /// <summary>Puts the toolbar button and the full-screen overlay in step with the transport.</summary>
    public void Sync()
    {
        // Recompiles call this on every knob frame, so the glyph is only swapped when it changes.
        var paused = playback.Paused;

        if (pauseShowsPlay != paused)
        {
            pauseShowsPlay = paused;
            toolbar.Pause.Content = paused ? Glyphs.Play() : Glyphs.Pause();
        }

        toolbar.Pause.IsEnabled = recording is { InHand: false, Counting: false };
        toolbar.Seek.IsEnabled = toolbar.Pause.IsEnabled;

        ToolTip.SetTip(toolbar.Pause, paused ? Toolbar.PlayTip : Toolbar.PauseTip);

        foreach (var overlay in Overlays)
        {
            overlay.Paused = paused;
            overlay.Muted = playback.Muted;
            overlay.Sounding = playback.Audible;
        }
    }

    /// <summary>Shows the stats line over a full-screen picture, or puts it away.</summary>
    public void ToggleStats(bool previewIsFullScreen)
    {
        statsShown = !statsShown;
        SyncStats(previewIsFullScreen);
    }

    public void SyncStats(bool previewIsFullScreen)
    {
        if (Stats is not null) Stats.IsVisible = statsShown && previewIsFullScreen;
        if (PictureWindow is not null) PictureWindow.Stats.IsVisible = statsShown;
    }
}
