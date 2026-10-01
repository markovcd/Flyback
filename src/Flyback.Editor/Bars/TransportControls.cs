using Avalonia.Automation;
using Avalonia.Controls;
using Flyback.App.Capture;
using Flyback.App.Controls;
using Flyback.App.Notices;
using Flyback.App.Statistics;
using Flyback.App.Windows;

namespace Flyback.App.Bars;

/// <summary>
/// Play and pause, and the mute that goes with them, on the transport row and on the
/// pictures that have a window of their own or all of this one.
/// </summary>
internal sealed class TransportControls(Playback playback, TransportRow row, TakeRecording recording, Usage usage)
    : IReactTo<TakeMarked>, IReactTo<TransportChanged>, IReactTo<PauseAsked>
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

    public Task On(TakeMarked notice)
    {
        Sync();
        return Task.CompletedTask;
    }

    public Task On(TransportChanged notice)
    {
        Sync();
        return Task.CompletedTask;
    }

    public Task On(PauseAsked notice)
    {
        TogglePause();
        return Task.CompletedTask;
    }

    public void TogglePause()
    {
        // A take is paced by the samples it is handed, so pausing under one would stop the file.
        if (recording.InHand || recording.Counting) return;

        usage.Count(Used.Paused);

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
            row.Pause.Content = paused ? Glyphs.Play() : Glyphs.Pause();
            AutomationProperties.SetName(row.Pause, paused ? "Play" : "Pause");
        }

        row.Pause.IsEnabled = recording is { InHand: false, Counting: false };
        row.Seek.IsEnabled = row.Pause.IsEnabled;

        ToolTip.SetTip(row.Pause, paused ? TransportRow.PlayTip : TransportRow.PauseTip);

        foreach (var overlay in Overlays)
        {
            overlay.Paused = paused;
            overlay.Muted = playback.Muted;
            overlay.Sounding = playback.Audible;
            overlay.HasSound = playback.HasSound;
        }
    }

    /// <summary>Shows the stats line over a full-screen picture, or puts it away.</summary>
    public void ToggleStats(bool previewIsFullScreen)
    {
        statsShown = !statsShown;
        if (statsShown) usage.Count(Used.Stats);
        SyncStats(previewIsFullScreen);
    }

    public void SyncStats(bool previewIsFullScreen)
    {
        if (Stats is not null) Stats.IsVisible = statsShown && previewIsFullScreen;
        if (PictureWindow is not null) PictureWindow.Stats.IsVisible = statsShown;
    }
}
