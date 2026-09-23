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
    /// <param name="key">The PEM such a build is signed with, asked for only when there is one; null packs it unsigned.</param>
    public static void Seed(PresetStore presets, PluginStore plugins, string folder, string builds, Func<string?> key, DateTimeOffset at)
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

        SeedBuilds(plugins, builds, key, packed, at);
    }

    /// <summary>
    /// A plugin built beside the site stands in for a package nobody shipped: it is
    /// packed afresh at every start as a build for any system, so a run from the source
    /// lists what was just built. A plugin a package already covers is left to the package.
    /// </summary>
    private static void SeedBuilds(PluginStore plugins, string builds, Func<string?> key, HashSet<string> packed, DateTimeOffset at)
    {
        if (!Directory.Exists(builds)) return;

        ECDsa? signing = null;
        var asked = false;

        try
        {
            foreach (var folder in Directory.EnumerateDirectories(builds).Order(StringComparer.Ordinal))
            {
                if (Path.GetFileName(folder).StartsWith('.')) continue;
                if (PluginDescription.OfFolder(folder) is not { } description || packed.Contains(description.Assembly)) continue;

                if (!asked)
                {
                    signing = Load(key());
                    asked = true;
                }

                var bytes = PluginPackage.Pack([(PluginPackage.AnyPlatform, folder)]);

                if (signing is not null) bytes = PackageSigner.Sign(bytes, signing);

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
            signing?.Dispose();
        }
    }

    private static ECDsa? Load(string? pem)
    {
        if (pem is null) return null;

        try
        {
            return PackageSigner.Load(pem);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException($"{ReleaseKey.Variable} is not a key the site can sign a plugin with. {ex.Message}", ex);
        }
    }
}
