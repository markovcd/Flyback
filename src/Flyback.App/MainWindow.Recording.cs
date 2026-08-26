using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Capture;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.App;

/// <summary>
/// Recording a performance, as opposed to rendering a patch. The export writes
/// what the patch would do; this writes what it did.
/// </summary>
/// <remarks>
/// The difference is the whole point. An export freezes the patch and evaluates
/// it frame by frame on the processor, so a knob turned while it runs changes the
/// next export and not that one. A take reads the frames the GPU has already
/// drawn and the samples the speakers have already had, which means the knobs —
/// and therefore the performance — are in the file.
/// <para>
/// That also means it can only record what is on screen: the picture comes off
/// the card, so the GPU renderer has to be the one running. Sound is recorded
/// wherever it is playing, so a WAV needs the audio on and an AVI made with the
/// audio off is simply a silent one.
/// </para>
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>How often the status line is refreshed while a take runs.</summary>
    private static readonly TimeSpan RecordingTick = TimeSpan.FromMilliseconds(500);

    /// <summary>Live while a take is running, and the only thing that says one is.</summary>
    private LiveRecorder? recorder;

    private DispatcherTimer? recordingTicker;

    private static readonly string RecordTip =
        "Record what the patch is doing now, knobs and all, as it happens. An AVI "
        + $"takes the picture off the GPU at {MovieRenderer.DefaultFrameRate:0} frames a second, at "
        + "whatever Size says, with the sound alongside it; a WAV takes the sound "
        + "on its own. Unlike an export it has no length — it runs until you stop it.";

    private const string NothingToRecord =
        "Nothing is wired into the Output, so there is nothing to record. "
        + "Patch something into its 'color' or its 'left'.";

    /// <summary>
    /// Whether there is anything a take could contain, and what to say about it.
    /// </summary>
    /// <remarks>
    /// A take already running keeps the button enabled whatever the patch says,
    /// for the reason the export's does: it is <c>Stop</c> by then, and editing
    /// mid-take must not take away the only way to finish the file.
    /// </remarks>
    private void MarkRecordable()
    {
        var kinds = RecordKinds(editor.Patch);

        recordButton.IsEnabled = recorder is not null || kinds.Count > 0;

        ToolTip.SetTip(recordButton, kinds.Count > 0 ? RecordTip : NothingToRecord);
    }

    /// <summary>Asks where the take goes, and starts it.</summary>
    private async Task RecordAsync()
    {
        var patch = editor.Patch;
        var kinds = RecordKinds(patch);

        if (kinds.Count == 0)
        {
            Report(NothingToRecord);
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Record",
            FileTypeChoices = kinds,
            SuggestedFileName = "take",
            DefaultExtension = kinds[0].Patterns?[0].TrimStart('*', '.'),
        });

        if (file?.TryGetLocalPath() is not { } path) return;

        Start(path, patch);
    }

    private void Start(string path, Patch patch)
    {
        var wantsPicture = path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase);

        // What is actually playing, not what the patch could play. A take with
        // the audio switched off has no sound to record, whatever is wired up.
        var withSound = patch.Reaches().Sound && audio.IsRunning;

        if (!wantsPicture && !withSound)
        {
            Report("Turn the audio on before recording a WAV — there is nothing to record otherwise.");
            return;
        }

        var size = wantsPicture ? preview.Resolution : default;

        var settings = new RecordingSettings(
            path,
            size,
            MovieRenderer.DefaultFrameRate,
            JpegWriter.DefaultQuality,
            withSound ? audio.SampleRate : 0,
            withSound ? NodeCatalog.AudioChannels : 0);

        LiveRecorder started;

        try
        {
            started = new LiveRecorder(settings);
        }
        catch (Exception ex)
        {
            Report($"Could not start recording: {ex.Message}");
            return;
        }

        // The picture is asked for last, because it is the one that can refuse —
        // and a file already open would then have to be unpicked.
        if (wantsPicture && preview.BeginCapture(started) is { } refused)
        {
            started.Dispose();
            File.Delete(path);

            Report($"Could not record the picture: {refused}");
            return;
        }

        if (withSound) audio.Capture = started;

        recorder = started;

        recordButton.Content = "Stop";

        // The header has already committed to a frame size, so the picker cannot
        // be allowed to change it underneath the take.
        resolution.IsEnabled = false;

        recordingTicker = new DispatcherTimer(DispatcherPriority.Background) { Interval = RecordingTick };
        recordingTicker.Tick += (_, _) => ShowProgress();
        recordingTicker.Start();

        Report($"Recording to {System.IO.Path.GetFileName(path)}…");
    }

    private void ShowProgress()
    {
        if (recorder is not { } running) return;

        var status = running.Status;

        if (status.Stopped is { } failure)
        {
            Stop($"Recording stopped: {failure}");
            return;
        }

        var name = System.IO.Path.GetFileName(running.Path);
        var repeated = status.Duplicated > 0 ? $", {status.Duplicated} repeated" : string.Empty;
        var lost = status.AudioDropped > 0 ? "  •  dropping sound — the disk is not keeping up" : string.Empty;

        Report(
            status.Frames > 0
                ? $"Recording {status.Seconds:0.0}s — {status.Frames} frames{repeated} → {name}{lost}"
                : $"Recording {status.Seconds:0.0}s → {name}{lost}",
            progress: true);
    }

    /// <summary>
    /// Finishes the file and says so. Everything that ends a take goes through
    /// here, including the ones nobody asked for.
    /// </summary>
    private void Stop(string? because = null)
    {
        if (recorder is not { } running) return;

        // Detached first, so neither the render thread nor the sound callback is
        // still handing frames to something that is closing its file.
        preview.EndCapture();
        audio.Capture = null;

        recordingTicker?.Stop();
        recordingTicker = null;

        var status = running.Status;
        var name = System.IO.Path.GetFileName(running.Path);

        running.Dispose();
        recorder = null;

        recordButton.Content = "Record…";
        resolution.IsEnabled = true;

        MarkRecordable();

        Report(because ?? $"Recorded {status.Seconds:0.0}s to {name}.");
    }
}
