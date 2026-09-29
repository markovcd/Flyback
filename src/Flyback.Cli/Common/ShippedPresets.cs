using System.Text.Json;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli.Common;

/// <summary>What <c>--preset</c> opens: a shipped preset by name, and saying why when that does not work.</summary>
internal static class ShippedPresets
{
    /// <summary>Writes every name <c>--preset</c> accepts, one a line, or as a JSON array.</summary>
    public static void List(PluginCatalog catalog, TextWriter output, bool json = false)
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(catalog.Presets.Select(p => p.Name), Writing.Json));

            return;
        }

        foreach (var shipped in catalog.Presets) output.WriteLine(shipped.Name);
    }

    /// <summary>
    /// The preset built, with the files it carries, and its own spelling of its
    /// name; or null with the reason already written to <paramref name="error"/>.
    /// </summary>
    public static (Opened Opened, string Name)? Open(PluginCatalog catalog, string name, TextWriter error)
    {
        if (catalog.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } wanted)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: no preset is called '{name}'. The presets are:");

            foreach (var shipped in catalog.Presets) error.WriteLine($"    {shipped.Name}");

            return null;
        }

        try
        {
            var built = wanted.Build(catalog.Modules);

            if (wanted.Files is not { } files) return (new Opened(built, new SampleLibrary(), new ImageLibrary()), wanted.Name);

            var within = new BundleFiles(files());

            return (new Opened(built, within, within), wanted.Name);
        }
        catch (Exception ex)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: the '{wanted.Name}' preset would not build: {ex.Message}");

            return null;
        }
    }
}
