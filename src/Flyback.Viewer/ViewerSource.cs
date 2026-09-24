using Flyback.App;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.Viewer;

/// <summary>What a run plays: a file, a preset by name, or the one the settings start on.</summary>
internal static class ViewerSource
{
    /// <summary>
    /// The patch to play and what to call it, or null with the reason written to
    /// <paramref name="error"/>.
    /// </summary>
    public static (Opened Opened, string Name)? Resolve(
        ViewerOptions options, OutputSettings settings, PluginCatalog plugins, PresetLibrary library, TextWriter error)
    {
        if (Find(options, settings, plugins, library, error) is not var (opened, name)) return null;

        if (options.FullScreen && !opened.Patch.Reaches().Picture)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {name} has no picture to fill the screen with; --full-screen needs one.");

            return null;
        }

        return (opened, name);
    }

    private static (Opened Opened, string Name)? Find(
        ViewerOptions options, OutputSettings settings, PluginCatalog plugins, PresetLibrary library, TextWriter error)
    {
        if (options.Patch is { } path) return OpenFile(path, error);

        var ordered = PresetLibrary.Ordered(plugins.Presets, library);

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
            if (ordered.Count == 0)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: there is no preset to play.");

                return null;
            }

            // What the editor opens on.
            wanted = ordered[PresetLibrary.Opening(ordered, settings.DefaultPreset)];
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
            return (PresetLibrary.Open(preset, library, plugins.Modules), preset.Name);
        }
        catch (Exception ex)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: the '{preset.Name}' preset would not open: {ex.Message}");

            return null;
        }
    }
}
