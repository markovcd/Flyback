using System.Reflection;
using Flyback.Core.Graph;
using Flyback.Engine.Language;

namespace Flyback.Plugins;

/// <summary>A preset written in the text language, baked into the assembly that ships it.</summary>
public static class PresetText
{
    /// <summary>
    /// The <c>.fbks</c> resource <paramref name="resource"/> of <paramref name="assembly"/>,
    /// offered as <paramref name="name"/>, with the description and tags its own
    /// <c>description</c> and <c>tags</c> lines give.
    /// </summary>
    /// <remarks>
    /// Embed the file with <c>LogicalName="name.fbks"</c>, outside the folder
    /// <see cref="PresetFiles.Embedded"/> reads. A text that does not build throws with
    /// every complaint, since a shipped preset that fails is a bug in the plugin.
    /// </remarks>
    public static PatchPreset Preset(Assembly assembly, string name, string resource, PresetKind kind = PresetKind.Idea)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"{assembly.GetName().Name} carries no resource '{resource}'.");
        using var reader = new StreamReader(stream);

        var source = reader.ReadToEnd();
        var header = SourceHeader.Read(source);

        return new PatchPreset(name, modules => Build(source, resource, modules), header.Description, kind)
        {
            Tags = header.Tags,
        };
    }

    private static Patch Build(string source, string resource, ModuleCatalog modules)
    {
        var load = PatchLanguage.Build(source, modules);

        return load.Ok ? load.Patch : throw new InvalidOperationException($"{resource} does not build:{Environment.NewLine}{load.Report}");
    }
}
