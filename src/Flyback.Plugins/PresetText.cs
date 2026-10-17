using Flyback.Core.Graph;
using Flyback.Engine.Language;
using System.Reflection;

namespace Flyback.Plugins;

/// <summary>A preset written in the text language, baked into the assembly that ships it.</summary>
public static class PresetText
{
    /// <summary>
    /// Builds the <c>.fbks</c> resource <paramref name="name"/> of <paramref name="assembly"/>
    /// against the catalog the preset is opened with.
    /// </summary>
    /// <remarks>
    /// Embed the file with <c>LogicalName="name.fbks"</c>, outside the folder
    /// <see cref="PresetFiles.Embedded"/> reads. A text that does not build throws with
    /// every complaint, since a shipped preset that fails is a bug in the plugin.
    /// </remarks>
    public static Func<ModuleCatalog, Patch> Embedded(Assembly assembly, string name)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return modules =>
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"{assembly.GetName().Name} carries no resource '{name}'.");
            using var reader = new StreamReader(stream);

            var load = PatchLanguage.Build(reader.ReadToEnd(), modules);

            return load.Ok ? load.Patch : throw new InvalidOperationException($"{name} does not build:{Environment.NewLine}{load.Report}");
        };
    }
}
