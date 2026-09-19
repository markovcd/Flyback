using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Flyback.App.Assist;
using Flyback.App.Controls;
using Flyback.App.Statistics;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Core.Render;

namespace Flyback.App;

/// <summary>
/// Everything the window writes or reads: patches, and a live recording. The
/// engine does the work in every case — these are the file pickers around it,
/// the progress on the button, and the one decision the pickers cannot make
/// for themselves, which is what a patch has to offer. A deterministic render
/// of a frozen patch is <c>flyback-cli render</c>'s job — ADR-0078.
/// </summary>
public sealed partial class MainWindow
{
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

        // Where the last document's knobs were left says nothing about this one's.
        controls.Forget();
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

        usage.Count(Used.Saved);
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
        var all = new FilePickerFileType(GlobalConstants.ApplicationName)
        {
            Patterns = [.. OpenKinds().SelectMany(o => o.Patterns ?? [])]
        };
        
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open patch",
            AllowMultiple = false,
            FileTypeFilter = [all, .. OpenKinds()],
        });
        
        
        if (files.Count == 0) return;

        await OpenFileAsync(files[0]);
    }

    /// <summary>
    /// Opens a file handed back by any of the routes that produce one — a
    /// picker, a drop from the file explorer, a path named on the command
    /// line, or a file macOS hands the program through an activation — so the
    /// extension decides which kind it is exactly as it does for the picker.
    /// </summary>
    private async Task OpenFileAsync(IStorageFile file)
    {
        if (Bundled(file.Name))
        {
            await OpenBundleAsync(file);
            return;
        }

        if (Sourced(file.Name))
        {
            await OpenSourceAsync(file);
            return;
        }

        try
        {
            await using var stream = await file.OpenReadAsync();
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
                Path.GetFileNameWithoutExtension(file.Name),
                Path.GetDirectoryName(file.TryGetLocalPath()));

            // Whatever preset the list still showed is not this patch.
            ClearPresetSelection();

            usage.Count(Used.Opened);

            editor.Patch = loaded.Patch;
            RewindToZero();

            // Whatever was said about this patch was kept beside it, if anything
            // was and the file is still what it was saved as — ADR-0072.
            assistant?.Open(ConversationFor(file, text));

            // A patch file is the document, so the graph owns it — ADR-0068.
            DropSource();
        }
        catch (Exception ex)
        {
            Report($"Could not open patch: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens a file already sitting on disk rather than one a picker handed
    /// back — named on the command line when the program started, or resolved
    /// from a plain path some other way. A drop from the file explorer skips
    /// this: it hands back an <see cref="IStorageFile"/> of its own, which is
    /// what <see cref="OpenFileAsync"/> already takes.
    /// </summary>
    private async Task OpenPathAsync(string path)
    {
        IStorageFile? file;

        try
        {
            file = await StorageProvider.TryGetFileFromPathAsync(path);
        }
        catch (Exception ex)
        {
            Report($"Could not open {Path.GetFileName(path)}: {ex.Message}");
            return;
        }

        if (file is null)
        {
            Report($"Could not open {Path.GetFileName(path)}.");
            return;
        }

        await OpenFileAsync(file);
    }

    /// <summary>
    /// Lets a patch, a bundle or a text file be opened by dropping it in from
    /// the file explorer — the same three kinds <see cref="OpenPatchAsync"/>
    /// offers through a picker, arriving without one.
    /// </summary>
    private void WireFileDrop()
    {
        DragDrop.SetAllowDrop(this, true);

        // Refused under a dialog, and shown as refused, for the reason
        // OpenActivatedFileAsync gives.
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) && !this.HasDialogUp
                ? DragDropEffects.Copy
                : DragDropEffects.None);

        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            // Only the first: one window holds one patch, and a picker never
            // offers more than that either — see AllowMultiple above.
            if (e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault() is not { } file) return;

            e.Handled = true;

            await OpenActivatedFileAsync(file);
        });
    }

    /// <summary>
    /// Opens a file handed to the program from outside a picker or a drop —
    /// which on macOS is how "open this file" arrives at all: Finder delivers
    /// it as an activation rather than as a command-line argument, whether
    /// that launches the program or lands on its Dock icon while it is
    /// already running. See <see cref="FlybackApp.OnFrameworkInitializationCompleted"/>.
    /// </summary>
    /// <remarks>
    /// Not while a dialog is up. A dialog stops the pointer and the keyboard and
    /// neither of these arrives by them, so with nothing unsaved to ask about the
    /// document would be replaced behind the sheet — and a text one takes the
    /// focus with it, out of a dialog that then no longer hears Escape.
    /// </remarks>
    internal async Task OpenActivatedFileAsync(IStorageFile file)
    {
        if (this.HasDialogUp)
        {
            Report($"{file.Name} was not opened: there is a dialog to answer first.");
            return;
        }

        if (await MayReplaceThePatchAsync()) await OpenFileAsync(file);
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
    /// in front — a bundle or a text saved again should stay one without anybody
    /// having to type the extension.
    /// </summary>
    /// <param name="sourced">
    /// Whether the text is the document. It is then the one kind that loses
    /// nothing: a patch or a bundle written from it drops its comments, names and
    /// defs.
    /// </param>
    internal static IReadOnlyList<FilePickerFileType> SaveKinds(bool bundled, bool sourced = false) =>
        sourced ? [SourceFileType, PatchFileType, BundleFileType]
        : bundled ? [BundleFileType, PatchFileType, SourceFileType]
        : [PatchFileType, BundleFileType, SourceFileType];

    /// <summary>The extension a save offers, which is the first of <see cref="SaveKinds"/>.</summary>
    internal static string SaveExtension(bool bundled, bool sourced = false) =>
        sourced ? PatchLanguage.FileExtension
        : bundled ? PatchBundle.Extension[1..]
        : PatchIO.FileExtension;

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
                // What a bundle was carrying goes beside the text, and the text
                // measures its files from there now — both for the reason a save
                // as a patch file does them (ADR-0060): what has just been written
                // names those files, and they were nowhere but in memory.
                var folder = Path.GetDirectoryName(file.TryGetLocalPath());
                var spilled = folder is { Length: > 0 } ? Scatter(folder) : 0;

                SavedAs(Path.GetFileNameWithoutExtension(file.Name), asBundle: false);
                MarkSourceSaved();

                // The text is the document, so the conversation is kept beside
                // it as it would be beside a patch file. A printing is a copy,
                // and a copy takes nothing with it — ADR-0072.
                KeepConversation(file, written);

                soundFolder.Beside = folder;
                pictureFolder.Beside = folder;
                Recompile();

                Report(spilled > 0
                    ? $"Saved {file.Name}, and {spilled} file(s) beside it."
                    : $"Saved {file.Name}.");

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

            // A copy saves nothing, so whatever asked for a save has not had one.
            // The unsaved-changes question is what reads this, and going ahead
            // on the strength of a printing would shut the only whole patch
            // there is — with its groups, which the printing has just dropped.
            return !SomethingToLose;
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

            usage.Count(Used.Opened);

            editor.Patch = load.Patch;
            RewindToZero();

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

            // Refused for the reason a loose patch is, and before anything says
            // this is the document: opened with holes in it — or empty, which is
            // how a later version's reads — it would take the bundle's name, and
            // the next save would write that over the real one.
            if (bundle.Load is { IsComplete: false } lacking)
            {
                Report($"Not opened. {lacking.Summary}", lacking.Detail);
                return;
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

            usage.Count(Used.Opened);

            editor.Patch = bundle.Patch;
            RewindToZero();
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

    /// <returns>Whether the document was saved. A cancelled picker is not a save, and nor is a copy.</returns>
    private async Task<bool> SavePatchAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save patch",
            // What it is called now, or "patch" for one nobody has named — the
            // dialog is where a name is chosen, so it is where the one already
            // chosen belongs.
            SuggestedFileName = patchName ?? "patch",

            // Whichever kind this document already is — see SaveKinds.
            DefaultExtension = SaveExtension(bundled, sourceOwned),
            FileTypeChoices = SaveKinds(bundled, sourceOwned),
        });

        return file is not null && await SaveToAsync(file);
    }

    /// <summary>Writes the document to a file the picker handed back, as the kind its name says.</summary>
    /// <returns>
    /// Whether the document was saved — which writing a file is, except where the
    /// file is a printing of a patch the graph owns: that is a copy, and leaves
    /// whatever was unsaved as unsaved as it was.
    /// </returns>
    internal async Task<bool> SaveToAsync(IStorageFile file)
    {
        if (Sourced(file.Name)) return await SaveSourceAsync(file);

        // Either of the other two hands the patch to the graph and empties the
        // text, so text that is nowhere else is asked about first — ADR-0068.
        if (!await MayLoseTheTextToAsync(file.Name)) return false;

        if (Bundled(file.Name)) return await SaveBundleAsync(file);

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

    /// <summary>One format as the file picker asks about it.</summary>
    private static FilePickerFileType Kind(ClipFormat format) =>
        new(format.Label) { Patterns = [$"*{format.Extension}"] };

    /// <summary>
    /// The kinds a recording could be written to.
    /// </summary>
    /// <remarks>
    /// No PNG, because a still is not a recording — ADR-0078 leaves stills to
    /// <c>flyback-cli render</c>. A patch that draws but makes no sound is still
    /// offered a video, which simply has no audio stream: it is a recording of
    /// everything the patch does, and a silent one is only wrong when there was
    /// sound to be had.
    /// <para>
    /// One kind each rather than every format there is. The two the settings
    /// window is set to are what this offers, since a picker listing nine
    /// extensions would be a second place to choose a format and a slower way to
    /// do it — and an extension typed over the suggestion is honoured anyway
    /// (ADR-0089).
    /// </para>
    /// </remarks>
    /// <param name="video">
    /// The format a take with a picture is written as, defaulting to the one
    /// written here — which is also what a window with no settings file has.
    /// </param>
    /// <param name="sound">The format a take of the sound alone is written as.</param>
    internal static IReadOnlyList<FilePickerFileType> RecordKinds(
        Patch patch,
        ClipFormat? video = null,
        ClipFormat? sound = null)
    {
        var (picture, heard) = patch.Reaches();

        var movie = Kind(video ?? ClipFormats.MotionJpegAvi);
        var track = Kind(sound ?? ClipFormats.Wav);

        return (picture, heard) switch
        {
            (true, true) => [movie, track],
            (true, false) => [movie],
            (false, true) => [track],
            _ => [],
        };
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
    /// three for a document the graph owns: a patch and a bundle are what that is
    /// saved as, and offering the lossy one first would put it where the habit lands.
    /// </summary>
    private static FilePickerFileType SourceFileType => new($"{GlobalConstants.ApplicationName} text")
    {
        Patterns = [$"*.{PatchLanguage.FileExtension}"],
    };
}
