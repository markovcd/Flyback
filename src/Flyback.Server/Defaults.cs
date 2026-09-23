using Flyback.Plugins.Hosting;

namespace Flyback.Server;

/// <summary>
/// What the site starts with: patch files and plugin packages shipped beside it
/// in <c>Defaults</c>, added as it starts and kept to the file (ADR-0138).
/// </summary>
internal static class Defaults
{
    /// <remarks>
    /// A file there that is neither a patch nor a package the editor would install
    /// stops the site starting, as a bad setting does: it is a broken build, and a
    /// shelf quietly short of it would not say so.
    /// </remarks>
    public static void Seed(PresetStore presets, PluginStore plugins, string folder, DateTimeOffset at)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var path in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            var bytes = File.ReadAllBytes(path);

            if (PluginPackage.Named(name))
            {
                try
                {
                    plugins.Seed(PluginSubmissions.Read(name, bytes), at);
                }
                catch (InvalidDataException ex)
                {
                    throw new InvalidOperationException($"The default plugin {name} cannot be installed. {ex.Message}", ex);
                }

                continue;
            }

            presets.Seed(
                Submissions.Read(name, bytes, name: null)
                    ?? throw new InvalidOperationException($"The default preset {name} is not a patch."),
                at);
        }
    }
}
