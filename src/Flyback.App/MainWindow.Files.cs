using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.Core;
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
            FileTypeFilter = [PatchFileType, BundleFileType],
        });

        if (files.Count == 0) return;

        if (Bundled(files[0]))
        {
            await OpenBundleAsync(files[0]);
            return;
        }

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
            soundFolder.Beside = Path.GetDirectoryName(files[0].TryGetLocalPath());
            pictureFolder.Beside = soundFolder.Beside;

            // Whatever bundle was open is not open any more. A loose patch is
            // backed by a folder, and leaving the last one's files in front of
            // that folder would answer for paths this document knows nothing
            // about.
            carried = null;
            bundled = false;

            // Before the patch and not after: setting it is what tells the
            // window to draw the title again, and a name arriving a line later
            // would be a title bar one edit out of date.
            patchName = Path.GetFileNameWithoutExtension(files[0].Name);

            editor.Patch = loaded.Patch;
            preview.Rewind();
        }
        catch (Exception ex)
        {
            Report($"Could not open patch: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the patch and everything it names into one file, which is a save
    /// like any other.
    /// </summary>
    /// <remarks>
    /// A bundle is a document rather than a copy of one: writing it marks the
    /// patch saved, takes the name in the title bar, and is what the question
    /// about unsaved changes accepts as an answer. The two kinds of file differ
    /// in what is in them and in nothing else.
    /// <para>
    /// A bundle already open is written out of what it is carrying rather than
    /// out of the disk, so saving one that was never unpacked writes the same
    /// bytes back — a photograph is not re-encoded on its way through, which
    /// would quietly make a sixteen-bit file an eight-bit one.
    /// </para>
    /// </remarks>
    private async Task<bool> SaveBundleAsync(IStorageFile file)
    {
        try
        {
            BundleReport report;

            // Into memory first: a zip is written by seeking back over its own
            // directory, and what a picker hands back cannot always be seeked.
            using var packed = new MemoryStream();

            report = PatchBundle.Write(packed, editor.Patch, Bytes, plugins.Modules);

            packed.Position = 0;

            await using (var stream = await file.OpenWriteAsync()) await packed.CopyToAsync(stream);

            patchName = Path.GetFileNameWithoutExtension(file.Name);
            bundled = true;
            editor.MarkSaved();

            Report(report.Whole
                ? $"Saved {file.Name}, carrying {report.Carried.Count} file(s)."
                : $"Saved {file.Name}, without {report.Missing.Count} file(s) that could not be read.",
                report.Whole ? null : string.Join(Environment.NewLine, report.Missing));

            return true;
        }
        catch (Exception ex)
        {
            Report($"Could not write bundle: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes what an open bundle is carrying into <paramref name="folder"/>,
    /// under the names the patch already calls them by, and stops being a bundle.
    /// </summary>
    /// <remarks>
    /// What saving a bundle as a loose patch has to do, and the exact inverse of
    /// packing: the paths in the patch are the archive's own names, they are
    /// relative, and a relative path is measured from beside the patch — so
    /// writing them there is all it takes for the saved document to work.
    /// <para>
    /// Only what the patch still names. A bundle may be carrying a picture whose
    /// module has since been deleted, and spilling that onto somebody's disk
    /// would be leaving litter behind a save they did not ask about.
    /// </para>
    /// <para>
    /// Nothing is overwritten. A file already there is one somebody put there,
    /// and the copy in the bundle is not automatically the better of the two —
    /// so the patch goes on naming what is on the disk, which is what it would
    /// have read anyway.
    /// </para>
    /// </remarks>
    /// <returns>How many files were written.</returns>
    private int Scatter(string folder)
    {
        if (carried is not { } held) return 0;

        var written = 0;

        foreach (var path in PatchBundle.Files(editor.Patch, plugins.Modules))
        {
            if (Path.IsPathRooted(path) || !held.Bytes.TryGetValue(path, out var bytes)) continue;

            var into = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(into)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(into)!);
            File.WriteAllBytes(into, bytes);

            written++;
        }

        // A document backed by a folder from here on. What it names is on the
        // disk now, and the copies in memory would only be a second answer to
        // the same question.
        carried = null;
        bundled = false;

        return written;
    }

    /// <summary>
    /// The bytes of a file the patch names: out of the bundle that is open where
    /// it holds one, and off the disk where it does not.
    /// </summary>
    /// <remarks>
    /// The same order the libraries look in, so what is packed is what the
    /// picture was actually drawn from — a bundle cannot come out holding a file
    /// nothing was reading.
    /// </remarks>
    private byte[]? Bytes(string path)
    {
        if (carried is { } held && held.Bytes.TryGetValue(path, out var bytes)) return bytes;

        try
        {
            var full = Path.IsPathRooted(path) || soundFolder.Beside is not { Length: > 0 } folder
                ? path
                : Path.Combine(folder, path);

            return File.Exists(full) ? File.ReadAllBytes(full) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Whether a file the picker handed back is a bundle rather than a patch.</summary>
    private static bool Bundled(IStorageFile file) =>
        file.Name.EndsWith(PatchBundle.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Opens a bundle by unpacking it into a folder beside itself and opening
    /// what comes out.
    /// </summary>
    /// <remarks>
    /// Unpacked rather than read where it lies, which the command line does
    /// instead — and the two are right for opposite reasons. The command line
    /// draws a bundle and writes nothing; this is where somebody is going to
    /// change the patch, so the files it names have to be files they can find,
    /// replace and save beside. Reading it into memory would mean a document
    /// whose pictures vanish the first time it is saved anywhere.
    /// <para>
    /// Nothing is written anywhere, which is the whole of what makes a bundle a
    /// document here rather than an archive to be spilled onto the disk first.
    /// The files are held as they came — see <see cref="carried"/> — and the
    /// folder libraries stay behind them, so a module pointed at something on
    /// this machine a moment later means the thing on this machine.
    /// </para>
    /// <para>
    /// Where a loose patch is opened from is left alone on purpose. A bundle has
    /// no folder to measure a relative path from, and the paths inside one are
    /// the archive's own names, so nothing about this document is measured from
    /// anywhere.
    /// </para>
    /// </remarks>
    private async Task OpenBundleAsync(IStorageFile file)
    {
        try
        {
            LoadedBundle bundle;

            await using (var reading = await file.OpenReadAsync())
            {
                // Copied out of the stream first, because a zip is read by
                // seeking about in it and what a picker hands back is not always
                // something that can be.
                using var whole = new MemoryStream();

                await reading.CopyToAsync(whole);
                whole.Position = 0;

                bundle = PatchBundle.Read(whole, plugins.Modules);
            }

            carried = new BundleFiles(bundle.Files, soundFolder, pictureFolder);
            bundled = true;

            patchName = Path.GetFileNameWithoutExtension(file.Name);

            editor.Patch = bundle.Patch;
            preview.Rewind();

            Report(bundle.Files.Count == 0
                ? $"Opened {file.Name}."
                : $"Opened {file.Name}, carrying {bundle.Files.Count} file(s).");
        }
        catch (Exception ex)
        {
            Report($"Could not open bundle: {ex.Message}");
        }
    }

    /// <returns>Whether a file was written. A cancelled picker is not one.</returns>
    private async Task<bool> SavePatchAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save patch",
            // What it is called now, or "patch" for one nobody has named — the
            // dialog is where a name is chosen, so it is where the one already
            // chosen belongs.
            SuggestedFileName = patchName ?? "patch",

            // Whichever kind this document already is. A bundle saved again
            // should stay one without anybody having to type the extension.
            DefaultExtension = bundled ? PatchBundle.Extension[1..] : PatchIo.FileExtension,
            FileTypeChoices = bundled
                ? [BundleFileType, PatchFileType]
                : [PatchFileType, BundleFileType],
        });

        if (file is null) return false;

        // The name decides which, the way it does for an export.
        if (Bundled(file)) return await SaveBundleAsync(file);

        try
        {
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(PatchIo.ToJson(editor.Patch));
            }

            // A patch saved somewhere new measures its samples from there now,
            // which is what lets one be written beside the sounds it names.
            var folder = Path.GetDirectoryName(file.TryGetLocalPath());

            // And a bundle saved as a loose patch has to put what it was
            // carrying where the patch now says it is, or what has just been
            // written names files that exist nowhere. The inverse of packing,
            // and the one place saving writes more than the file it was given.
            var spilled = folder is { Length: > 0 } ? Scatter(folder) : 0;

            // Only once it is actually on disk. A patch that failed to write is
            // still a patch with everything to lose.
            patchName = Path.GetFileNameWithoutExtension(file.Name);
            editor.MarkSaved();

            soundFolder.Beside = folder;
            pictureFolder.Beside = folder;
            Recompile();

            if (spilled > 0) Report($"Saved {file.Name}, and {spilled} file(s) beside it.");

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
            SuggestedFileName = "flyback", // todo, change to name of patch

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
            await Task.Run(() => RenderAudioFile(patch, path, seconds, Sounds));
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

        var videoPatch = patch.CompileForVideo(samples: Sounds, pictures: Pictures).Program;
        var soundPatch = patch.Reaches().Sound ? patch.CompileForAudio(samples: Sounds).Program : null;
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

    private static FilePickerFileType PatchFileType => new($"{GlobalConstants.ApplicationName} patch")
    {
        Patterns = [$"*.{PatchIo.FileExtension}"],
    };

    /// <summary>
    /// A patch and everything it names, in one file — see
    /// <see cref="PatchBundle"/>. Offered beside the patch rather than instead
    /// of it: a bundle is what you send somebody, and a patch is what you work
    /// on.
    /// </summary>
    private static FilePickerFileType BundleFileType => new($"{GlobalConstants.ApplicationName} bundle")
    {
        Patterns = [$"*{PatchBundle.Extension}"],
    };
}
