using Flyback.Plugins.Hosting;

namespace Flyback.App.PluginPackages;

/// <summary>A plugin a package installed, and who signed that package.</summary>
internal sealed record InstalledPlugin(PluginDescription Description, PackageSigner? Signer);

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
/// package can take their place. A plugin is the same plugin only where its assembly's
/// name and its signer's key both match, so an update is signed by the key that
/// signed what it replaces.
/// </para>
/// </remarks>
/// <param name="folder">The plugins folder.</param>
/// <param name="loaded">What this run loaded, whose assemblies a package may not bring a second copy of.</param>
/// <param name="checkKeys">Whether to refuse unsigned packages and another signer's update; <see cref="PackageSigner.Checked"/> unless a test says otherwise.</param>
internal sealed class PluginInstaller(string folder, IReadOnlyList<LoadedPlugin> loaded, bool? checkKeys = null)
{
    /// <summary>Starts with a dot, so the host never scans it for plugins.</summary>
    public const string PendingName = ".pending";

    /// <summary>Starts a file in <see cref="PendingName"/> that asks for the plugin it names to be removed at the next start.</summary>
    private const string RemovePrefix = ".remove-";

    private readonly bool checkKeys = checkKeys ?? PackageSigner.Checked;

    private string Pending => Path.Combine(folder, PendingName);

    /// <summary>Why <paramref name="package"/> may not be installed here, or null where it may.</summary>
    public string? Refusal(PluginPackage package, string platform)
    {
        if (package.Refusal(platform) is { } refused) return refused;

        if (checkKeys && package.Signer is null)
            return "It is not signed, and Flyback installs only signed plugins.";

        var name = package.Description(package.BuildFor(platform)!).Assembly;
        var target = Path.Combine(folder, name);

        if (Directory.Exists(target) && !FromPackage(target))
            return $"There is already a plugin in {PluginHost.DirectoryName}/{name} that was not installed from a package, and it is left alone.";

        if (checkKeys && Replacing(name) is { } installed && installed.Signer != package.Signer)
        {
            return installed.Signer is null
                ? $"{installed.Description.Name} in {PluginHost.DirectoryName}/{name} was installed unsigned, so nothing shows this is its update."
                : $"{installed.Description.Name} in {PluginHost.DirectoryName}/{name} was signed with another key, so this is a different plugin with the same assembly name.";
        }

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
    public InstalledPlugin? Replacing(string name) =>
        Installed(Path.Combine(Pending, name)) ?? Installed(Path.Combine(folder, name));

    /// <summary>The plugins waiting to be moved into place at the next start.</summary>
    public IReadOnlyList<InstalledPlugin> Waiting() =>
        Directory.Exists(Pending)
            ? [.. Directory.EnumerateDirectories(Pending)
                .Where(staged => !Path.GetFileName(staged).StartsWith('.'))
                .Select(Installed)
                .OfType<InstalledPlugin>()]
            : [];

    /// <summary>The plugins that will be removed at the next start.</summary>
    public IReadOnlySet<string> Removing() =>
        Directory.Exists(Pending)
            ? Directory.EnumerateFiles(Pending, RemovePrefix + "*").Select(f => Path.GetFileName(f)[RemovePrefix.Length..]).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();

    /// <summary>Why the plugin in the folder <paramref name="name"/> may not be removed, or null where it may.</summary>
    public string? Removal(string name)
    {
        var target = Path.Combine(folder, name);

        // A loaded plugin's folder need not be named after its assembly: the shipped ones are not.
        var running = loaded
            .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p.AssemblyPath), name, StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetDirectoryName(p.AssemblyPath))
            .OfType<string>()
            .FirstOrDefault();

        foreach (var at in new[] { running, target })
        {
            if (at is not null && Directory.Exists(at) && !FromPackage(at))
                return $"It was not installed from a package, so {PluginHost.DirectoryName}/{Path.GetFileName(at)} is left alone.";
        }

        if (!FromPackage(target) && !FromPackage(Path.Combine(Pending, name))) return "It is not installed.";

        if (!Writable()) return $"You cannot write to the plugins folder, {folder}.";

