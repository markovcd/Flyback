using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Flyback.App.Assist;
using Flyback.App.Controls;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
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
    /// question the dialog would have asked a moment later.
    /// </summary>
    /// <remarks>
    /// An export already running keeps it enabled whatever the patch says: the
    /// button is <c>Stop</c> by then, and editing mid-render must not take away the
    /// only way to abandon it.
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

    /// <summary>
    /// A different document from here on: what it is called, where the files it
    /// names are measured from, and the bundle it arrived in if it arrived in one.
    /// </summary>
    /// <remarks>
    /// Five routes reach a new patch and every one has to say all of this rather
    /// than the part it cares about: what a patch names is looked up in
    /// <see cref="carried"/> first, so a preset picked while a bundle was open went
    /// on reading that bundle's sounds. Call it before handing the patch to the
    /// canvas, since setting the patch is what redraws the title.
    /// </remarks>
    /// <param name="name">What the title bar says, and null for a document with no name.</param>
    /// <param name="beside">
    /// The folder a relative sample or picture path is measured from. Null where
    /// there is none, which is what a preset has.
    /// </param>
    /// <param name="files">What a bundle brought with it, and null for everything else.</param>
    internal void Became(string? name, string? beside, BundleFiles? files = null)
    {
        patchName = name;

        carried = files;
        bundled = files is not null;

        soundFolder.Beside = beside;
        pictureFolder.Beside = beside;
    }

    /// <summary>
    /// Whether the document is a bundle, which is what the next save offers first.
    /// Readable from the tests, as <see cref="Became"/> is callable from them:
    /// every route that opens a document is behind a file picker the headless
    /// platform does not put up.
    /// </summary>
    internal bool IsBundle => bundled;

    /// <summary>
    /// What was open has just been written, and the file it was written to is the
    /// document now.
    /// </summary>
    /// <remarks>
    /// Deliberately smaller than <see cref="Became"/>: a save changes what the
    /// patch is called and which kind of file it is and nothing else — what it
    /// carries is still the only copy of those files.
    /// </remarks>
    /// <param name="asBundle">Which kind of file it went to, which is what the next save offers.</param>
    private void SavedAs(string name, bool asBundle)
    {
        patchName = name;
        bundled = asBundle;

        // Whatever preset the list still showed, the patch just took on a file
        // of its own — the preset is where it started, not what it is now.
        ClearPresetSelection();

        editor.MarkSaved();
    }

    /// <summary>
    /// The conversations kept for patch files — a bundle keeps its own inside it
    /// (ADR-0072).
    /// </summary>
    private readonly ConversationStore conversations = new();

    /// <summary>
    /// The conversation kept for a patch file that has just been read as
    /// <paramref name="text"/>, or null — including for a file with no path on this
    /// machine, which has nothing to be found by.
    /// </summary>
    private string? ConversationFor(IStorageFile file, string text) =>
        file.TryGetLocalPath() is { } path ? conversations.Find(path, text) : null;

    /// <summary>
    /// Puts the conversation about this patch beside the file it has just been
    /// written to as <paramref name="written"/>, or forgets whatever was kept for
    /// that file before.
    /// </summary>
    /// <remarks>
    /// The conversation counts as saved whatever happens here. The patch is on disk,
    /// and saving it again would meet the same refusal — so what is said instead is
    /// that the conversation did not go with it.
    /// </remarks>
    private void KeepConversation(IStorageFile file, string written)
    {
        var conversation = assistant?.ConversationToSave();
        var path = file.TryGetLocalPath();

        var kept = path is null ? conversation is null : conversations.Keep(path, written, conversation);

        assistant?.ConversationSaved();

        if (!kept) Report($"Saved {file.Name}, but the conversation about it could not be kept with it.");
    }

    private async Task OpenPatchAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open patch",
            AllowMultiple = false,
            FileTypeFilter = OpenKinds(),
        });

        if (files.Count == 0) return;

        if (Bundled(files[0].Name))
        {
            await OpenBundleAsync(files[0]);
            return;
        }

        if (Sourced(files[0].Name))
        {
            await OpenSourceAsync(files[0]);
            return;
        }

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            var loaded = PatchIO.Read(text);

            // A patch short of a module would open with holes in it and compile
            // to something that is not what was saved. Better to refuse it and
            // say what is missing, leaving what is open where it was.
            if (!loaded.IsComplete)
            {
                Report($"Not opened. {loaded.Summary}", loaded.Detail);
                return;
            }

            // Everything that says which document this is, before the patch it is
            // about — see Became.
            Became(
                Path.GetFileNameWithoutExtension(files[0].Name),
                Path.GetDirectoryName(files[0].TryGetLocalPath()));

            // Whatever preset the list still showed is not this patch.
            ClearPresetSelection();

            editor.Patch = loaded.Patch;
            preview.Rewind();

            // Whatever was said about this patch was kept beside it, if anything
            // was and the file is still what it was saved as — ADR-0072.
            assistant?.Open(ConversationFor(files[0], text));

            // A patch file is the document, so the graph owns it — ADR-0068.
            DropSource();
        }
        catch (Exception ex)
        {
            Report($"Could not open patch: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the patch and everything it names into one file, which is a save like
    /// any other.
    /// </summary>
    /// <remarks>
    /// A bundle is a document rather than a copy of one: it marks the patch saved
    /// and takes the name in the title bar. A bundle already open is written out of
    /// what it is carrying rather than off the disk, so a photograph is not
    /// re-encoded on its way through.
    /// </remarks>
    private async Task<bool> SaveBundleAsync(IStorageFile file)
    {
        try
        {
            BundleReport report;

            // Into memory first: a zip is written by seeking back over its own
            // directory, and what a picker hands back cannot always be seeked.
            using var packed = new MemoryStream();

            // The conversation goes inside, since a bundle is the whole of the
            // document wherever it is taken — ADR-0072.
            report = PatchBundle.Write(
                packed, editor.Patch, Bytes, plugins.Modules, assistant?.ConversationToSave());

            packed.Position = 0;

            await using (var stream = await file.OpenWriteAsync()) await packed.CopyToAsync(stream);

            SavedAs(Path.GetFileNameWithoutExtension(file.Name), asBundle: true);
            assistant?.ConversationSaved();

            // Saved as a bundle, so a bundle is the document now and the graph
            // owns it — ADR-0068.
            DropSource();

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
    /// Writes what an open bundle is carrying into <paramref name="folder"/>, under
    /// the names the patch already calls them by, and stops being a bundle.
    /// </summary>
    /// <remarks>
    /// The exact inverse of packing: the paths in the patch are the archive's own
    /// names and are relative, so writing them beside the patch is all it takes.
    /// Only what the patch still names, since spilling a deleted module's picture
    /// onto somebody's disk is litter. Nothing is overwritten — a file already
    /// there is one somebody put there.
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
    /// The bytes of a file the patch names: out of the bundle that is open where it
    /// holds one, and off the disk where it does not. The same order the libraries
    /// look in, so a bundle cannot come out holding a file nothing was reading.
    /// </summary>
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

    /// <summary>Whether a name the picker handed back is a bundle rather than a patch.</summary>
    internal static bool Bundled(string name) =>
        name.EndsWith(PatchBundle.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a name the picker handed back is the patch written as text.</summary>
    internal static bool Sourced(string name) =>
        name.EndsWith($".{PatchLanguage.FileExtension}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What the save dialog offers, with whichever kind this document already is
    /// in front — a bundle saved again should stay one without anybody having to
    /// type the extension.
    /// </summary>
    internal static IReadOnlyList<FilePickerFileType> SaveKinds(bool bundled) => bundled
        ? [BundleFileType, PatchFileType, SourceFileType]
        : [PatchFileType, BundleFileType, SourceFileType];

    /// <summary>Everything the open dialog will read.</summary>
    internal static IReadOnlyList<FilePickerFileType> OpenKinds() =>
        [PatchFileType, BundleFileType, SourceFileType];

    /// <summary>
    /// Writes the patch as text, in the language.
    /// </summary>
    /// <remarks>
    /// Two saves in one method, and which it is depends on who owns the patch
    /// (ADR-0068). Where the text is the document this writes the text itself and
    /// is a save like any other. Where the graph is, it prints one instead — which
    /// is lossy, dropping the groups and laying the canvas out again (ADR-0065) —
    /// so it is a copy for reading, sending and diffing. What a printing does keep
    /// is the instrument exactly: the text builds back to the same program.
    /// </remarks>
    private async Task<bool> SaveSourceAsync(IStorageFile file)
    {
        var written = sourceOwned ? source.Source : PatchPrinter.Print(editor.Patch);

        try
        {
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(written);
            }

            if (sourceOwned)
            {
                SavedAs(Path.GetFileNameWithoutExtension(file.Name), asBundle: false);
                MarkSourceSaved();

                // The text is the document, so the conversation is kept beside
                // it as it would be beside a patch file. A printing is a copy,
                // and a copy takes nothing with it — ADR-0072.
                KeepConversation(file, written);

                Report($"Saved {file.Name}.");

                return true;
            }

            // Said only where there is something to have lost. A patch with no
            // groups in it loses nothing anybody would miss, and warning about
            // it every time would teach people to stop reading.
            var groups = editor.Patch.Groups?.Count ?? 0;

            Report(groups == 0
                ? $"Wrote {file.Name}. It is a copy: what is open is still {patchName ?? "the patch"}."
                : $"Wrote {file.Name}, without its {groups} group(s) — text has no place to keep them. "
                  + $"What is open is still {patchName ?? "the patch"}.");

            return true;
        }
        catch (Exception ex)
        {
            Report($"Could not write the text: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens a patch written as text, which is a patch like any other once it has
    /// been read. Refused whole where it does not read, with every complaint and
    /// the line each is on: half a patch is worse than none.
    /// </summary>
    private async Task OpenSourceAsync(IStorageFile file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);

            var text = await reader.ReadToEndAsync();
            var load = PatchLanguage.Build(text);

            if (!load.Ok)
            {
                Report($"Not opened. {file.Name} does not read.", load.Report);
                return;
            }

            Became(
                Path.GetFileNameWithoutExtension(file.Name),
                Path.GetDirectoryName(file.TryGetLocalPath()));

            // Whatever preset the list still showed is not this patch.
            ClearPresetSelection();

            editor.Patch = load.Patch;
            preview.Rewind();

            // The text is the document now, and the canvas is a view of it —
            // ADR-0068. Said by opening on it, because somebody who opened a
            // source file came to read or write source.
            TakeSource(text);

            // Kept beside the file, as for a patch file — ADR-0072.
            assistant?.Open(ConversationFor(file, text));

            Report($"Opened {file.Name}. The text is the document; the canvas shows what it builds.");
        }
        catch (Exception ex)
        {
            Report($"Could not open the text: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens a bundle by unpacking it into a folder beside itself and opening what
    /// comes out.
    /// </summary>
    /// <remarks>
    /// Unpacked rather than read where it lies, which the command line does
    /// instead: that draws a bundle and writes nothing, where this is somebody
    /// about to change the patch, so the files it names have to be files they can
    /// find and save beside. Nothing is written until they save — the files are
    /// held as they came, see <see cref="carried"/>. Where a loose patch is opened
    /// from is left alone, because a bundle has no folder to measure from.
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

            // A bundle answers for its own files first and falls through to the folder
            // it was opened from, which is why that folder is this one's rather than
            // whatever the last document left behind.
            Became(
                Path.GetFileNameWithoutExtension(file.Name),
                Path.GetDirectoryName(file.TryGetLocalPath()),
                new BundleFiles(bundle.Files, soundFolder, pictureFolder));

            // Whatever preset the list still showed is not this patch.
            ClearPresetSelection();

            editor.Patch = bundle.Patch;
            preview.Rewind();
            DropSource();

            // A bundle carries its conversation inside it — ADR-0072.
            assistant?.Open(bundle.Conversation);

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
            DefaultExtension = bundled ? PatchBundle.Extension[1..] : PatchIO.FileExtension,
            FileTypeChoices = SaveKinds(bundled),
        });

        if (file is null) return false;

        // The name decides which, the way it does for an export.
        if (Bundled(file.Name)) return await SaveBundleAsync(file);
        if (Sourced(file.Name)) return await SaveSourceAsync(file);

        try
        {
            var written = PatchIO.ToJson(editor.Patch);

            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(written);
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
            SavedAs(Path.GetFileNameWithoutExtension(file.Name), asBundle: false);

            // And what was said about it, beside it — ADR-0072.
            KeepConversation(file, written);

            // Saved as a patch file, so that is the document now — ADR-0068.
            DropSource();

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
    /// Video first when there is one, because an AVI carries the sound too; a PNG
    /// follows wherever there is a picture, being the same picture stopped. A patch
    /// that draws nothing is offered neither and one that makes no sound is offered
    /// no WAV, so the dialog can never produce a black rectangle or silence.
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
    /// <see cref="ExportKinds"/> answers, asked about a take rather than a render.
    /// </summary>
    /// <remarks>
    /// No PNG, because a still is not a recording. A patch that draws but makes no
    /// sound is still offered an AVI, which simply has no audio stream: it is a
    /// recording of everything the patch does, and a silent one is only wrong when
    /// there was sound to be had.
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
    /// because which kind you want is the same decision as what to call it — and
    /// only the kinds this patch has are offered.
    /// </summary>
    /// <remarks>
    /// Unlike every other export a video takes long enough to watch, so it reports
    /// as it goes and can be stopped.
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
    /// One frame, at the moment the preview is showing, at export size rather than
    /// at preview size.
    /// </summary>
    /// <remarks>
    /// The only export that finishes before the button could become Stop, so it
    /// neither starts a run nor reports a length. Rendered afresh rather than
    /// lifted off the preview, which is why feedback comes out of a still as a
    /// single pass with nothing behind it.
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
        Patterns = [$"*.{PatchIO.FileExtension}"],
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

    /// <summary>
    /// The patch written in the language — text, and readable as text. Last of the
    /// three in every list: a patch and a bundle are what a document is saved as,
    /// and offering the lossy one first would put it where the habit lands.
    /// </summary>
    private static FilePickerFileType SourceFileType => new($"{GlobalConstants.ApplicationName} text")
    {
        Patterns = [$"*.{PatchLanguage.FileExtension}"],
    };
}
