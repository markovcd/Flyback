using Flyback.Plugins.Hosting;

namespace Flyback.App.PluginPackages;

/// <summary>
/// Puts a package's build for this system into the plugins folder, as
/// <c>plugins/&lt;id&gt;</c> with the package's <c>plugin.json</c> beside it.
/// </summary>
/// <remarks>
/// Installing only unpacks into <see cref="PendingName"/>; the plugin is moved into
/// place at the next start, before anything is loaded (see <see cref="Finish"/>).
/// There is no reload, so it could not run sooner anyway, and a plugin being
/// replaced is one this process has loaded and Windows will not let go of.
/// <para>
/// A folder is only ever replaced if a package put it there, which its
/// <c>plugin.json</c> says. The plugins Flyback ships and any somebody copied in by
/// hand have none, so no package can take their place.
/// </para>
/// </remarks>
/// <param name="folder">The plugins folder.</param>
/// <param name="loaded">What this run loaded, whose ids a package may not take.</param>
internal sealed class PluginInstaller(string folder, IReadOnlyList<LoadedPlugin> loaded)
{
    /// <summary>Starts with a dot, so the host never scans it for plugins.</summary>
    public const string PendingName = ".pending";

    private string Pending => Path.Combine(folder, PendingName);

    /// <summary>Why <paramref name="package"/> may not be installed here, or null where it may.</summary>
    public string? Refusal(PluginPackage package, string platform)
    {
        if (package.Refusal(platform) is { } refused) return refused;

        var id = package.Manifest.Id;
        var target = Path.Combine(folder, id);

        if (Directory.Exists(target) && Installed(target) is null)
            return $"There is already a plugin in {PluginHost.DirectoryName}/{id} that was not installed from a package, and it is left alone.";

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (loaded.FirstOrDefault(p => p.Info.Id == id
                && !string.Equals(Path.GetDirectoryName(p.AssemblyPath), Path.GetFullPath(target), comparison)) is { } other)
            return $"{other.Info.Name} already has the id {id}.";

        if (!Writable()) return $"You cannot write to the plugins folder, {folder}.";

        return null;
    }

    /// <summary>
    /// What <paramref name="id"/> is installed as now, or waiting to be at the next
    /// start, or null where neither.
    /// </summary>
    public PluginManifest? Replacing(string id) =>
        Installed(Path.Combine(Pending, id)) ?? Installed(Path.Combine(folder, id));

    /// <summary>Unpacks the build for <paramref name="platform"/> to be moved into place at the next start.</summary>
    public void Stage(PluginPackage package, string platform)
    {
        var build = package.BuildFor(platform)
            ?? throw new InvalidOperationException($"No build for {PluginPackage.Describe(platform)}.");

        var unpacking = Path.Combine(Pending, $".{Guid.NewGuid():N}");

        try
        {
            package.Unpack(build, unpacking);

            // Written last, over any plugin.json the build carried.
            File.WriteAllBytes(Path.Combine(unpacking, PluginPackage.ManifestName), package.Manifest.ToJson());

            var staged = Path.Combine(Pending, package.Manifest.Id);

            if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);

            Directory.Move(unpacking, staged);
        }
        catch
        {
            Delete(unpacking);
            throw;
        }
    }

    /// <summary>
    /// Moves every plugin waiting in <see cref="PendingName"/> into place. Called
    /// before plugins are loaded. One that cannot be moved yet — another Flyback has
    /// the old one open — waits for the start after.
    /// </summary>
    /// <returns>The name and version of each plugin installed, and what went wrong.</returns>
    public static (IReadOnlyList<string> Installed, IReadOnlyList<string> Problems) Finish(string folder)
    {
        var pending = Path.Combine(folder, PendingName);
        var installed = new List<string>();
        var problems = new List<string>();

        if (!Directory.Exists(pending)) return (installed, problems);

        foreach (var staged in Directory.EnumerateDirectories(pending).Order(StringComparer.Ordinal).ToList())
        {
            var id = Path.GetFileName(staged);

            // An unpacking cut off before it finished.
            if (id.StartsWith('.'))
            {
                Delete(staged);
                continue;
            }

            if (!PluginManifest.ValidId(id) || Installed(staged) is not { } manifest)
            {
                problems.Add($"{id}: not a package's plugin, and removed.");
                Delete(staged);
                continue;
            }

            var target = Path.Combine(folder, id);
            var old = Path.Combine(pending, $".old-{id}");

            try
            {
                if (Directory.Exists(target) && Installed(target) is null)
                {
                    problems.Add($"{id}: {PluginHost.DirectoryName}/{id} holds a plugin that was not installed from a package, so it was not replaced.");
                    Delete(staged);
                    continue;
                }

                Delete(old);

                if (Directory.Exists(target)) Directory.Move(target, old);

                try
                {
                    Directory.Move(staged, target);
                }
                catch
                {
                    if (Directory.Exists(old)) Directory.Move(old, target);
                    throw;
                }

                Delete(old);
                installed.Add($"{manifest.Name} {manifest.Version}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{id}: not installed yet, {ex.Message}");
            }
        }

        if (!Directory.EnumerateFileSystemEntries(pending).Any()) Delete(pending);

        return (installed, problems);
    }

    /// <summary>The <c>plugin.json</c> a package left in <paramref name="plugin"/>, or null for a folder with none.</summary>
    private static PluginManifest? Installed(string plugin)
    {
        var path = Path.Combine(plugin, PluginPackage.ManifestName);

        try
        {
            return File.Exists(path) ? PluginManifest.Parse(File.ReadAllBytes(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Asked of the nearest folder that exists, so that asking creates nothing.</summary>
    private bool Writable()
    {
        var probed = Path.GetFullPath(folder);

        while (!Directory.Exists(probed) && Path.GetDirectoryName(probed) is { } parent) probed = parent;

        try
        {
            using (File.Create(Path.Combine(probed, $".probe-{Guid.NewGuid():N}"), 1, FileOptions.DeleteOnClose)) { }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next start to try again.
        }
    }
}
