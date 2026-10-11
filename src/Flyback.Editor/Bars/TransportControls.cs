using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Flyback.Editor.Capture;
using Flyback.Ui.Midi;
using Flyback.Ui.Controls;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Statistics;
using Flyback.Plugins.Midi;
using Flyback.Editor.Hubs;

namespace Flyback.Editor.Bars;

/// <summary>
/// Play and pause, and the mute that goes with them, on the transport row and on the
/// pictures that have a window of their own or all of this one.
/// </summary>
internal sealed class TransportControls : IReactTo<TakeMarked>, IReactTo<TransportChanged>, IReactTo<PauseAsked>
{
    private readonly Playback playback;
    private readonly TransportRow row;
    private readonly TakeRecording recording;
    private readonly Usage usage;
    private readonly OutputSettingRepository settings;

    /// <param name="settings">Whether an instrument's Start, Continue and Stop play and pause the patch: the MIDI section's setting.</param>
    public TransportControls(Playback playback, TransportRow row, TakeRecording recording, Usage usage, MidiHub midi, OutputSettingRepository settings)
    {
        this.playback = playback;
        this.row = row;
        this.recording = recording;
        this.usage = usage;
        this.settings = settings;

        midi.Transported += (_, action) => Dispatcher.UIThread.Post(() => Follow(action));
    }

    private bool pauseShowsPlay;

    /// <summary>Whether the full-screen picture carries the stats line, wherever it is shown. F3 says.</summary>
    private bool statsShown;

    /// <summary>The dots and toolbar over a full-screen preview, or null before the layout is built.</summary>
    public TransportOverlay? Overlay { get; private set; }

    /// <summary>The line saying how the picture is drawn, over the preview's cell while it has the window.</summary>
    public StatsOverlay? Stats { get; private set; }

    /// <summary>The transport and stats line over the picture on another monitor, while it is there.</summary>
    public (TransportOverlay Transport, StatsOverlay Stats)? Away { get; private set; }

    /// <summary>The transport and stats line over the window's own preview, once the layout has built them.</summary>
    public void Register(TransportOverlay overlay, StatsOverlay stats)
    {
        Overlay = overlay;
        Stats = stats;
    }

    /// <summary>The picture has gone to another monitor, under these.</summary>
    public void RegisterAway(TransportOverlay transport, StatsOverlay stats) => Away = (transport, stats);

    /// <summary>Forgets the away picture under <paramref name="transport"/>, and says whether it was the one held.</summary>
    public bool ReleaseAway(TransportOverlay transport)
    {
        if (Away?.Transport != transport) return false;

        Away = null;
        return true;
    }

    /// <summary>Every transport over a picture: the window's own, and the other monitor's while it has one.</summary>
    public IEnumerable<TransportOverlay> Overlays =>
        new[] { Overlay, Away?.Transport }.OfType<TransportOverlay>();

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

    /// <summary>
    /// An instrument pressed Start, Continue or Stop: from the top, on from where it
    /// paused, or paused. Left alone under a take, for the reason <see cref="TogglePause"/> is.
    /// </summary>
    private void Follow(MidiAction action)
    {
        if (!settings.Current.FollowTransport || recording.InHand || recording.Counting) return;

        switch (action)
        {
            case MidiAction.Start:
                playback.Rewind();
                playback.Resume();
                break;

            case MidiAction.Continue:
                playback.Resume();
                break;

            case MidiAction.Stop:
                playback.Pause();
                break;
        }

        Sync();
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
        if (Away is { } away) away.Stats.IsVisible = statsShown;
    }
}
