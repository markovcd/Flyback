using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.App;

/// <summary>
/// Everything the window writes or reads: patches, still frames, and the two
/// kinds of export. The engine does the work in every case — these are the file
/// pickers around it, the progress on the button, and the one decision the
/// pickers cannot make for themselves, which is what a patch has to offer.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly PixelSize ExportSize = new(1920, 1080);

    /// <summary>
    /// Greys the export out while there is nothing to write, which is the same
    /// question the dialog would have asked a moment later — better answered on
    /// the button than by a file picker with nothing in its list.
    /// </summary>
    /// <remarks>
    /// An export already running keeps it enabled whatever the patch says: the
    /// button is <c>Stop</c> by then, and editing the patch mid-render must not
    /// take away the only way to abandon it.
    /// </remarks>
    private void MarkExportable()
    {
        var kinds = ExportKinds(editor.Patch);

        exportButton.IsEnabled = export is not null || kinds.Count > 0;

        ToolTip.SetTip(exportButton, kinds.Count > 0 ? ExportTip : NothingToExport);

        MarkRecordable();
    }

    private static readonly string ExportTip =
        "Write the patch to a file. The name decides which: AVI for the moving picture "
        + $"— Motion JPEG at {MovieRenderer.DefaultFrameRate:0} frames a second, at whatever "
        + "Size says, with the sound alongside it — WAV for the sound on its own, or PNG "
        + $"for one frame at {ExportSize.Width} x {ExportSize.Height}. Length says how long "
        + "the first two run for; a still ignores it.";

    private const string NothingToExport =
        "Nothing is wired into the Output, so there is nothing to write. "
        + "Patch something into its 'color' or its 'left'.";

    private async Task OpenPatchAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open patch",
            AllowMultiple = false,
            FileTypeFilter = [PatchFileType],
        });

        if (files.Count == 0) return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var loaded = PatchIo.Read(await reader.ReadToEndAsync());

            // A patch short of a module would open with holes in it and compile
            // to something that is not what was saved. Better to refuse it and
            // say what is missing, leaving what is open where it was.
            if (!loaded.IsComplete)
            {
                Report($"Not opened. {loaded.Summary}", loaded.Detail);
                return;
            }

            // Where a relative sample path is measured from, before the patch
            // that names one is compiled for the first time.
            samples.Beside = Path.GetDirectoryName(files[0].TryGetLocalPath());

            editor.Patch = loaded.Patch;
            preview.Rewind();
        }
        catch (Exception ex)
        {
            Report($"Could not open patch: {ex.Message}");
        }
    }

    /// <returns>Whether a file was written. A cancelled picker is not one.</returns>
    private async Task<bool> SavePatchAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save patch",
            SuggestedFileName = "patch",
            DefaultExtension = PatchIo.FileExtension,
            FileTypeChoices = [PatchFileType],
        });

        if (file is null) return false;

        try
        {
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(PatchIo.ToJson(editor.Patch));
            }

            // Only once it is actually on disk. A patch that failed to write is
            // still a patch with everything to lose.
            editor.MarkSaved();

            // A patch saved somewhere new measures its samples from there now,
            // which is what lets one be written beside the sounds it names.
            samples.Beside = Path.GetDirectoryName(file.TryGetLocalPath());
            Recompile();

            return true;
        }
        catch (Exception ex)
        {
            Report($"Could not save patch: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The frame an export is written at, as a ratio. Only the sound needs it —
    /// a scanned patch sweeps a width, and it should be the width being written.
    /// </summary>
    private static float ExportAspect => (float)ExportSize.Width / ExportSize.Height;

    private static FilePickerFileType Avi => new("AVI video") { Patterns = ["*.avi"] };

    private static FilePickerFileType Wav => new("WAV audio") { Patterns = ["*.wav"] };

    private static FilePickerFileType Png => new("PNG image") { Patterns = ["*.png"] };

    /// <summary>
    /// The kinds of file this patch could be written to, in the order the dialog
    /// should offer them.
    /// </summary>
    /// <remarks>
    /// Video first when there is one, because an AVI carries the sound too and
    /// is therefore the whole of what the patch does. A PNG follows it wherever
    /// there is a picture: it is the same picture, stopped — one frame at the
    /// moment on screen, and the one kind here that ignores the length entirely.
    /// <para>
    /// A patch that draws nothing is offered neither, and one that makes no
    /// sound is offered no WAV, so the dialog can never produce a file that is
    /// only a black rectangle or only silence. Empty means there is nothing to
    /// write at all.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<FilePickerFileType> ExportKinds(Patch patch)
    {
        var (picture, sound) = patch.Reaches();

        return (picture, sound) switch
        {
            (true, true) => [Avi, Png, Wav],
            (true, false) => [Avi, Png],
            (false, true) => [Wav],
            _ => [],
        };
    }

    /// <summary>
    /// The kinds a recording could be written to. The same question
    /// <see cref="ExportKinds"/> answers, asked about a take rather than a
    /// render.
    /// </summary>
    /// <remarks>
    /// No PNG, because a still is not a recording — there is nothing about one
    /// moment that needs the performance to be running. Otherwise the rule is the
    /// export's rule: a patch that draws nothing is offered no video and one that
    /// makes no sound is offered no WAV, so a take can never come out as a black
    /// rectangle or as silence.
    /// <para>
    /// A patch that draws but makes no sound is still offered an AVI, and that
    /// AVI simply has no audio stream. It is a recording of everything the patch
    /// does, which is the test — a silent one is only wrong when there was sound
    /// to be had.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<FilePickerFileType> RecordKinds(Patch patch)
    {
        var (picture, sound) = patch.Reaches();

        return (picture, sound) switch
        {
            (true, true) => [Avi, Wav],
            (true, false) => [Avi],
            (false, true) => [Wav],
            _ => [],
        };
    }

    /// <summary>
    /// Writes the patch to a file. One button and one dialog for both kinds,
    /// because which kind you want is the same decision as what to call it —
    /// and the dialog offers only the kinds this patch actually has, so a silent
    /// patch is never offered a WAV of silence.
    /// </summary>
    /// <remarks>
    /// Unlike every other export here a video takes long enough to watch, so it
    /// reports as it goes and can be stopped: rendering a minute of an expensive
    /// patch is minutes of work, and a program that merely appears to have hung
    /// during it is not acceptable.
    /// </remarks>
    private async Task ExportAsync()
    {
        var patch = editor.Patch;
        var kinds = ExportKinds(patch);

        if (kinds.Count == 0)
        {
            Report("Nothing is wired into the Output, so there is nothing to write.");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export",
            SuggestedFileName = "flyback",

            // The first kind offered is the one the patch is most fully
            // described by, so it is also the extension a name gets by default.
            DefaultExtension = kinds[0].Patterns is ["*.wav"] ? "wav" : "avi",
            FileTypeChoices = [.. kinds],
        });

        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (path is null)
        {
            Report("That location can't be written to directly.");
            return;
        }

        // The name decides, because the name is what the person actually chose
        // — a dialog's selected filter is not carried back on every platform,
        // and the extension is.
        var kind = Path.GetExtension(path);

        if (kind.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            await ExportSoundAsync(patch, path);
        else if (kind.Equals(".png", StringComparison.OrdinalIgnoreCase))
            await ExportFrameAsync(path);
        else
            await ExportPictureAsync(patch, path);
    }

    /// <summary>
    /// One frame, at the moment the preview is showing, at export size rather
    /// than at preview size.
    /// </summary>
    /// <remarks>
    /// The only export that finishes before the button could become Stop, so it
    /// neither starts a run nor reports a length: what is written is what was on
    /// screen when it was asked for, and the seconds on the panel mean nothing
    /// to it.
    /// <para>
    /// Rendered afresh rather than lifted off the preview, which is why it is
    /// full size whatever Size says — and why feedback, which is a frame's
    /// memory of the one before, comes out of a still as a single pass with
    /// nothing behind it.
    /// </para>
    /// </remarks>
    private async Task ExportFrameAsync(string path)
    {
        try
        {
            await PreviewSurface.SaveFrameAsync(preview.Program, preview.Time, path, ExportSize);
        }
        catch (Exception ex)
        {
            Report($"Could not save the frame: {ex.Message}");
        }
    }

    private async Task ExportSoundAsync(Patch patch, string path)
    {
        var seconds = ExportSeconds;

        try
        {
            await Task.Run(() => RenderAudioFile(patch, path, seconds, samples));
            Report($"Wrote {seconds:0.0}s to {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            Report($"Could not render audio: {ex.Message}");
        }
    }

    private async Task ExportPictureAsync(Patch patch, string path)
    {
        // Everything the background pass needs, taken while the patch is still
        // sitting still. An export is a picture of the patch as it is now, so
        // editing during one changes the next export and not this one.
        var size = preview.Resolution;
        var seconds = ExportSeconds;
        var settings = new MovieSettings(size.Width, size.Height, seconds);

        var videoPatch = patch.CompileForVideo(samples: samples).Program;
        var soundPatch = patch.Reaches().Sound ? patch.CompileForAudio(samples: samples).Program : null;
        var scan = AudioScan.For(patch, ExportAspect);

        using var stopping = new CancellationTokenSource();
        export = stopping;
        exportButton.Content = "Stop";
        length.IsEnabled = false;

        var progress = new Progress<double>(done => Report(
            $"Exporting {seconds:0}s at {size.Width} × {size.Height} — {done:P0}",
            progress: true));

        try
        {
            var written = await Task.Run(
                () => MovieRenderer.Render(path, videoPatch, soundPatch, scan, settings, progress, stopping.Token),
                stopping.Token);

            var duration = written / settings.FramesPerSecond;

            Report(written < settings.FrameCount
                ? $"Stopped — kept the {duration:0.0}s already rendered in {Path.GetFileName(path)}."
                : $"Wrote {duration:0.0}s to {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            Report($"Could not export video: {ex.Message}");
        }
        finally
        {
            export = null;
            exportButton.Content = "Export…";
            length.IsEnabled = true;

            // The patch may have been edited while this ran, so what there is
            // to write is asked again rather than assumed to be what it was.
            MarkExportable();
        }
    }

    /// <summary>The length control, as the number an export actually wants.</summary>
    private double ExportSeconds => (double)(length.Value ?? 10m);

    /// <summary>
    /// Renders offline through a fresh renderer, so exporting never disturbs the
    /// cursor or filter state of whatever is currently playing.
    /// </summary>
    private static void RenderAudioFile(
        Patch patch,
        string path,
        double seconds,
        ISampleLibrary? samples)
    {
        var program = patch.CompileForAudio(samples: samples).Program;
        var renderer = new AudioRenderer();
        var frames = (int)Math.Round(renderer.SampleRate * seconds);
        var buffer = new float[frames * NodeCatalog.AudioChannels];
        renderer.Render(program, buffer, AudioScan.For(patch, ExportAspect));

        WavWriter.Write(path, buffer, renderer.SampleRate, NodeCatalog.AudioChannels);
    }

    private static FilePickerFileType PatchFileType => new("Flyback patch")
    {
        Patterns = [$"*.{PatchIo.FileExtension}"],
    };
}
