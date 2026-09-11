using System.IO.Compression;

namespace Flyback.Core.Graph;

/// <summary>
/// What came of packing a patch: the bundle was written whatever happened, and
/// this says what went into it and what did not.
/// </summary>
/// <param name="Carried">
/// The files that went in, as the patch named them before it was rewritten.
/// </param>
/// <param name="Missing">
/// The files that could not be read, again as the patch named them. Not an error:
/// a patch naming a file that has gone still opens and still draws, so a bundle
/// of it does too.
/// </param>
public readonly record struct BundleReport(
    IReadOnlyList<string> Carried,
    IReadOnlyList<string> Missing)
{
    /// <summary>Whether everything the patch names went in, which is what "self-contained" means.</summary>
    public bool Whole => Missing.Count == 0;
}

/// <summary>A bundle read back: the patch, the files it names, and the conversation saved with it.</summary>
/// <param name="Files">
/// Keyed by the path the patch stores, which is the path this wrote into it when
/// it was packed — so a library serving these needs no rules about folders.
/// </param>
/// <param name="Conversation">
/// The text of <see cref="PatchBundle.ConversationEntry"/>, or null for a bundle
/// saved with none.
/// </param>
public readonly record struct LoadedBundle(
    Patch Patch,
    IReadOnlyDictionary<string, byte[]> Files,
    string? Conversation = null);

/// <summary>
/// A patch and everything it names, in one file.
/// </summary>
/// <remarks>
/// A patch names its sounds and pictures rather than carrying them (ADR-0052,
/// ADR-0059), which leaves a <c>.fbk</c> full of paths that mean nothing on
/// somebody else's machine. A bundle is the other file: the document as it always
/// was, with the things it points at travelling beside it.
/// <para>
/// A zip, and deliberately nothing cleverer: the format is in the framework
/// (ADR-0019), every operating system opens one, and the zip's own directory is
/// the manifest. Inside is <c>patch.fbk</c> at the root and the files under
/// <c>files/</c>, with every path in the patch rewritten to name the copy in the
/// archive — which works because a relative path is measured from wherever the
/// patch is.
/// </para>
/// <para>
/// Nothing here opens a file: what to pack is asked for by path and answered by
/// the caller. What is carried is asked of the modules through
/// <see cref="NodeExtra.Files"/>, so this mentions neither WAV nor PNG.
/// </para>
/// </remarks>
public static class PatchBundle
{
    /// <summary>What a bundle is called. A patch is <c>.fbk</c>; this is that, packed.</summary>
    public const string Extension = ".fbkb";

    /// <summary>The patch, at the root of the archive.</summary>
    public const string PatchEntry = "patch.fbk";

    /// <summary>
    /// Where everything else goes. A folder rather than the root so that a file
    /// called <c>patch.fbk</c> cannot be mistaken for the patch, and so that an
    /// unpacked bundle reads as a document with its things beside it.
    /// </summary>
    public const string FilesFolder = "files/";

    /// <summary>
    /// The conversation an assistant had about this patch, at the root beside it,
    /// where one was saved with it (ADR-0072). Carried as text and never read
    /// here: what is in it is the shell's.
    /// </summary>
    public const string ConversationEntry = "conversation.json";

    /// <summary>
    /// Writes <paramref name="patch"/> and everything it names into
    /// <paramref name="archive"/>.
    /// </summary>
    /// <param name="patch"></param>
    /// <param name="conversation">
    /// What goes in as <see cref="ConversationEntry"/>, or null to write none.
    /// </param>
    /// <param name="open">
    /// Hands back the bytes of a file the patch names, or null where there are
    /// none to be had. Called once per distinct path, so a patch showing one
    /// picture four times reads it once and carries it once.
    /// </param>
    /// <param name="against">
    /// Which catalogue the type ids mean, for the copy this makes of the patch —
    /// it is written and read back through <see cref="PatchIO"/>, so a module
    /// from a plugin has to be nameable.
    /// </param>
    /// <param name="archive"></param>
    public static BundleReport Write(
        Stream archive,
        Patch patch,
        Func<string, byte[]?> open,
        ModuleCatalog? against = null,
        string? conversation = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(open);

        var catalog = against ?? NodeCatalog.Current;

        // A copy, because packing rewrites every path and the document on screen
        // must not change under somebody who only asked to save a copy of it.
        // Through the file format rather than by hand, which is what already
        // guarantees a deep copy of everything a node carries.
        var packed = PatchIO.Read(PatchIO.ToJson(patch, catalog), catalog).Patch;

        var carried = new List<string>();
        var missing = new List<string>();
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var (node, extra) in Carriers(packed, catalog))
        foreach (var path in extra.Files(node))
        {
            if (string.IsNullOrWhiteSpace(path) || renamed.ContainsKey(path)) continue;

            if (open(path) is not { } bytes)
            {
                missing.Add(path);
                continue;
            }

            var entry = FilesFolder + Unique(path, taken);

            using (var writing = zip.CreateEntry(entry).Open()) writing.Write(bytes);

            renamed[path] = entry;
            carried.Add(path);
        }

