using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.App;

/// <summary>
/// One preset somebody saved, as it is listed: a name and the file it is in.
/// </summary>
/// <remarks>
/// Only listed when the folder is read. The patch is read when it is picked, drawn
/// or tried, so a folder of big bundles costs a directory listing to show and a
/// broken one is a tile that says so rather than a gallery that will not open.
/// </remarks>
/// <param name="Name">What the gallery calls it, which is the file's own name.</param>
/// <param name="Path">The file it is in, which is also how it is removed.</param>
public sealed class SavedPreset(string Name, string Path)
{
    public string Name { get; } = Name;

    public string Path { get; } = Path;

    /// <summary>
    /// The same preset as everything that offers presets takes one. Made once, so
    /// it can be looked up again by what it is — see <see cref="PresetLibrary.Holding"/>.
    /// </summary>
    public PatchPreset Preset => preset ??= new PatchPreset(Name, catalog => Open(catalog).Patch);

    private PatchPreset? preset;

    /// <summary>
    /// The patch, with whatever sounds and pictures it carries.
    /// </summary>
    /// <remarks>
    /// A bundle, so that a preset made from a patch playing a sample still plays it
    /// when the sample has moved. A plain patch dropped into the folder by hand is
    /// read too, carrying nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// It read with holes in it — a module from a plugin this build has not got.
    /// Refused rather than opened, as a patch file with holes in it is.
    /// </exception>
    public LoadedBundle Open(ModuleCatalog catalog)
    {
        LoadedBundle bundle;

        // Shared for deleting, so a tile reading it in the background never stands
        // in the way of somebody deleting it.
        using var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

        if (PatchBundle.Extension.Equals(System.IO.Path.GetExtension(Path), StringComparison.OrdinalIgnoreCase))
        {
            bundle = PatchBundle.Read(file, catalog);
        }
        else
        {
            using var reader = new StreamReader(file);
            var load = PatchIO.Read(reader.ReadToEnd(), catalog);
            bundle = new LoadedBundle(load.Patch, new Dictionary<string, byte[]>(), Load: load);
        }

        if (bundle.Load is { IsComplete: false } lacking)
            throw new InvalidOperationException(lacking.Summary);

        return bundle;
    }
}

/// <summary>
/// The presets somebody saved, as files in a folder of their own.
/// </summary>
/// <remarks>
/// The same shape as <c>GroupLibrary</c>: a file per preset and no index,
/// so a bundle dropped into the folder is in the gallery the next time it opens,
/// and one can be mailed to somebody. Reading never throws; writing does, because
/// failing to keep what somebody just asked to keep should be said.
/// </remarks>
public sealed class PresetLibrary
{
    /// <summary>Beside the kept groups, in the folder the settings are in.</summary>
    public static string DefaultFolder => Path.Combine(GlobalConstants.DataFolder, "presets");

    /// <summary>
    /// Every preset there is to start from, in the order the editor and the viewer
    /// list them: the blank canvas, then ideas, then interplay, then the big ones. A
    /// stable sort, so within a kind the engine's own still come before any plugin's —
    /// the list is the same wherever the program is installed.
    /// </summary>
    /// <remarks>
    /// The presets somebody saved come after all of those, so a save never moves
    /// the row any other preset is on.
    /// </remarks>
    public static List<PatchPreset> Ordered(IEnumerable<PatchPreset> shipped, PresetLibrary? saved) =>
        [.. shipped.OrderBy(p => p.Kind), .. saved?.All.Select(entry => entry.Preset) ?? []];

    /// <summary>
    /// The row of <paramref name="presets"/> holding the preset called
    /// <paramref name="name"/>, or the row holding the first patch in the list
    /// for one it does not offer — a plugin taken away, or a settings file nobody
    /// has written to yet.
    /// </summary>
    public static int Opening(IReadOnlyList<PatchPreset> presets, string? name)
    {
        var row = presets.ToList().FindIndex(preset => preset.Name == name);

        if (row >= 0) return row;

        // Nothing chosen yet, or a name this build no longer offers: the first
        // preset that is a patch, which is not the first preset. The blank canvas
        // heads the list, and a program that shipped thirty patches and opened on
        // none of them would be one whose presets nobody found.
        return Math.Max(presets.ToList().FindIndex(preset => preset.Kind != PresetKind.Blank), 0);
    }

    private List<SavedPreset> kept = [];

    /// <param name="folder">Somewhere other than the usual place, for the tests.</param>
    public PresetLibrary(string? folder = null)
    {
        Folder = folder ?? DefaultFolder;

        Reload();
    }

    public string Folder { get; }

    /// <summary>What was in the folder as of the last <see cref="Reload"/>, by name.</summary>
    public IReadOnlyList<SavedPreset> All => kept;

