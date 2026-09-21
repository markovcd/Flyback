using Flyback.App;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;

namespace Flyback.Viewer;

/// <summary>What a run plays: a file, a preset by name, or the one the settings start on.</summary>
internal static class ViewerSource
{
    /// <summary>
    /// Every preset there is, in the order the editor lists them: shipped ones by kind,
    /// then the ones somebody saved.
    /// </summary>
    public static IReadOnlyList<PatchPreset> Ordered(PluginCatalog plugins, PresetLibrary library) =>
        [.. plugins.Presets.OrderBy(preset => preset.Kind), .. library.All.Select(saved => saved.Preset)];

    /// <summary>
    /// The patch to play and what to call it, or null with the reason written to
    /// <paramref name="error"/>.
    /// </summary>
    public static (Opened Opened, string Name)? Resolve(
        ViewerOptions options, OutputSettings settings, PluginCatalog plugins, PresetLibrary library, TextWriter error)
    {
        if (options.Patch is { } path) return OpenFile(path, error);

        var ordered = Ordered(plugins, library);

        PatchPreset? wanted;

        if (options.Preset is { } name)
        {
            wanted = ordered.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));

            if (wanted is null)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: no preset is called '{name}'. --presets lists them.");

                return null;
            }
        }
        else
        {
            // What the editor opens on: the one the settings name, or for a name this
            // build no longer offers the first that is a patch rather than a blank.
            wanted = ordered.FirstOrDefault(preset => preset.Name == settings.DefaultPreset)
                ?? ordered.FirstOrDefault(preset => preset.Kind != PresetKind.Blank)
                ?? ordered.FirstOrDefault();

            if (wanted is null)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: there is no preset to play.");

                return null;
            }
        }

        return Build(wanted, library, plugins, error);
    }

    private static (Opened, string)? OpenFile(string path, TextWriter error)
    {
        var file = new FileInfo(path);
        var open = PatchFile.Open(file);

        // Said whether or not there is a patch: one short of a plugin plays anyway.
        foreach (var problem in open.Problems) error.WriteLine(problem);

        return open.Patch is { } opened ? (opened, file.Name) : null;
    }

    private static (Opened, string)? Build(PatchPreset preset, PresetLibrary library, PluginCatalog plugins, TextWriter error)
    {
        try
        {
            if (library.Holding(preset) is { } saved)
            {
                var bundle = saved.Open(plugins.Modules);
                var files = BundleFiles.Of(bundle);

                return (new Opened(bundle.Patch, files, files), preset.Name);
            }

            return (new Opened(preset.Build(plugins.Modules), new SampleLibrary(), new ImageLibrary()), preset.Name);
        }
        catch (Exception ex)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: the '{preset.Name}' preset would not open: {ex.Message}");

            return null;
        }
    }
}
