namespace Flyback.Core.Graph;

/// <summary>
/// A patch to start from, and how it is offered. Built on demand, because a
/// preset from a plugin needs that plugin's modules in the catalog.
/// </summary>
/// <param name="Description">
/// One line saying what the patch is for, written into the patch it builds
/// where that patch does not say already.
/// </param>
public sealed record PatchPreset(
    string Name,
    Func<ModuleCatalog, Patch> Build,
    string Description = "",
    PresetKind Kind = PresetKind.Idea) : IPreset
{
    /// <summary>
    /// The sound files and pictures the patch names, keyed by the path it names them
    /// by, or null for a preset that names none. Read when the preset is opened, as a
    /// bundle's are — see <see cref="PresetFiles.Embedded"/>.
    /// </summary>
    public Func<IReadOnlyDictionary<string, byte[]>>? Files { get; init; }

    /// <summary>Words to find the preset by, written into the patch it builds where that patch has none.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Builds the patch, carrying <see cref="Description"/> and <see cref="Tags"/> unless it has its own.</summary>
    public Func<ModuleCatalog, Patch> Build
    {
        get => modules =>
        {
            var patch = Builder(modules);

            if (patch.Description is null) patch.Describe(Description);
            if (patch.Tags is null) patch.Tag(Tags);
            return patch;
        };
        init => Builder = value;
    }

    /// <summary>The method <see cref="Build"/> calls, before it carries the description and tags.</summary>
    internal Func<ModuleCatalog, Patch> Builder { get; private init; } = Build;
}