    /// <summary>Reads the folder again. Never throws.</summary>
    public void Reload()
    {
        var found = new List<SavedPreset>();

        try
        {
            if (Directory.Exists(Folder))
                foreach (var file in Directory.EnumerateFiles(Folder))
                    if (Listed(file))
                        found.Add(kept.FirstOrDefault(entry => entry.Path == file)
                            ?? new SavedPreset(Path.GetFileNameWithoutExtension(file), file));
        }
        catch (Exception)
        {
            // A folder that cannot be listed is a gallery without this section's
            // presets, which is what it looked like before anybody saved one.
        }

        // The entries already held are held again rather than made anew, so a
        // preset keeps being the same preset — and keeps its thumbnail — across
        // a save of some other one.
        kept = [.. found.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>What is saved under <paramref name="name"/>, or null where nothing is.</summary>
    /// <remarks>Compared the way a file name is, because a name here is one.</remarks>
    public SavedPreset? Named(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : kept.FirstOrDefault(entry => Same(entry.Name, name.Trim()));

    /// <summary>The saved preset <paramref name="preset"/> is, or null for one that is not saved here.</summary>
    public SavedPreset? Holding(PatchPreset preset) =>
        kept.FirstOrDefault(entry => ReferenceEquals(entry.Preset, preset));

    /// <summary>
    /// A preset as the patch it is and the files it plays: a saved one's own bundle,
    /// and none for one that is built.
    /// </summary>
    /// <param name="saved">Where saved presets are kept, or null where none are.</param>
    public static Opened Open(PatchPreset preset, PresetLibrary? saved, ModuleCatalog modules)
    {
        if (saved?.Holding(preset) is not { } held)
            return new Opened(preset.Build(modules), new SampleLibrary(), new ImageLibrary());

        var bundle = held.Open(modules);
        var files = BundleFiles.Of(bundle);

        return new Opened(bundle.Patch, files, files);
    }

    /// <summary>
    /// What is wrong with <paramref name="name"/> as a file name, or null where
    /// nothing is. A preset is named by its file, so a name that cannot be one is
    /// refused rather than quietly spelled some other way.
    /// </summary>
    public static string? Refusal(string name)
    {
        var trimmed = name.Trim();

        if (trimmed.Length == 0) return "A preset is listed by its name.";

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || trimmed.IndexOfAny(['/', '\\', ':']) >= 0)
            return "A name cannot have / \\ : or the like in it.";

        if (trimmed.EndsWith('.')) return "A name cannot end in a full stop.";

        return null;
    }

    /// <summary>
    /// Saves <paramref name="patch"/> as a bundle, replacing whatever was saved under
    /// the same name.
    /// </summary>
    /// <param name="open">Hands back the bytes of a file the patch names — see <see cref="PatchBundle.Write"/>.</param>
    /// <exception cref="ArgumentException">The name cannot be a file name — see <see cref="Refusal"/>.</exception>
    public SavedPreset Save(string name, Patch patch, Func<string, byte[]?> open, ModuleCatalog catalog)
    {
        if (Refusal(name) is { } refused) throw new ArgumentException(refused, nameof(name));

        name = name.Trim();

        Directory.CreateDirectory(Folder);

        var path = Path.Combine(Folder, name + PatchBundle.Extension);

        // Into memory first, so a patch that fails to pack leaves the one it
        // would have replaced where it was.
        using var packed = new MemoryStream();

        PatchBundle.Write(packed, patch, open, catalog);

        // A plain patch of the same name dropped in by hand is the one being
        // replaced, and two files for one name would be two tiles that read alike.
        if (Named(name) is { } before && before.Path != path) File.Delete(before.Path);

        // Written aside and swapped in whole, because a tile drawing this preset
        // has its file open on a thread of its own — see SavedPreset.Open, which
        // shares it for deleting. Replace is the one swap Windows allows over an
        // open handle; writing over it, and moving over it, are both refused.
        var writing = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllBytes(writing, packed.ToArray());

            if (File.Exists(path)) File.Replace(writing, path, destinationBackupFileName: null);
            else File.Move(writing, path);
        }
        catch
        {
            Forget(writing);
            throw;
        }

        // Held anew rather than kept, because what it holds has changed: a tile
        // drawn from the old one would show a patch that is not there any more.
        kept.RemoveAll(entry => entry.Path == path);
        Reload();

        return kept.First(entry => entry.Path == path);
    }

    /// <summary>Forgets one, which is deleting the file it was.</summary>
    /// <returns>Whether it was there to remove.</returns>
    public bool Remove(SavedPreset entry)
    {
        var went = false;

        if (File.Exists(entry.Path))
        {
            File.Delete(entry.Path);
            went = true;
        }

        Reload();
        return went;
    }

    /// <summary>Deletes a file half written, which is nothing to fail over.</summary>
    private static void Forget(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool Listed(string file) =>
        Path.GetExtension(file) is var extension
        && (extension.Equals(PatchBundle.Extension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals($".{PatchIO.FileExtension}", StringComparison.OrdinalIgnoreCase));

    private static bool Same(string a, string? b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);
}
