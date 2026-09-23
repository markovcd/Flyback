using System.Security.Cryptography;
using Flyback.Plugins.Hosting;

namespace Flyback.Server;

/// <summary>
/// What the site starts with: patch files and plugin packages shipped beside it
/// in <c>Defaults</c>, added as it starts and kept to the file (ADR-0138), and
/// a plugin built beside it where no package of it was shipped (ADR-0141).
/// </summary>
internal static class Defaults
{
    /// <remarks>
    /// A file there that is neither a patch nor a package the editor would install
    /// stops the site starting, as a bad setting does: it is a broken build, and a
    /// shelf quietly short of it would not say so.
    /// </remarks>
    /// <param name="builds">The folder a plugin's build is laid out under, one folder each, for a run from the source.</param>
    /// <param name="keyPath">The key the site signs such a build with, made here the first time it is needed.</param>
    public static void Seed(PresetStore presets, PluginStore plugins, string folder, string builds, string keyPath, DateTimeOffset at)
    {
        var packed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(folder))
        {
            foreach (var path in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                var bytes = File.ReadAllBytes(path);

                if (PluginPackage.Named(name))
                {
                    PluginSubmission submission;

                    try
                    {
                        submission = PluginSubmissions.Read(name, bytes);
                    }
                    catch (InvalidDataException ex)
                    {
                        throw new InvalidOperationException($"The default plugin {name} cannot be installed. {ex.Message}", ex);
                    }

                    plugins.Seed(submission, at);
                    packed.Add(submission.Assembly);
                    continue;
                }

                presets.Seed(
                    Submissions.Read(name, bytes, name: null)
                        ?? throw new InvalidOperationException($"The default preset {name} is not a patch."),
                    at);
            }
        }

        SeedBuilds(plugins, builds, keyPath, packed, at);
    }

    /// <summary>
    /// A plugin built beside the site stands in for a package nobody shipped: it is
    /// packed as a build for any system and signed with a key the site makes once
    /// and keeps beside its database, so a run from the source lists it and the
    /// editor installs it. A plugin a package already covers is left to the package.
    /// </summary>
    private static void SeedBuilds(PluginStore plugins, string builds, string keyPath, HashSet<string> packed, DateTimeOffset at)
    {
        if (!Directory.Exists(builds)) return;

        ECDsa? key = null;

        try
        {
            foreach (var folder in Directory.EnumerateDirectories(builds).Order(StringComparer.Ordinal))
            {
                if (Path.GetFileName(folder).StartsWith('.')) continue;
                if (PluginDescription.OfFolder(folder) is not { } description || packed.Contains(description.Assembly)) continue;

                key ??= LocalKey(keyPath);

                var bytes = PackageSigner.Sign(PluginPackage.Pack([(PluginPackage.AnyPlatform, folder)]), key);

                try
                {
                    plugins.Seed(PluginSubmissions.Read(description.Assembly + PluginPackage.Extension, bytes), at);
                }
                catch (InvalidDataException ex)
                {
                    throw new InvalidOperationException($"The plugin built at {folder} cannot be installed. {ex.Message}", ex);
                }
            }
        }
        finally
        {
            key?.Dispose();
        }
    }

    /// <summary>The key at <paramref name="path"/>, made and kept there the first time it is asked for.</summary>
    private static ECDsa LocalKey(string path)
    {
        if (!File.Exists(path))
        {
            if (Path.GetDirectoryName(path) is { } folder) Directory.CreateDirectory(folder);

            File.WriteAllText(path, PackageSigner.NewKey());
        }

        try
        {
            return PackageSigner.Load(File.ReadAllText(path));
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException($"{path} is not a key the site can sign a plugin with. {ex.Message}", ex);
        }
    }
}
