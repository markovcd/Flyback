using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Flyback.App.Assist;
using Flyback.App.Controls;
using Flyback.App.Statistics;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// Which file the patch is: what it is called, whether it is a bundle, what it
/// carries and where the files it names are looked for — and opening and saving
/// patches, bundles and text (ADR-0148).
/// </summary>
/// <remarks>
/// The engine does the work in every case; these are the file pickers around it.
/// Whether what is open may be replaced is the window's question, asked before
/// anything here is called. A deterministic render of a frozen patch is
/// <c>flyback-cli render</c>'s job — ADR-0078.
/// </remarks>
internal sealed class PatchFiles
{
    private readonly TopLevel owner;
    private readonly NodeEditor editor;
    private readonly Document document;
    private readonly PluginCatalog plugins;
    private readonly ReportLine report;
    private readonly Usage usage;
    private readonly Func<AssistantPanel?> assistant;
    private readonly Action<Patch> show;
    private readonly Func<PatchLoad, Reopen?, Task> offerMissing;

    /// <summary>
    /// Whether this document is a bundle. What it decides is small and worth
    /// having: which kind the save dialog offers first, so that a bundle saved
    /// again stays one without anybody typing an extension.
    /// </summary>
    private bool bundled;

    /// <summary>The conversations kept for patch files — a bundle keeps its own inside it (ADR-0072).</summary>
    private readonly ConversationStore conversations = new();

    /// <summary>A different document has arrived.</summary>
    public event EventHandler? Arrived;

    /// <summary>What was open has just been written, and is a file of its own now.</summary>
    public event EventHandler? Saved;

    /// <summary>What the patch names is measured from somewhere else now, so it has to be read again.</summary>
    public event EventHandler? Moved;

    /// <param name="show">Puts a patch that has just been read on the canvas, from its beginning.</param>
    /// <param name="offerMissing">Offers the plugins a patch that could not be opened is short of.</param>
    public PatchFiles(
        Shell shell,
        Action<Patch> show,
        Func<PatchLoad, Reopen?, Task> offerMissing)
    {
        owner = shell.Owner;
        editor = shell.Editor;
        document = shell.Document;
        plugins = shell.Plugins;
        report = shell.Report;
        usage = shell.Usage;
        assistant = () => shell.Assistant;
        this.show = show;
        this.offerMissing = offerMissing;
    }

    /// <summary>
    /// What the document is called: the file it was opened from or last written
    /// to, or the preset it was built from.
    /// </summary>
    /// <remarks>
    /// Kept rather than worked out, because after a Save As there is no other record
    /// of which file on the disk is the one on screen. Without the extension,
    /// because a preset has none — and a picker adds one itself.
    /// </remarks>
    public string? Name { get; private set; }

    /// <summary>
    /// The sound files the patch names, read once each and kept, measured from
    /// where the patch was opened.
    /// </summary>
    public SampleLibrary SoundFolder { get; } = new();

    /// <summary>The pictures a patch shows, cached the way its sounds are.</summary>
    public ImageLibrary PictureFolder { get; } = new();

    /// <summary>
    /// The files a bundle carries, while one is open, and null while the document
    /// is a loose patch backed by a folder.
    /// </summary>
    /// <remarks>
    /// Held rather than unpacked, which is what makes a bundle a document here
    /// rather than an archive to spill onto a disk first: nothing is written
    /// anywhere until they save. It costs one copy of the compressed bytes and
    /// costs the undo history nothing — the history is snapshots of the patch, and
    /// the patch is paths (ADR-0052).
    /// </remarks>
    public BundleFiles? Carried { get; private set; }

    /// <summary>
    /// Where a sound is looked for: the bundle first while one is open, and the
    /// folder behind it — because a module pointed at a file on this machine
    /// while a bundle is open means the file on this machine.
    /// </summary>
    public ISampleLibrary Sounds => Carried ?? (ISampleLibrary)SoundFolder;

    /// <inheritdoc cref="Sounds"/>
    public IImageLibrary Pictures => Carried ?? (IImageLibrary)PictureFolder;

