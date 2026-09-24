using Avalonia.Input;
using Avalonia.Platform.Storage;
using Flyback.App.Assist;
using Flyback.App.Controls;
using Flyback.Plugins.Hosting;
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
        knobs.Hub.Forget();
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

    /// <summary>
    /// The Open gesture whole: what is unsaved is asked about, and then the
    /// picker. The toolbar's button and Ctrl+O both come through here, so the
    /// question cannot be stepped round by reaching for the keyboard.
    /// </summary>
    private async Task OpenAnotherPatchAsync()
    {
        if (await MayReplaceThePatchAsync()) await OpenPatchAsync();
    }

    private async Task OpenPatchAsync()
    {
        var all = new FilePickerFileType(GlobalConstants.ApplicationName)
        {
            Patterns = [.. PatchFileKinds.OpenKinds().SelectMany(o => o.Patterns ?? [])]
        };
        
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open patch",
            AllowMultiple = false,
            FileTypeFilter = [all, .. PatchFileKinds.OpenKinds()],
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
        if (PluginPackage.Named(file.Name))
        {
            await pluginInstalls.InstallAsync(file);
            return;
        }

        if (PatchFileKinds.Bundled(file.Name))
        {
            await OpenBundleAsync(file);
            return;
        }

        if (PatchFileKinds.Sourced(file.Name))
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
                await OfferMissingPluginsAsync(loaded, new Reopen(Path: file.TryGetLocalPath()));
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
            document.DropSource();
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

        if (PluginPackage.Named(file.Name) || await MayReplaceThePatchAsync()) await OpenFileAsync(file);
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
            document.DropSource();

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
            if (!held.Bytes.TryGetValue(path, out var bytes)) continue;
            if (PatchPaths.Inside(folder, path) is not { } into || File.Exists(into)) continue;

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

        return PatchPaths.Carriable(path, soundFolder.Beside);
    }

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
        var written = document.Owned ? document.Text : PatchPrinter.Print(editor.Patch);

        try
        {
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(written);
            }

            if (document.Owned)
            {
                // What a bundle was carrying goes beside the text, and the text
                // measures its files from there now — both for the reason a save
                // as a patch file does them (ADR-0060): what has just been written
                // names those files, and they were nowhere but in memory.
                var folder = Path.GetDirectoryName(file.TryGetLocalPath());
                var spilled = folder is { Length: > 0 } ? Scatter(folder) : 0;

                SavedAs(Path.GetFileNameWithoutExtension(file.Name), asBundle: false);
                document.MarkSourceSaved();

                // The text is the document, so the conversation is kept beside
                // it as it would be beside a patch file. A printing is a copy,
                // and a copy takes nothing with it — ADR-0072.
                KeepConversation(file, written);

                soundFolder.Beside = folder;
                pictureFolder.Beside = folder;
                playback.Recompile();

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
            document.TakeSource(text);

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
                await OfferMissingPluginsAsync(lacking, new Reopen(Path: file.TryGetLocalPath()));
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
            document.DropSource();

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

    /// <returns>Whether the document was saved. A canceled picker is not a save, and nor is a copy.</returns>
    private async Task<bool> SavePatchAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save patch",
            // What it is called now, or "patch" for one nobody has named — the
            // dialog is where a name is chosen, so it is where the one already
            // chosen belongs.
            SuggestedFileName = patchName ?? "patch",

            // Whichever kind this document already is — see PatchFileKinds.SaveKinds.
            DefaultExtension = PatchFileKinds.SaveExtension(bundled, document.Owned),
            FileTypeChoices = PatchFileKinds.SaveKinds(bundled, document.Owned),
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
        if (PatchFileKinds.Sourced(file.Name)) return await SaveSourceAsync(file);

        // Either of the other two hands the patch to the graph and empties the
        // text, so text that is nowhere else is asked about first — ADR-0068.
        if (!await MayLoseTheTextToAsync(file.Name)) return false;

        if (PatchFileKinds.Bundled(file.Name)) return await SaveBundleAsync(file);

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
            document.DropSource();

            soundFolder.Beside = folder;
            pictureFolder.Beside = folder;
            playback.Recompile();

            if (spilled > 0) Report($"Saved {file.Name}, and {spilled} file(s) beside it.");

            return true;
        }
        catch (Exception ex)
        {
            Report($"Could not save patch: {ex.Message}");
            return false;
        }
    }
}
