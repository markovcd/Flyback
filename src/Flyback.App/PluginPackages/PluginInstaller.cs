using Flyback.Plugins.Hosting;

namespace Flyback.App.PluginPackages;

/// <summary>
/// Puts a package's build for this system into the plugins folder, as
/// <c>plugins/&lt;plugin assembly&gt;</c> with <see cref="PluginPackage.MarkerName"/> beside it.
/// </summary>
/// <remarks>
/// Installing only unpacks into <see cref="PendingName"/>; the plugin is moved into
/// place at the next start, before anything is loaded (see <see cref="Finish"/>).
/// There is no reload, so it could not run sooner anyway, and a plugin being
/// replaced is one this process has loaded and Windows will not let go of.
/// <para>
/// A folder is only ever replaced if a package put it there, which the marker says.
/// The plugins Flyback ships and any somebody copied in by hand have none, so no
/// package can take their place.
/// </para>
/// </remarks>
/// <param name="folder">The plugins folder.</param>
/// <param name="loaded">What this run loaded, whose assemblies a package may not bring a second copy of.</param>
internal sealed class PluginInstaller(string folder, IReadOnlyList<LoadedPlugin> loaded)
{
    /// <summary>Starts with a dot, so the host never scans it for plugins.</summary>
    public const string PendingName = ".pending";

    private string Pending => Path.Combine(folder, PendingName);

    /// <summary>Why <paramref name="package"/> may not be installed here, or null where it may.</summary>
    public string? Refusal(PluginPackage package, string platform)
    {
        if (package.Refusal(platform) is { } refused) return refused;

        var name = package.Description(package.BuildFor(platform)!).Assembly;
        var target = Path.Combine(folder, name);

        if (Directory.Exists(target) && !FromPackage(target))
            return $"There is already a plugin in {PluginHost.DirectoryName}/{name} that was not installed from a package, and it is left alone.";

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (loaded.FirstOrDefault(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p.AssemblyPath), name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetDirectoryName(p.AssemblyPath), Path.GetFullPath(target), comparison)) is { } other)
            return $"{other.Info.Name} is already installed from {name}.dll.";

        if (!Writable()) return $"You cannot write to the plugins folder, {folder}.";

        return null;
    }

    /// <summary>
    /// The plugin in the folder <paramref name="name"/> installed now, or waiting to be
    /// at the next start, or null where neither.
    /// </summary>
    public PluginDescription? Replacing(string name) =>
        Installed(Path.Combine(Pending, name)) ?? Installed(Path.Combine(folder, name));

    /// <summary>Unpacks the build for <paramref name="platform"/> to be moved into place at the next start.</summary>
    public void Stage(PluginPackage package, string platform)
    {
        var build = package.BuildFor(platform)
            ?? throw new InvalidOperationException($"No build for {PluginPackage.Describe(platform)}.");

        var unpacking = Path.Combine(Pending, $".{Guid.NewGuid():N}");

        try
        {
            package.Unpack(build, unpacking);

            // Written last, over any file of that name the build carried.
            File.WriteAllText(Path.Combine(unpacking, PluginPackage.MarkerName), package.Sha256 + "\n");

            var staged = Path.Combine(Pending, package.Description(build).Assembly);

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
            var name = Path.GetFileName(staged);

            // An unpacking cut off before it finished.
            if (name.StartsWith('.'))
            {
                Delete(staged);
                continue;
            }

            if (!PluginDescription.ValidFolder(name) || Installed(staged) is not { } plugin)
            {
                problems.Add($"{name}: not a package's plugin, and removed.");
                Delete(staged);
                continue;
            }

            var target = Path.Combine(folder, name);
            var old = Path.Combine(pending, $".old-{name}");

            try
            {
                if (Directory.Exists(target) && !FromPackage(target))
                {
                    problems.Add($"{name}: {PluginHost.DirectoryName}/{name} holds a plugin that was not installed from a package, so it was not replaced.");
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
                installed.Add($"{plugin.Name} {plugin.Version}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{name}: not installed yet, {ex.Message}");
            }
        }

        if (!Directory.EnumerateFileSystemEntries(pending).Any()) Delete(pending);

        return (installed, problems);
    }

    private static bool FromPackage(string plugin) => File.Exists(Path.Combine(plugin, PluginPackage.MarkerName));

    /// <summary>The plugin a package left in <paramref name="plugin"/>, or null for a folder no package filled.</summary>
    private static PluginDescription? Installed(string plugin) =>
        FromPackage(plugin) ? PluginDescription.OfFolder(plugin) : null;

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
