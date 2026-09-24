using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.App.PluginPackages;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// Installing, updating and removing a plugin, from a <c>.fbkp</c> opened with
/// Flyback or from the plugins window (ADR-0132, ADR-0148).
/// </summary>
internal sealed class PluginInstalls
{
    private readonly Window owner;
    private readonly PluginCatalog plugins;
    private readonly ReportLine report;
    private readonly string? pluginFolder;
    private readonly Uri? presetSite;
    private readonly Func<HttpClient> http;
    private readonly Func<AudioSetup> sound;
    private readonly Func<(string Assembly, string Said)?> assisting;
    private readonly Func<Task<bool>>? restart;

    /// <param name="pluginFolder">Where a package's plugin is installed, or null where nothing is.</param>
    /// <param name="presetSite">Where the plugins window lists shared plugins from, or null for nowhere.</param>
    /// <param name="assisting">The folder of the plugin whose assistant Ask sends a patch to, and what to say of it.</param>
    /// <param name="restart">
    /// Closes the window and starts Flyback again, answering false where the window
    /// stays; null where a restart is not offered.
    /// </param>
    public PluginInstalls(
        Window owner,
        PluginCatalog plugins,
        ReportLine report,
        string? pluginFolder,
        Uri? presetSite,
        Func<HttpClient> http,
        Func<AudioSetup> sound,
        Func<(string Assembly, string Said)?> assisting,
        Func<Task<bool>>? restart)
    {
        this.owner = owner;
        this.plugins = plugins;
        this.report = report;
        this.pluginFolder = pluginFolder;
        this.presetSite = presetSite;
        this.http = http;
        this.sound = sound;
        this.assisting = assisting;
        this.restart = restart;
    }

    /// <summary>
    /// What the patch the plugins window was opened for was short of, so an install
    /// can say how many are still to come. Empty at every other moment.
    /// </summary>
    public IReadOnlyList<SitePlugin> Awaited { get; set; } = [];

    /// <summary>
    /// Shows what the package says it is and installs it if asked. Leaves the patch
    /// alone, so nothing unsaved is asked about.
    /// </summary>
    public async Task InstallAsync(IStorageFile file)
    {
        PluginPackage package;

        try
        {
            await using var stream = await file.OpenReadAsync();
            package = await PluginPackage.ReadAsync(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            report.Say($"{file.Name} was not installed. {ex.Message}");
            return;
        }

        if (await InstallPackageAsync(package) is { } said) report.Say(said);
    }

    /// <summary>
    /// Asks whether to install <paramref name="package"/> and does so if told to.
    /// </summary>
    /// <returns>What became of it, or null where the question was canceled or Flyback is restarting.</returns>
    private async Task<string?> InstallPackageAsync(PluginPackage package)
    {
        var platform = PluginPackage.ThisPlatform;
        var installer = pluginFolder is null ? null : new PluginInstaller(pluginFolder, plugins.Plugins);
        var refusal = installer is null ? "This window has no plugins folder." : installer.Refusal(package, platform);
        var described = package.DescriptionFor(platform);
        var replacing = described is null ? null : installer?.Replacing(described.Assembly);
        var change = described is null ? PluginChange.Install : PluginChanges.Of(replacing?.Description, described);

        var removable = described is not null && installer?.Removal(described.Assembly) is null;

        var awaiting = Awaiting(installer, described?.Assembly);

        var view = PluginInstallView.View(
            package, platform, refusal, replacing, change,
            // Not offered while the patch is short of others: one start loads everything
            // installed by then, and a restart before the last of them lands back on the
            // same refusal with the window it was being installed from thrown away.
            offerRestart: restart is not null && awaiting == 0,
            removable,
            awaiting);
        var answer = await owner.ShowDialog<PluginAnswer>(PluginInstallView.Title(change), view);

        if (answer == PluginAnswer.Cancel) return null;

        if (answer == PluginAnswer.Remove) return Remove(installer!, described!.Assembly, (replacing?.Description ?? described).Name);

        var name = $"{described!.Name} {described.Version}";

        try
        {
            installer!.Stage(package, platform);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return $"{name} was not installed: {ex.Message}";
        }

        if (answer == PluginAnswer.InstallAndRestart && restart is not null && await restart()) return null;

        return $"{name} is {(change == PluginChange.Update ? "updated" : "installed")}, and loads the next time Flyback starts.";
    }

    /// <summary>
    /// The plugins window: what is installed, and what the plugin site offers, to
    /// search and install from.
    /// </summary>
    /// <param name="wanted">What a patch was short of, listed first, or null for an ordinary opening.</param>
    public async Task ShowAsync(IReadOnlyList<SitePlugin>? wanted = null)
    {
        var site = presetSite is null ? null : new PluginSite(http(), presetSite);
        var (run, troubles) = PluginSummary.Run(plugins, pluginFolder ?? PluginHost.DefaultDirectory, sound());
        using var hub = new PluginHub(site, () => { var assisting = this.assisting(); return Task.Run(() => InstalledPlugins(assisting, troubles)); }, (plugin, downloaded) => InstallFromSiteAsync(site!, plugin, downloaded), plugin => ShowInstalledAsync(site, plugin), wanted, run);

        // Read before the window goes up, so the rows do not arrive above whatever is showing.
        await hub.RereadAsync();
        _ = hub.AskSiteAsync();

        await owner.ShowDialog<object?>("Plugins", hub.View, hub.Header, fill: true);
    }

    /// <summary>
    /// How many of what the patch was short of would still be missing after installing
    /// <paramref name="installing"/> — counting one waiting for the next start as had,
    /// since one start loads every plugin staged by then.
    /// </summary>
    private int Awaiting(PluginInstaller? installer, string? installing)
    {
        if (Awaited.Count == 0 || installer is null) return 0;

        var had = plugins.Plugins
            .Select(plugin => Path.GetFileNameWithoutExtension(plugin.AssemblyPath))
            .Concat(installer.Waiting().Select(waiting => waiting.Description.Assembly))
            .Concat(installing is null ? [] : [installing]);

        return MissingPlugins.StillNeeded(Awaited, had);
    }

    private async Task<string?> InstallFromSiteAsync(PluginSite site, SitePlugin plugin, Action downloaded)
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
        finally
        {
            downloaded();
        }

        return await InstallPackageAsync(package);
    }

