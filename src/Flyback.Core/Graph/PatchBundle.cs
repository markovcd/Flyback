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
/// The files that could not be read, again as the patch named them. Not an
/// error: a patch naming a file that has gone still opens, still compiles and
/// still draws — to silence or to black where that file would have been — so a
/// bundle of it does too, and the caller says so rather than this refusing.
/// </param>
public readonly record struct BundleReport(
    IReadOnlyList<string> Carried,
    IReadOnlyList<string> Missing)
{
    /// <summary>Whether everything the patch names went in, which is what "self-contained" means.</summary>
    public bool Whole => Missing.Count == 0;
}

/// <summary>A bundle read back: the patch, and the files it names, by name.</summary>
/// <param name="Files">
/// Keyed by the path the patch stores, which is the path this wrote into it when
/// it was packed — so a library serving these needs no rules about folders.
/// </param>
public readonly record struct LoadedBundle(Patch Patch, IReadOnlyDictionary<string, byte[]> Files);

/// <summary>
/// A patch and everything it names, in one file.
/// </summary>
/// <remarks>
/// <para>
/// [0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) made a
/// patch name its sounds rather than carry them, and
/// [0059](0059-a-picture-comes-in-as-a-texture.md) did the same for its
/// pictures. Both records say plainly what that gives up: a <c>.fbk</c> is no
/// longer everything it needs, and a patch sent to somebody arrives as a
/// document full of paths that mean nothing on their machine. Neither record
/// was wrong — the reasons are about the undo stack and the file being text —
/// and neither is undone here. A bundle is the *other* file: the document as it
/// always was, with the things it points at travelling beside it.
/// </para>
/// <para>
/// It is a zip, and deliberately nothing cleverer. The format is in the
/// framework, so this adds no dependency
/// ([0019](0019-no-third-party-dependencies-in-the-engine.md)); it is a format
/// every operating system can already open, so a bundle that this program some
/// day cannot read is still a folder somebody can get their work out of; and the
/// zip's own directory is the manifest, so there is nothing to keep in step.
/// </para>
/// <para>
/// Inside: <c>patch.fbk</c> at the root and the files under <c>files/</c>, named
/// by their own names. The patch inside is not quite the patch outside — every
/// path it holds is rewritten to name the copy in the archive — which is the
/// whole trick, because a relative path is already measured from wherever the
/// patch is (see <c>SampleLibrary.Beside</c>). So a bundle unpacked into a
/// folder is a working patch with no further arrangement, and a bundle read
/// without unpacking is one whose paths are the keys of what was read beside it.
/// </para>
/// <para>
/// Nothing here opens a file. What to pack is asked for by path and answered by
/// the caller, and what comes out is handed back as bytes for the caller to put
/// somewhere — the same division <see cref="Compile.ISampleLibrary"/> makes, and
/// for the same reason: this is the document's business and not the disk's.
/// </para>
/// <para>
/// What is carried is asked of the modules rather than known here. A kind of
/// carried state that names a file says so through
/// <see cref="NodeExtra.Files"/>, so this mentions neither WAV nor PNG and a
/// third kind of file is carried without it being touched.
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
    /// Writes <paramref name="patch"/> and everything it names into
    /// <paramref name="archive"/>.
    /// </summary>
    /// <param name="patch"></param>
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
        ModuleCatalog? against = null)
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

        return new BundleReport(carried, missing);
    }

    /// <summary>
    /// Reads a bundle: the patch as it was packed, and the files it names, keyed
    /// by the names it names them.
    /// </summary>
    /// <remarks>
    /// Nothing is written anywhere. A caller that wants a folder writes one out
    /// of what comes back; a caller that only wants to draw the patch — the
    /// command line, rendering on a machine with none of the files loose on it —
    /// serves them from memory and never touches the disk.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// The archive is not one, or holds no patch. Thrown rather than answered,
    /// because unlike a missing sound file there is nothing here to go on with:
    /// what was asked for was a patch and there is not one.
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

        return new LoadedBundle(PatchIO.Read(json, against).Patch, files);
    }

    /// <summary>
    /// Every file a patch names, once each and in the order it names them.
    /// </summary>
    /// <remarks>
    /// What <see cref="Write"/> is about to ask for, offered on its own because
    /// the question is worth asking without packing anything: a patch backed by
    /// a bundle that is being saved as a loose patch has to put those files on
    /// the disk, and what to put there is exactly this list.
    /// </remarks>
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
    /// about. Its paths are still in the patch and are still written back out
    /// unchanged, so a bundle made on a machine missing a plugin carries
    /// everything but that plugin's files — which is worse than carrying them and
    /// a great deal better than losing what the patch said.
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
    /// What to call a file inside the archive: its own name, and its own name
    /// with a number after it where that is taken already.
    /// </summary>
    /// <remarks>
    /// Two folders may each hold a <c>drums.wav</c>, and inside a bundle there is
    /// only one folder. Numbered rather than made unique by hashing the path,
    /// because a bundle is a zip somebody may open in anything and a file called
    /// <c>drums (2).wav</c> is one they can still recognise.
    /// <para>
    /// The name is taken apart by the same rules a path is, and then anything
    /// left that a zip entry may not hold is dropped — so a path from another
    /// operating system, or one with a separator the local one does not use,
    /// still lands somewhere sensible instead of escaping the folder.
    /// </para>
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