        // Told after every name has been decided, so that two nodes naming one
        // file are pointed at one copy of it.
        foreach (var (node, extra) in Carriers(packed, catalog))
            extra.Rebase(node, path => renamed.GetValueOrDefault(path, path));

        using (var writing = new StreamWriter(zip.CreateEntry(PatchEntry).Open()))
            writing.Write(PatchIO.ToJson(packed, catalog));

        if (conversation is not null)
        {
            using var writing = new StreamWriter(zip.CreateEntry(ConversationEntry).Open());

            writing.Write(conversation);
        }

        return new BundleReport(carried, missing);
    }

    /// <summary>
    /// Reads a bundle: the patch as it was packed, and the files it names, keyed
    /// by the names it names them.
    /// </summary>
    /// <remarks>
    /// Nothing is written anywhere. A caller that only wants to draw the patch
    /// serves the files from memory and never touches the disk.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// The archive is not one, or holds no patch. Thrown rather than answered,
    /// because unlike a missing sound file there is nothing to go on with.
    /// </exception>
    public static LoadedBundle Read(Stream archive, ModuleCatalog? against = null)
    {
        ArgumentNullException.ThrowIfNull(archive);

        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);

        var patch = zip.GetEntry(PatchEntry)
            ?? throw new InvalidDataException($"There is no {PatchEntry} in this bundle.");

        string json;
        using (var reading = new StreamReader(patch.Open())) json = reading.ReadToEnd();

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(FilesFolder, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.FullName.EndsWith('/')) continue;

            using var reading = entry.Open();
            using var bytes = new MemoryStream();

            reading.CopyTo(bytes);
            files[entry.FullName] = bytes.ToArray();
        }

        string? conversation = null;

        if (zip.GetEntry(ConversationEntry) is { } kept)
        {
            using var reading = new StreamReader(kept.Open());

            conversation = reading.ReadToEnd();
        }

        return new LoadedBundle(PatchIO.Read(json, against).Patch, files, conversation);
    }

    /// <summary>
    /// Every file a patch names, once each and in the order it names them. What
    /// <see cref="Write"/> is about to ask for, offered on its own because a patch
    /// backed by a bundle and saved loose has to put exactly these on the disk.
    /// </summary>
    public static IReadOnlyList<string> Files(Patch patch, ModuleCatalog? against = null)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var named = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (node, extra) in Carriers(patch, against ?? NodeCatalog.Current))
        foreach (var path in extra.Files(node))
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                named.Add(path);

        return named;
    }

    /// <summary>
    /// Every extra of every node that might name a file, paired with the node it
    /// belongs to.
    /// </summary>
    /// <remarks>
    /// A module this build does not have is passed over rather than complained
    /// about: its paths are still written back out unchanged, so a bundle made
    /// without a plugin carries everything but that plugin's files.
    /// </remarks>
    private static IEnumerable<(NodeInstance Node, NodeExtra Extra)> Carriers(
        Patch patch, ModuleCatalog catalog)
    {
        foreach (var node in patch.Nodes)
        {
            if (catalog.Get(node.TypeId) is not { } def) continue;

            foreach (var extra in def.Extras) yield return (node, extra);
        }
    }

    /// <summary>
    /// What to call a file inside the archive: its own name, and its own name with
    /// a number after it where that is taken already.
    /// </summary>
    /// <remarks>
    /// Numbered rather than hashed, because a bundle is a zip somebody may open in
    /// anything and <c>drums (2).wav</c> is a name they still recognise. Anything
    /// a zip entry may not hold is then dropped, so a path from another operating
    /// system still lands somewhere sensible instead of escaping the folder.
    /// </remarks>
    private static string Unique(string path, HashSet<string> taken)
    {
        var name = Safe(path);

        var stem = Path.GetFileNameWithoutExtension(name);
        var suffix = Path.GetExtension(name);

        var tried = name;

        for (var n = 2; !taken.Add(tried); n++) tried = $"{stem} ({n}){suffix}";

        return tried;
    }

    /// <summary>The last part of a path, whichever kind of separator it used, and never empty.</summary>
    private static string Safe(string path)
    {
        var cut = path.Trim().LastIndexOfAny(['/', '\\', ':']);
        var name = cut >= 0 ? path.Trim()[(cut + 1)..] : path.Trim();

        foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');

        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }
}
