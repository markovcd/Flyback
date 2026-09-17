using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Capture;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.App;

/// <summary>
/// Recording a performance, as opposed to rendering a patch. `flyback-cli
/// render` writes what the patch would do; this writes what it did —
/// ADR-0078 moved the shell's own export to that command, leaving this the
/// only way to write a file from inside the program.
/// </summary>
/// <remarks>
/// A render freezes the patch and evaluates it frame by frame on the processor, so a
/// knob turned while it runs changes the next one. A take reads the frames the GPU
/// has already drawn and the samples the speakers have already had, which puts the
/// performance in the file — and means it can only record what is on screen, with the
/// GPU renderer running. Sound is recorded wherever it is playing.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>How often the status line is refreshed while a take runs.</summary>
    private static readonly TimeSpan RecordingTick = TimeSpan.FromMilliseconds(500);

    /// <summary>Live while a take is running, and the only thing that says one is.</summary>
    private LiveRecorder? recorder;

    private DispatcherTimer? recordingTicker;

    private static readonly string RecordTip =
        "Record what the patch is doing now, knobs and all, as it happens.  (Ctrl+R)  A video "
        + "takes the picture off the GPU at whatever the Recording settings say, at "
        + "whatever Size says, with the sound alongside it; a sound file takes the sound "
        + "on its own. It has no fixed length, unlike a file `flyback-cli render` "
        + "writes — it runs until you stop it.";

    private const string StopTip = "End this take and write the file.  (Ctrl+R)";

    private const string NothingToRecord =
        "Nothing is wired into the Output, so there is nothing to record. "
        + "Patch something into its 'color' or its 'left'.";

    /// <summary>
    /// Whether there is anything a take could contain, and what to say about it.
    /// </summary>
    /// <remarks>
    /// A take already running keeps the button enabled whatever the patch says: it is
    /// <c>Stop</c> by then, and editing mid-take must not take away the only way to
    /// finish the file. The tip follows the same state, since a patch edit calls this
    /// again mid-take — ADR-0021 recompiles on every edit — and must not read as an
    /// offer to start a second recording over the one already running.
    /// </remarks>
    private void MarkRecordable()
    {
        var kinds = RecordingKinds();

        recordButton.IsEnabled = recorder is not null || kinds.Count > 0;

        ToolTip.SetTip(recordButton, recorder is not null ? StopTip
            : kinds.Count > 0 ? RecordTip
            : NothingToRecord);
    }

    /// <summary>
    /// What the toolbar button's press means: start a take, or end the one
    /// running. The same control does both — a take has no length, so
    /// stopping it is the only way it ever finishes.
    /// </summary>
    private async Task ToggleRecordAsync()
    {
        if (!recordButton.IsEnabled) return;

        if (recorder is not null)
        {
            Stop();
            return;
        }

        await RecordAsync();
    }

    /// <summary>Asks where the take goes, and starts it.</summary>
    private async Task RecordAsync()
    {
        var patch = editor.Patch;
        var kinds = RecordingKinds();

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

    /// <summary>
    /// What the picker offers: the guard on what the patch reaches, asked with the
    /// two formats the settings are on.
    /// </summary>
    private IReadOnlyList<FilePickerFileType> RecordingKinds() => RecordKinds(
        editor.Patch,
        ClipFormats.Wanted(outputSettings.VideoFormat, picture: true),
        ClipFormats.Wanted(outputSettings.SoundFormat, picture: false));

    private void Start(string path, Patch patch)
    {
        // The name decides, not the setting: an extension typed over the one the
        // picker suggested is what somebody meant by typing it.
        var (format, ffmpeg) = Encoder(path);

        if (format.NeedsFfmpeg && ffmpeg is null)
        {
            Report(
                $"{format.Label} is written by ffmpeg, and there is none on PATH. "
                + "Find it in Settings → Recording, or record an "
                + $"{ClipFormats.MotionJpegAvi.Extension} or a {ClipFormats.Wav.Extension}.");

            return;
        }

        var wantsPicture = format.HasPicture;

        // What is actually playing, not what the patch could play. A take with
        // the audio switched off has no sound to record, whatever is wired up.
        var withSound = patch.Reaches().Sound && audio.IsRunning;

        if (!wantsPicture && !withSound)
        {
            Report($"Turn the Output's Volume up before recording a {format.Extension} — there is nothing to record otherwise.");
            return;
        }

        var size = wantsPicture ? preview.Resolution : default;

        var settings = new RecordingSettings(
            path,
            format,
            size,
            outputSettings.FrameRate,
            outputSettings.JpegQuality,
            withSound ? audio.SampleRate : 0,
            withSound ? NodeCatalog.AudioChannels : 0,
            ffmpeg);

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

            // A file nothing was ever written to, so whether it can be removed
            // says nothing worth saying over the reason the take did not start.
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Left where it is, empty.
            }

            Report($"Could not record the picture: {refused}");
            return;
        }

        if (withSound) audio.Capture = started;

        recorder = started;

        recordButton.Content = Glyphs.Stop();

        // Recomputed from scratch rather than just flipping the tip: with a
        // recorder now set, this reads the same branch Stop() will read once
        // it clears it, so Start and Stop share the one place that decides
        // what the button says.
        MarkRecordable();

        // The header has already committed to a frame size, so the picker cannot
        // be allowed to change it underneath the take.
        resolution.IsEnabled = false;

        recordingTicker = new DispatcherTimer(DispatcherPriority.Background) { Interval = RecordingTick };
        recordingTicker.Tick += (_, _) => ShowProgress();
        recordingTicker.Start();

        Report($"Recording to {Path.GetFileName(path)}…");
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

        var name = Path.GetFileName(running.Path);
        var repeated = status.Duplicated > 0 ? $", {status.Duplicated} repeated" : string.Empty;
        var lost = status.AudioDropped > 0 ? "  •  dropping sound — the disk is not keeping up" : string.Empty;

        Report(
            status.Frames > 0
                ? $"Recording {status.Seconds:0.0}s — {status.Frames} frames{repeated} → {name}{lost}"
                : $"Recording {status.Seconds:0.0}s → {name}{lost}",
            progress: true);
    }

    /// <summary>
    /// Ends a take and says so. Everything that ends one goes through here,
    /// including the ones nobody asked for.
    /// </summary>
    /// <remarks>
    /// The shell is handed back at once and the file is finished behind it, because
    /// finishing is no longer a header patch: an ffmpeg format ends by muxing the
    /// sound into the picture, which reads and rewrites everything recorded, and
    /// the window must not be frozen for the length of that. So the last word on
    /// a take — its length, or what went wrong writing it — is reported when the
    /// file is actually closed rather than when Stop was pressed.
    /// </remarks>
    private void Stop(string? because = null)
    {
        if (recorder is not { } running) return;

        // Detached first, so neither the render thread nor the sound callback is
        // still handing frames to something that is closing its file.
        preview.EndCapture();
        audio.Capture = null;

        recordingTicker?.Stop();
        recordingTicker = null;

        var name = Path.GetFileName(running.Path);

        // Taken off the field before anything is awaited, so a second press —
        // or the preview losing its picture as this runs — is a no-op rather
        // than a second attempt to close the same file.
        recorder = null;

        recordButton.Content = Glyphs.Record();
        resolution.IsEnabled = true;

        MarkRecordable();

        // A take that ended for a reason has its last word already. One that was
        // simply stopped gets a holding line, since closing the file is the only
        // part of this that can take long enough to need one.
        Report(because ?? $"Finishing {name}…", progress: because is null);

        _ = FinishAsync(running, name, said: because is not null);
    }

    /// <summary>
    /// Closes the file off the UI thread and reports what it came to. What the
    /// writer says on the way out — ffmpeg refusing an argument, an AVI at its
    /// 4 GB ceiling — is the only account of a take that is not what was asked
    /// for, so it is read after the close rather than before it.
    /// </summary>
    /// <param name="said">
    /// Whether <see cref="Stop"/> has already reported why this ended. Then the
    /// close is only how a take that had already failed was tidied up, and
    /// saying anything more would write over the reason.
    /// </param>
    private async Task FinishAsync(LiveRecorder running, string name, bool said)
    {
        await Task.Run(running.Dispose);

        if (said) return;

        var status = running.Status;

        Report(status.Stopped is { } failure
            ? $"Recording stopped: {failure}"
            : $"Recorded {status.Seconds:0.0}s to {name}.");
    }
}
