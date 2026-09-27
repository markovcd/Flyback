using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>
/// One preset somebody saved, as it is listed: a name and the file it is in.
/// </summary>
/// <remarks>
/// Only listed when the folder is read. The patch is read when it is picked, drawn
/// or tried, so a folder of big bundles costs a directory listing to show and a
/// broken one is a tile that says so rather than a gallery that will not open.
/// </remarks>
/// <param name="name">What the gallery calls it, which is the file's own name.</param>
/// <param name="path">The file it is in, which is also how it is removed.</param>
public sealed class SavedPreset(string name, string path)
{
    public string Name { get; } = name;

    public string Path { get; } = path;

    /// <summary>
    /// The same preset as everything that offers presets takes one. Made once, so
    /// it can be looked up again by what it is — see <see cref="PresetLibrary.Holding"/>.
    /// </summary>
    public PatchPreset Preset => field ??= new PatchPreset(Name, catalog => Open(catalog).Patch);

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