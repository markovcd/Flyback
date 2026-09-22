using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.App.PluginPackages;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>Installing or updating a plugin from a <c>.fbkp</c> opened with Flyback or found in the plugins window (ADR-0132).</summary>
public sealed partial class MainWindow
{
    /// <summary>Where a package's plugin is installed, or null where this window installs nothing.</summary>
    private readonly string? pluginFolder;

    /// <summary>Starts Flyback again once this window has closed, or null where a restart is not offered.</summary>
    private readonly Action? relaunch;

    /// <summary>Where the plugins window lists shared plugins from, or null for nowhere.</summary>
    private readonly Uri? pluginSite;

    /// <summary>
    /// Shows what the package says it is and installs it if asked. Leaves the patch
    /// alone, so nothing unsaved is asked about.
    /// </summary>
    private async Task InstallPluginAsync(IStorageFile file)
    {
        PluginPackage package;

        try
        {
            await using var stream = await file.OpenReadAsync();
            package = await PluginPackage.ReadAsync(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Report($"{file.Name} was not installed. {ex.Message}");
            return;
        }

        if (await InstallPackageAsync(package) is { } said) Report(said);
    }

    /// <summary>
    /// Asks whether to install <paramref name="package"/> and does so if told to.
    /// </summary>
    /// <returns>What became of it, or null where the question was cancelled or Flyback is restarting.</returns>
    private async Task<string?> InstallPackageAsync(PluginPackage package)
    {
        var platform = PluginPackage.ThisPlatform;
        var installer = pluginFolder is null ? null : new PluginInstaller(pluginFolder, plugins.Plugins);
        var refusal = installer is null ? "This window has no plugins folder." : installer.Refusal(package, platform);
        var described = package.DescriptionFor(platform);
        var replacing = described is null ? null : installer?.Replacing(described.Assembly);
        var change = described is null ? PluginChange.Install : PluginChanges.Of(replacing?.Description, described);

        var view = PluginInstallView.View(package, platform, refusal, replacing, change, offerRestart: relaunch is not null);
        var answer = await this.ShowDialog<PluginAnswer>(PluginInstallView.Title(change), view);

        if (answer == PluginAnswer.Cancel) return null;

        var name = $"{described!.Name} {described.Version}";

        try
        {
            installer!.Stage(package, platform);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return $"{name} was not installed: {ex.Message}";
        }

        if (answer == PluginAnswer.InstallAndRestart && await RestartAsync()) return null;

        return $"{name} is {(change == PluginChange.Update ? "updated" : "installed")}, and loads the next time Flyback starts.";
    }

    /// <summary>
    /// The plugins window: what is installed, and what the plugin site offers, to
    /// search and install from.
    /// </summary>
    private async Task ShowPluginsAsync()
    {
        var site = pluginSite is null ? null : new PluginSite(SiteHttp ?? SiteClient.Value, pluginSite);
        using var hub = new PluginHub(site, () => Task.Run(InstalledPlugins), plugin => InstallFromSiteAsync(site!, plugin));

        // Read before the window goes up, so the rows do not arrive above whatever is showing.
        await hub.RereadAsync();
        _ = hub.AskSiteAsync();

        await this.ShowDialog<object?>("Plugins", hub.View, hub.Header, fill: true);
    }

    /// <summary>Shared by every plugins window, as an <see cref="HttpClient"/> is meant to be.</summary>
    private static readonly Lazy<HttpClient> SiteClient = new(() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

    /// <summary>What the plugins window asks the site with in place of <see cref="SiteClient"/>, for a test.</summary>
    internal HttpClient? SiteHttp { get; init; }

    private async Task<string?> InstallFromSiteAsync(PluginSite site, SitePlugin plugin)
    {
        PluginPackage package;

        try
        {
            var bytes = await site.DownloadAsync(plugin, CancellationToken.None);

            package = await PluginPackage.ReadAsync(new MemoryStream(bytes, writable: false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException)
        {
            return $"{plugin.Plugin.Name} was not installed. {ex.Message}";
        }

        return await InstallPackageAsync(package);
    }

    /// <summary>
    /// Every plugin this run loaded, and every one waiting for the next start, read
    /// from its folder. Reads assemblies, so it is kept off the UI thread.
    /// </summary>
    private IReadOnlyList<HubInstalled> InstalledPlugins()
    {
        var waiting = pluginFolder is null
            ? []
            : new PluginInstaller(pluginFolder, plugins.Plugins).Waiting()
                .ToDictionary(p => p.Description.Assembly, StringComparer.OrdinalIgnoreCase);

        var listed = new List<HubInstalled>();

        foreach (var loaded in plugins.Plugins)
        {
            var assembly = Path.GetFileNameWithoutExtension(loaded.AssemblyPath);
            var described = Path.GetDirectoryName(loaded.AssemblyPath) is { } folder ? PluginDescription.OfFolder(folder) : null;

            var plugin = described is null
                ? new ListedPlugin(assembly, loaded.Info.Name, string.Empty, string.Empty, loaded.Info.Description, [], [])
                : ListedPlugin.Of(described);

            // A plugin that never set its product name is known by what it calls itself.
            if (plugin.Name == plugin.Assembly)
                plugin = plugin with { Name = loaded.Info.Name, Description = plugin.Description.Length > 0 ? plugin.Description : loaded.Info.Description };

            waiting.Remove(plugin.Assembly, out var next);
            listed.Add(new HubInstalled(plugin, next?.Description.Version, Loaded: true, described?.Preview?.Bytes));
        }

        listed.AddRange(waiting.Values.Select(p =>
            new HubInstalled(ListedPlugin.Of(p.Description), p.Description.Version, Loaded: false, p.Description.Preview?.Bytes)));

        return listed;
    }

    /// <summary>
    /// Closes the window, asking about unsaved work as any close does, and starts
    /// Flyback again behind it. False where the window stays: the question was
    /// cancelled, or a recording is running, which only its own button should end.
    /// </summary>
    private async Task<bool> RestartAsync()
    {
        if (TakeInHand || !await MayReplaceThePatchAsync()) return false;

        relaunch!();

        leaving = true;
        Close();

        return true;
    }
}
