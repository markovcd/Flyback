using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Capture;
using Flyback.App.Controls;
using Flyback.App.Statistics;
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

    /// <summary>A second, as long as one number is left up before the next.</summary>
    private static readonly TimeSpan CountInStep = TimeSpan.FromSeconds(1);

    /// <summary>Live while a take is running, and the only thing that says one is.</summary>
    private LiveRecorder? recorder;

    /// <summary>
    /// Live while a take is being counted in, and cancelled to call the count
    /// off. Null otherwise, which is the only thing that says no count is
    /// running — a take counted in is not yet a take.
    /// </summary>
    private CancellationTokenSource? counting;

    private DispatcherTimer? recordingTicker;

    /// <summary>
    /// A take that has been stopped and whose file is still closing. Null
    /// otherwise, and the only thing that says another take may not start —
    /// which is what keeps one from being written over a file not yet finished.
    /// </summary>
    private Task? finishing;

    /// <summary>
    /// The closing of the file alone, which runs on no thread of the window's and
    /// so is the one part of <see cref="finishing"/> that can be waited for from it.
    /// </summary>
    private Task? closingFile;

    /// <summary>Set once the window has closed, after which nothing may be reported.</summary>
    private bool gone;

    private const string DefaultTakeName = "take";

    private static readonly string RecordTip =
        "Record what the patch is doing now, knobs and all, as it happens.  (Ctrl+R)  A video "
        + "takes the picture off the GPU at whatever the Recording settings say, at "
        + "whatever Size says, with the sound alongside it; a sound file takes the sound "
        + "on its own. What happens between naming the file and the first frame — a count-in, "
        + "and the patch going back to zero — is set in Settings → Recording. It has no fixed "
        + "length, unlike a file `flyback-cli render` writes — it runs until you stop it.";

    private const string StopTip = "End this take and write the file.  (Ctrl+R)";

    private const string CountingTip = "Call off the count and record nothing.  (Ctrl+R)";

    private const string StillFinishing = "The last take is still being written.";

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
    /// offer to start a second recording over the one already running. A count-in is
    /// the same case: unwiring the Output during it must not strand the count with no
    /// way to call it off.
    /// </remarks>
    private void MarkRecordable()
    {
        var kinds = RecordingKinds();

        recordButton.IsEnabled =
            recorder is not null || counting is not null || (finishing is null && kinds.Count > 0);

        ToolTip.SetTip(recordButton, recorder is not null ? StopTip
            : counting is not null ? CountingTip
            : finishing is not null ? StillFinishing
            : kinds.Count > 0 ? RecordTip
            : NothingToRecord);
    }

    /// <summary>
    /// What the toolbar button's press means: start a take, call off the count
    /// before one, or end the one running. The same control does all three — a
    /// take has no length, so stopping it is the only way it ever finishes.
    /// </summary>
    private async Task ToggleRecordAsync()
    {
        if (!recordButton.IsEnabled) return;

        if (counting is not null)
        {
            CallOffCount();
            return;
        }

        if (recorder is not null)
        {
            Stop();
            return;
        }

        await RecordAsync();
    }

    /// <summary>Asks where the take goes, counts it in, and starts it.</summary>
    private async Task RecordAsync()
    {
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
            SuggestedFileName = FileNameFor(patchName),
            DefaultExtension = kinds[0].Patterns?[0].TrimStart('*', '.'),
        });

        if (file?.TryGetLocalPath() is not { } path) return;

        // Before the count rather than only inside Start: a count-in is three
        // seconds of standing ready, and spending them to be told there is no
        // ffmpeg is three seconds nobody gets back.
        var (format, ffmpeg) = Encoder(path);

        if (Refusal(format, ffmpeg, editor.Patch) is { } refused)
        {
            Report(refused);
            return;
        }

        await CountInAsync(path, CountInStep);
    }

    /// <summary>
    /// Counts the take in on the status bar and starts it, unless the count is
    /// called off first. The patch is read at the end rather than the beginning,
    /// because the count is time somebody may still be spending on the patch.
    /// </summary>
    /// <remarks>
    /// How long the count runs and whether the rewind happens at all are the two
    /// Recording settings ADR-0091 added; a count of nought seconds is one
    /// nobody sees, and the take starts on the press.
    /// </remarks>
    /// <param name="step">
    /// How long one number stays up. A parameter because the only other caller
    /// is a test, which has nothing to stand ready for.
    /// </param>
    internal async Task CountInAsync(string path, TimeSpan step)
    {
        var name = Path.GetFileName(path);

        using var count = new CancellationTokenSource();

        counting = count;

        // The glyph is the square from the moment the count starts: the button
        // ends something from here on, and what it ends is the count.
        recordButton.Content = Glyphs.Stop();
        MarkRecordable();

        try
        {
            for (var left = outputSettings.CountInSeconds; left > 0; left--)
            {
                // Reported as progress: the whole count is one sentence with a
                // new number in it, and leaves the log one line rather than three.
                Report($"Recording {name} in {left}…", progress: true);

                await Task.Delay(step, count.Token);
            }
        }
        catch (OperationCanceledException)
        {
            if (!gone) Report($"{name} was not recorded.");
            return;
        }
        finally
        {
            counting = null;

            // Put back for every way out of the count, the take that follows
            // included: Start swaps it straight back to the square, in this
            // same turn of the dispatcher, so there is nothing to see.
            if (!gone)
            {
                recordButton.Content = Glyphs.Record();
                MarkRecordable();
            }
        }

        // What the count was for, where it was asked for: the take begins at
        // nought seconds, in the picture and in the sound, the same place the
        // Rewind button puts them.
        if (outputSettings.RewindBeforeTake)
        {
            RewindToZero();
        }

        Start(path, editor.Patch);
    }

    /// <summary>
    /// Calls off a count-in. The take is not started and no file is written —
    /// nothing has been opened yet, the name having only been chosen.
    /// </summary>
    private void CallOffCount() => counting?.Cancel();

    /// <summary>
    /// A patch's name as a file's. A preset is named for a person to read, and may
    /// hold what no file name can.
    /// </summary>
    internal static string FileNameFor(string? patchName)
    {
        if (string.IsNullOrWhiteSpace(patchName)) return DefaultTakeName;

        var cleaned = string.Join("_", patchName.Split(Path.GetInvalidFileNameChars())).Trim(' ', '.');

        return cleaned.Length > 0 ? cleaned : DefaultTakeName;
    }

    /// <summary>
    /// What the picker offers: the guard on what the patch reaches, asked with the
    /// two formats the settings are on.
    /// </summary>
    private IReadOnlyList<FilePickerFileType> RecordingKinds() => RecordKinds(
        editor.Patch,
        ClipFormats.Wanted(outputSettings.VideoFormat, picture: true),
        ClipFormats.Wanted(outputSettings.SoundFormat, picture: false));

    /// <summary>
    /// Why this take cannot be written, or null for one that can. Asked twice —
    /// once before the count-in and once as the file is opened — because a count
    /// is long enough for the Volume to be turned down inside it.
    /// </summary>
    private string? Refusal(ClipFormat format, string? ffmpeg, Patch patch)
    {
        if (format.NeedsFfmpeg && ffmpeg is null)
        {
            return $"{format.Label} is written by ffmpeg, and there is none on PATH. "
                + "Find it in Settings → Recording, or record an "
                + $"{ClipFormats.MotionJpegAvi.Extension} or a {ClipFormats.Wav.Extension}.";
        }

        if (!format.HasPicture && !WithSound(patch))
        {
            return $"Turn the Output's Volume up before recording a {format.Extension} — there is nothing to record otherwise.";
        }

        return null;
    }

    /// <summary>
    /// Whether a take would have any sound in it: what is actually playing, not
    /// what the patch could play. A take made with the audio switched off has
    /// nothing to record from, whatever is wired up.
    /// </summary>
    private bool WithSound(Patch patch) => patch.Reaches().Sound && audio.IsRunning;

    private void Start(string path, Patch patch)
    {
        // The name decides, not the setting: an extension typed over the one the
        // picker suggested is what somebody meant by typing it.
        var (format, ffmpeg) = Encoder(path);

        if (Refusal(format, ffmpeg, patch) is { } why)
        {
            Report(why);
            return;
        }

        var wantsPicture = format.HasPicture;
        var withSound = WithSound(patch);

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
                ? $"Recording {Clock(status.Seconds)} — {status.Frames} frames{repeated} → {name}{lost}"
                : $"Recording {Clock(status.Seconds)} → {name}{lost}",
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

        var name = Path.GetFileName(running.Path);

        Detach();

        // A take that ended for a reason has its last word already. One that was
        // simply stopped gets a holding line, since closing the file is the only
        // part of this that can take long enough to need one.
        Report(because ?? $"Finishing {name}…", progress: because is null);

        closingFile = Task.Run(running.Dispose);

        // Kept only while it is still to come. A close that has finished by the
        // time it is awaited — a take that failed on its own has nothing left to
        // write — runs this to its end before it returns, and its last act is to
        // clear the field: assigned regardless, the finished task would go back
        // in after that and stay, with Record greyed out behind it for good.
        var closing = FinishAsync(running, closingFile, name, said: because is not null);

        finishing = closing.IsCompleted ? null : closing;

        // The device was kept running for the take whatever Volume said, so it
        // is asked again now there is none — see SyncAudioToVolume.
        SyncAudioToVolume();

        MarkRecordable();
    }

    /// <summary>
    /// Takes the running take away from everything feeding it and puts the
    /// toolbar back, leaving the file still to be closed.
    /// </summary>
    private void Detach()
    {
        // Neither the render thread nor the sound callback may still be handing
        // frames to something that is closing its file.
        preview.EndCapture();
        audio.Capture = null;

        recordingTicker?.Stop();
        recordingTicker = null;

        recorder = null;

        recordButton.Content = Glyphs.Record();
        resolution.IsEnabled = true;
    }

    /// <summary>
    /// Waits for the file to close and reports what it came to. What the writer
    /// says on the way out — ffmpeg refusing an argument, an AVI at its 4 GB
    /// ceiling — is the only account of a take that is not what was asked for,
    /// so it is read after the close rather than before it.
    /// </summary>
    /// <param name="said">
    /// Whether <see cref="Stop"/> has already reported why this ended. Then the
    /// close is only how a take that had already failed was tidied up, and
    /// saying anything more would write over the reason.
    /// </param>
    private async Task FinishAsync(LiveRecorder running, Task closing, string name, bool said)
    {
        try
        {
            await closing;

            if (said || gone) return;

            var status = running.Status;

            if (status.Stopped is null) usage.Count(Used.Recorded);

            Report(status.Stopped is { } failure
                ? $"Recording stopped: {failure}"
                : $"Recorded {Clock(status.Seconds)} to {name}.");
        }
        catch (Exception ex)
        {
            // Nothing awaits this but a window on its way out, so it may not throw.
            if (!gone) Report($"Could not finish {name}: {ex.Message}");
        }
        finally
        {
            finishing = null;
            closingFile = null;

            if (!gone) MarkRecordable();
        }
    }

    /// <summary>Whether a take is running, or stopped and its file not yet closed.</summary>
    private bool TakeInHand => recorder is not null || finishing is not null;

    /// <summary>
    /// Stops the take if one is running and waits for its file to close, which is
    /// what a window about to close has to do first.
    /// </summary>
    private async Task FinishTakeAsync()
    {
        // A count still running would otherwise open a file on a window that is
        // leaving — there is nothing to wait for here, only a count to drop.
        CallOffCount();

        Stop();

        if (finishing is { } pending) await pending;
    }

    /// <summary>
    /// The close nothing could put off: the file is closed on this thread, since
    /// there will be no other once the window has gone.
    /// </summary>
    private void FinishTakeNow()
    {
        gone = true;

        CallOffCount();

        if (recorder is { } running)
        {
            Detach();
            running.Dispose();
        }

        // Only the closing of the file. The rest of a finish resumes on this
        // thread and would wait for ever on being waited for.
        closingFile?.Wait();
    }
}