    /// <summary>
    /// What an installed plugin is, read from its folder, with an update where the plugin
    /// site has a newer build and a way to remove it.
    /// </summary>
    /// <returns>What became of it, or null where nothing did.</returns>
    private async Task<string?> ShowInstalledAsync(PluginSite? site, HubInstalled plugin)
    {
        var assembly = plugin.Plugin.Assembly;
        var loaded = plugins.Plugins.FirstOrDefault(p =>
            string.Equals(Path.GetFileNameWithoutExtension(p.AssemblyPath), assembly, StringComparison.OrdinalIgnoreCase));
        var installer = pluginFolder is null ? null : new PluginInstaller(pluginFolder, plugins.Plugins);

        var reading = Task.Run(() =>
        {
            if (loaded is not null && Path.GetDirectoryName(loaded.AssemblyPath) is { } running)
            {
                var installed = PluginInstaller.Installed(running);

                return (running, installed, installed?.Description ?? PluginDescription.OfFolder(running));
            }

            // Waiting for the next start, which moves it here.
            var waiting = installer?.Replacing(assembly);
            var folder = pluginFolder is null ? null : Path.Combine(pluginFolder, assembly);

            if (waiting is not null || folder is null || !Directory.Exists(folder)) return (folder, waiting, waiting?.Description);

            // Installed, and refused this run.
            var there = PluginInstaller.Installed(folder);

            return (folder, there, there?.Description ?? PluginDescription.OfFolder(folder));
        });

        // Not waited for: a site that is down takes seconds to say so.
        var newer = NewerAsync(site, plugin);
        var (folder, fromPackage, described) = await reading;

        var removal = plugin.Removing ? "It is removed at the next start already."
            : installer is null ? "This window has no plugins folder."
            : installer.Removal(assembly);

        var ids = plugins.Plugins
            .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p.AssemblyPath), assembly, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Info.Id);

        // Only a loaded plugin has a provider in the catalog.
        var providers = loaded is null ? [] : (described?.Modules ?? [])
            .Select(m => plugins.Modules.ProviderOf(m.TypeId))
            .OfType<ModuleProvider>()
            .Distinct()
            .Select(p => $"{p.Name} ({p.Id})");

        var view = PluginInstallView.Installed(
            plugin.Plugin, described, fromPackage, folder, plugin.State,
            Named(newer), removal,
            string.Join(", ", ids), string.Join(", ", providers));

        return await owner.ShowDialog<PluginAnswer>(plugin.Plugin.Name, view) switch
        {
            // No site row here to stop saying Downloading…, so nothing to tell.
            PluginAnswer.Download => await InstallFromSiteAsync(site!, (await newer)!, () => { }),
            PluginAnswer.Remove => Remove(installer!, assembly, plugin.Plugin.Name),
            _ => null,
        };
    }

    /// <summary>
    /// The newest build the plugin site has of <paramref name="plugin"/>, where it is newer
    /// than the one installed. Asked briefly, and null where the site does not answer.
    /// </summary>
    private static async Task<SitePlugin?> NewerAsync(PluginSite? site, HubInstalled plugin)
    {
        if (site is null) return null;

        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        try
        {
            var found = await site.SearchAsync(plugin.Plugin.Assembly, tag: null, page: 1, cancel.Token);

            return found.Items
                .Where(p => string.Equals(p.Plugin.Assembly, plugin.Plugin.Assembly, StringComparison.OrdinalIgnoreCase))
                .Where(p => PluginChanges.Compare(p.Plugin.Version, plugin.Version) > 0)
                .OrderDescending(Comparer<SitePlugin>.Create((a, b) => PluginChanges.Compare(a.Plugin.Version, b.Plugin.Version) ?? 0))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> Named(Task<SitePlugin?> newer) =>
        await newer is { } found ? $"{found.Plugin.Name} {found.Plugin.Version}" : null;

    /// <summary>Removes the plugin in the folder <paramref name="assembly"/>, and says what became of it.</summary>
    private static string Remove(PluginInstaller installer, string assembly, string name)
    {
        try
        {
            return installer.Remove(assembly)
                ? $"{name} is removed."
                : $"{name} is removed the next time Flyback starts.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{name} was not removed: {ex.Message}";
        }
    }

    /// <summary>
    /// Every plugin this run loaded, and every one waiting for the next start, read
    /// from its folder. Reads assemblies, so it is kept off the UI thread.
    /// </summary>
    /// <param name="assisting">The folder of the plugin Ask sends a patch to, and what to say of it, or null for none.</param>
    /// <param name="troubles">What went wrong this run, by plugin folder. A folder in it that loaded nothing is listed too.</param>
    private IReadOnlyList<HubInstalled> InstalledPlugins((string Assembly, string Said)? assisting, IReadOnlyDictionary<string, string>? troubles = null)
    {
        troubles ??= new Dictionary<string, string>();

        var unexplained = troubles.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installer = pluginFolder is null ? null : new PluginInstaller(pluginFolder, plugins.Plugins);
        var waiting = installer is null ? [] : installer.Waiting().ToDictionary(p => p.Description.Assembly, StringComparer.OrdinalIgnoreCase);
        var removing = installer?.Removing() ?? new HashSet<string>();

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
            var said = assisting is { } a && string.Equals(a.Assembly, assembly, StringComparison.OrdinalIgnoreCase) ? a.Said : null;
            var home = Path.GetDirectoryName(loaded.AssemblyPath) is { } at ? PluginSummary.Folder(at) : null;
            var trouble = home is not null && troubles.TryGetValue(home, out var wrong) ? wrong : null;

            if (home is not null) unexplained.Remove(home);

            listed.Add(new HubInstalled(plugin, next?.Description.Version, Loaded: true, described?.Preview?.Bytes, removing.Contains(plugin.Assembly), said, trouble));
        }

        // A plugin that loaded nothing has no row of its own otherwise.
        foreach (var folder in unexplained)
        {
            var described = PluginDescription.OfFolder(folder);
            var name = Path.GetFileName(folder);
            var plugin = described is null ? new ListedPlugin(name, name, string.Empty, string.Empty, string.Empty, [], []) : ListedPlugin.Of(described);

            waiting.Remove(plugin.Assembly, out var next);
            listed.Add(new HubInstalled(plugin, next?.Description.Version, Loaded: false, described?.Preview?.Bytes, removing.Contains(plugin.Assembly), Trouble: troubles[folder]));
        }

        listed.AddRange(waiting.Values.Select(p =>
            new HubInstalled(ListedPlugin.Of(p.Description), p.Description.Version, Loaded: false, p.Description.Preview?.Bytes)));

        return listed;
    }

}
