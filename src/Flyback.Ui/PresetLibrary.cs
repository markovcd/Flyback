using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Ui;

/// <summary>
/// The presets somebody saved, as files in a folder of their own, or none where
/// there is no folder.
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
    /// list them: <see cref="PresetOrder"/>, so the blank canvas, then ideas, then
    /// interplay, then the big ones, each by name.
    /// </summary>
    /// <remarks>
    /// The presets somebody saved come after all of those, so a save never moves
    /// the row any other preset is on.
    /// </remarks>
    public static List<PatchPreset> Ordered(IEnumerable<PatchPreset> shipped, PresetLibrary? saved) =>
        [.. PresetOrder.Of(shipped), .. saved?.All.Select(entry => entry.Preset) ?? []];

    /// <summary>What opens when nothing is chosen, or what was chosen is not offered.</summary>
    public const string Fallback = "Plasma";

    /// <summary>
    /// The row of <paramref name="presets"/> holding the preset called
    /// <paramref name="name"/>, or the row holding <see cref="Fallback"/> for one
    /// it does not offer — a plugin taken away, or a settings file nobody has
    /// written to yet.
    /// </summary>
    public static int Opening(IReadOnlyList<PatchPreset> presets, string? name)
    {
        var row = presets.ToList().FindIndex(preset => preset.Name == name);

        if (row >= 0) return row;

        return Math.Max(presets.ToList().FindIndex(preset => preset.Name == Fallback), 0);
    }

    private List<SavedPreset> kept = [];

    public PresetLibrary(IPresetFolder setup)
        : this(setup.PresetFolder)
    {
    }

    /// <param name="folder">Where the presets are kept, or null to keep none.</param>
    public PresetLibrary(string? folder)
    {
        Folder = folder;

        Reload();
    }

    /// <summary>Where the presets are kept, or null where none are.</summary>
    public string? Folder { get; }

    /// <summary>Whether a preset can be saved here, which it cannot without a folder.</summary>
    public bool Keeps => Folder is not null;

    /// <summary>What was in the folder as of the last <see cref="Reload"/>, by name.</summary>
    public IReadOnlyList<SavedPreset> All => kept;

    /// <summary>Reads the folder again. Never throws.</summary>
    public void Reload()
    {
        var found = new List<SavedPreset>();

        if (Folder is null) return;

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
    /// and what a built one carries.
    /// </summary>
    /// <param name="saved">Where saved presets are kept, or null where none are.</param>
    public static Opened Open(PatchPreset preset, PresetLibrary? saved, ModuleCatalog modules)
    {
        if (saved?.Holding(preset) is not { } held)
        {
            var patch = preset.Build(modules);

            if (preset.Files is { } carried)
            {
                var within = new BundleFiles(carried());

                return new Opened(patch, within, within);
            }

            return new Opened(patch, new SampleLibrary(), new ImageLibrary());
        }

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
    /// <exception cref="InvalidOperationException">No presets are kept here — see <see cref="Keeps"/>.</exception>
    public SavedPreset Save(string name, Patch patch, Func<string, byte[]?> open, ModuleCatalog catalog)
    {
        if (Refusal(name) is { } refused) throw new ArgumentException(refused, nameof(name));

        var folder = Folder ?? throw new InvalidOperationException("No presets are kept here.");

        name = name.Trim();

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, name + PatchBundle.Extension);

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