    /// <summary>
    /// A different document from here on: what it is called, where the files it
    /// names are measured from, and the bundle it arrived in if it arrived in one.
    /// </summary>
    /// <remarks>
    /// Five routes reach a new patch and every one has to say all of this rather
    /// than the part it cares about: what a patch names is looked up in
    /// <see cref="Carried"/> first, so a preset picked while a bundle was open went
    /// on reading that bundle's sounds. Call it before handing the patch to the
    /// canvas, since setting the patch is what redraws the title.
    /// </remarks>
    /// <param name="name">What the title bar says, and null for a document with no name.</param>
    /// <param name="beside">
    /// The folder a relative sample or picture path is measured from. Null where
    /// there is none, which is what a preset has.
    /// </param>
    /// <param name="files">What a bundle brought with it, and null for everything else.</param>
    public void Became(string? name, string? beside, BundleFiles? files = null)
    {
        Name = name;

        Carried = files;
        bundled = files is not null;

        SoundFolder.Beside = beside;
        PictureFolder.Beside = beside;

        Arrived?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether the document is a bundle, which is what the next save offers first.</summary>
    public bool IsBundle => bundled;

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
        Name = name;
        bundled = asBundle;

        // Whatever preset the list still showed, the patch just took on a file
        // of its own — the preset is where it started, not what it is now.
        Saved?.Invoke(this, EventArgs.Empty);

        editor.MarkSaved();

        usage.Count(Used.Saved);
    }

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
        var conversation = assistant()?.ConversationToSave();
        var path = file.TryGetLocalPath();

        var kept = path is null ? conversation is null : conversations.Keep(path, written, conversation);

        assistant()?.ConversationSaved();