        return null;
    }

    /// <summary>
    /// Removes the plugin in the folder <paramref name="name"/>: one waiting to be installed
    /// at once, and one installed at the next start, since this run has it loaded.
    /// </summary>
    /// <returns>Whether it is gone already.</returns>
    public bool Remove(string name)
    {
        var staged = Path.Combine(Pending, name);

        if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);

        if (!FromPackage(Path.Combine(folder, name))) return true;

        Directory.CreateDirectory(Pending);
        File.WriteAllText(Path.Combine(Pending, RemovePrefix + name), string.Empty);

        return false;
    }

    /// <summary>Unpacks the build for <paramref name="platform"/> to be moved into place at the next start.</summary>
    public void Stage(PluginPackage package, string platform)
    {
        var build = package.BuildFor(platform)
            ?? throw new InvalidOperationException($"No build for {PluginPackage.Describe(platform)}.");

        var unpacking = Path.Combine(Pending, $".{Guid.NewGuid():N}");

        try
        {
            package.Unpack(build, unpacking);

            // Written last, over any file of those names the build carried.
            File.WriteAllText(Path.Combine(unpacking, PluginPackage.MarkerName), package.Sha256 + "\n");

            var key = Path.Combine(unpacking, PluginPackage.KeyMarkerName);

            if (package.Signer is { } signer) File.WriteAllText(key, signer.Key + "\n");
            else File.Delete(key);

            var staged = Path.Combine(Pending, package.Description(build).Assembly);

            if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);

            Directory.Move(unpacking, staged);

            // Installing again takes back a removal.
            File.Delete(Path.Combine(Pending, RemovePrefix + package.Description(build).Assembly));
        }
        catch
        {
            Delete(unpacking);
            throw;
        }
    }

    /// <summary>
    /// Removes every plugin asked to be and moves every plugin waiting in
    /// <see cref="PendingName"/> into place. Called before plugins are loaded. One that
    /// cannot be moved yet — another Flyback has the old one open — waits for the start after.
    /// </summary>
    /// <returns>The name and version of each plugin installed and removed, and what went wrong.</returns>
    public static (IReadOnlyList<string> Installed, IReadOnlyList<string> Removed, IReadOnlyList<string> Problems) Finish(string folder)
    {
        var pending = Path.Combine(folder, PendingName);
        var installed = new List<string>();
        var removed = new List<string>();
        var problems = new List<string>();

        if (!Directory.Exists(pending)) return (installed, removed, problems);

        foreach (var marker in Directory.EnumerateFiles(pending, RemovePrefix + "*").Order(StringComparer.Ordinal).ToList())
        {
            var name = Path.GetFileName(marker)[RemovePrefix.Length..];
            var target = Path.Combine(folder, name);
            var old = Path.Combine(pending, $".old-{name}");

            try
            {
                // Moved aside first, so a plugin another Flyback has open is left whole.
                if (PluginDescription.ValidFolder(name) && FromPackage(target))
                {
                    var plugin = PluginDescription.OfFolder(target);

                    Delete(old);
                    Directory.Move(target, old);
                    Delete(old);
                    removed.Add(plugin is null ? name : $"{plugin.Name} {plugin.Version}");
                }

                File.Delete(marker);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{name}: not removed yet, {ex.Message}");
            }
        }

        foreach (var staged in Directory.EnumerateDirectories(pending).Order(StringComparer.Ordinal).ToList())
        {
            var name = Path.GetFileName(staged);

            // An unpacking cut off before it finished.
            if (name.StartsWith('.'))
            {
                Delete(staged);
                continue;
            }

            if (!PluginDescription.ValidFolder(name) || Installed(staged)?.Description is not { } plugin)
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

        return (installed, removed, problems);
    }

    private static bool FromPackage(string plugin) => File.Exists(Path.Combine(plugin, PluginPackage.MarkerName));

    /// <summary>The plugin a package left in <paramref name="plugin"/>, or null for a folder no package filled.</summary>
    public static InstalledPlugin? Installed(string plugin)
    {
        if (!FromPackage(plugin) || PluginDescription.OfFolder(plugin) is not { } description) return null;

        var key = Path.Combine(plugin, PluginPackage.KeyMarkerName);

        return new InstalledPlugin(description, File.Exists(key) ? PackageSigner.Parse(File.ReadAllText(key)) : null);
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