        if (!kept) report.Say($"Saved {file.Name}, but the conversation about it could not be kept with it.");
    }

    /// <summary>Asks for a patch to open, or null where the picker was canceled.</summary>
    public async Task<IStorageFile?> PickOpenAsync()
    {
        var all = new FilePickerFileType(GlobalConstants.ApplicationName)
        {
            Patterns = [.. PatchFileKinds.OpenKinds().SelectMany(o => o.Patterns ?? [])]
        };

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open patch",
            AllowMultiple = false,
            FileTypeFilter = [all, .. PatchFileKinds.OpenKinds()],
        });

        return files.Count == 0 ? null : files[0];
    }

    /// <summary>
    /// Opens a patch, a bundle or text, by the extension, however the file arrived.
    /// A plugin package is the window's to install before this is asked.
    /// </summary>
    public async Task OpenFileAsync(IStorageFile file)
    {
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
                report.Say($"Not opened. {loaded.Summary}", loaded.Detail);
                await offerMissing(loaded, new Reopen(Path: file.TryGetLocalPath()));
                return;
            }

            // Everything that says which document this is, before the patch it is
            // about — see Became.
            Became(
                Path.GetFileNameWithoutExtension(file.Name),
                Path.GetDirectoryName(file.TryGetLocalPath()));

            usage.Count(Used.Opened);

            // Whatever preset the list still showed is not this patch.
            show(loaded.Patch);

            // Whatever was said about this patch was kept beside it, if anything
            // was and the file is still what it was saved as — ADR-0072.
            assistant()?.Open(ConversationFor(file, text));

            // A patch file is the document, so the graph owns it — ADR-0068.
            document.DropSource();
        }
        catch (Exception ex)
        {
            report.Say($"Could not open patch: {ex.Message}");
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
            BundleReport packing;

            // Into memory first: a zip is written by seeking back over its own
            // directory, and what a picker hands back cannot always be seeked.
            using var packed = new MemoryStream();

            // The conversation goes inside, since a bundle is the whole of the
            // document wherever it is taken — ADR-0072.
            packing = PatchBundle.Write(
                packed, editor.Patch, Bytes, plugins.Modules, assistant()?.ConversationToSave());

            packed.Position = 0;

            await using (var stream = await file.OpenWriteAsync()) await packed.CopyToAsync(stream);

            SavedAs(Path.GetFileNameWithoutExtension(file.Name), asBundle: true);
            assistant()?.ConversationSaved();

            // Saved as a bundle, so a bundle is the document now and the graph
            // owns it — ADR-0068.
            document.DropSource();

            report.Say(packing.Whole
                ? $"Saved {file.Name}, carrying {packing.Carried.Count} file(s)."
                : $"Saved {file.Name}, without {packing.Missing.Count} file(s) that could not be read.",
                packing.Whole ? null : string.Join(Environment.NewLine, packing.Missing));

            return true;
        }
        catch (Exception ex)
        {
            report.Say($"Could not write bundle: {ex.Message}");
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
        if (Carried is not { } held) return 0;

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
        Carried = null;
        bundled = false;

        return written;
    }

    /// <summary>
    /// The bytes of a file the patch names: out of the bundle that is open where it
    /// holds one, and off the disk where it does not. The same order the libraries
    /// look in, so a bundle cannot come out holding a file nothing was reading.
    /// </summary>
    public byte[]? Bytes(string path)
    {
        if (Carried is { } held && held.Bytes.TryGetValue(path, out var bytes)) return bytes;

        return PatchPaths.Carriable(path, SoundFolder.Beside);
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
    public async Task<bool> SaveSourceAsync(IStorageFile file)
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

                SoundFolder.Beside = folder;
                PictureFolder.Beside = folder;
                Moved?.Invoke(this, EventArgs.Empty);

                report.Say(spilled > 0
                    ? $"Saved {file.Name}, and {spilled} file(s) beside it."
                    : $"Saved {file.Name}.");

                return true;
            }

            // Said only where there is something to have lost. A patch with no
            // groups in it loses nothing anybody would miss, and warning about
            // it every time would teach people to stop reading.
            var groups = editor.Patch.Groups?.Count ?? 0;

            report.Say(groups == 0
                ? $"Wrote {file.Name}. It is a copy: what is open is still {Name ?? "the patch"}."
                : $"Wrote {file.Name}, without its {groups} group(s) — text has no place to keep them. "
                  + $"What is open is still {Name ?? "the patch"}.");

            // A copy saves nothing, which the caller weighs: going ahead on the
            // strength of a printing would shut the only whole patch there is.
            return true;
        }
        catch (Exception ex)
        {
            report.Say($"Could not write the text: {ex.Message}");
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
                report.Say($"Not opened. {file.Name} does not read.", load.Report);
                return;
            }

            Became(
                Path.GetFileNameWithoutExtension(file.Name),
                Path.GetDirectoryName(file.TryGetLocalPath()));

            usage.Count(Used.Opened);

            // Whatever preset the list still showed is not this patch.
            show(load.Patch);

            // The text is the document now, and the canvas is a view of it —
            // ADR-0068. Said by opening on it, because somebody who opened a
            // source file came to read or write source.
            document.TakeSource(text);

            // Kept beside the file, as for a patch file — ADR-0072.
            assistant()?.Open(ConversationFor(file, text));

            report.Say($"Opened {file.Name}. The text is the document; the canvas shows what it builds.");
        }
        catch (Exception ex)
        {
            report.Say($"Could not open the text: {ex.Message}");
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
    /// held as they came, see <see cref="Carried"/>. Where a loose patch is opened
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
                report.Say($"Not opened. {lacking.Summary}", lacking.Detail);
                await offerMissing(lacking, new Reopen(Path: file.TryGetLocalPath()));
                return;
            }

            // A bundle answers for its own files first and falls through to the folder
            // it was opened from, which is why that folder is this one's rather than
            // whatever the last document left behind.
            Became(
                Path.GetFileNameWithoutExtension(file.Name),
                Path.GetDirectoryName(file.TryGetLocalPath()),
                new BundleFiles(bundle.Files, SoundFolder, PictureFolder));

            usage.Count(Used.Opened);

            // Whatever preset the list still showed is not this patch.
            show(bundle.Patch);
            document.DropSource();

            // A bundle carries its conversation inside it — ADR-0072.
            assistant()?.Open(bundle.Conversation);

            report.Say(bundle.Files.Count == 0
                ? $"Opened {file.Name}."
                : $"Opened {file.Name}, carrying {bundle.Files.Count} file(s).");
        }
        catch (Exception ex)
        {
            report.Say($"Could not open bundle: {ex.Message}");
        }
    }

    /// <summary>Asks where to save, or null where the picker was canceled.</summary>
    public async Task<IStorageFile?> PickSaveAsync() =>
        await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save patch",
            // What it is called now, or "patch" for one nobody has named — the
            // dialog is where a name is chosen, so it is where the one already
            // chosen belongs.
            SuggestedFileName = Name ?? "patch",

            // Whichever kind this document already is — see PatchFileKinds.SaveKinds.
            DefaultExtension = PatchFileKinds.SaveExtension(bundled, document.Owned),
            FileTypeChoices = PatchFileKinds.SaveKinds(bundled, document.Owned),
        });

    /// <summary>Writes the document as a bundle or a patch file, as its name says.</summary>
    /// <returns>Whether it was written.</returns>
    public async Task<bool> SavePatchFileAsync(IStorageFile file)
    {
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

            SoundFolder.Beside = folder;
            PictureFolder.Beside = folder;
            Moved?.Invoke(this, EventArgs.Empty);

            if (spilled > 0) report.Say($"Saved {file.Name}, and {spilled} file(s) beside it.");

            return true;
        }
        catch (Exception ex)
        {
            report.Say($"Could not save patch: {ex.Message}");
            return false;
        }
    }
}
