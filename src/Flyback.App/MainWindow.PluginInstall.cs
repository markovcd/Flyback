using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.App.PluginPackages;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>Installing or updating a plugin from a <c>.fbkp</c> opened with Flyback or found in the plugins window (ADR-0132).</summary>
public sealed partial class MainWindow
{
    /// <summary>Where a package's plugin is installed, or null where this window installs nothing.</summary>
    private readonly string? pluginFolder;

    /// <summary>
    /// Starts Flyback again once this window has closed, with a patch for it to open,
    /// or null where a restart is not offered.
    /// </summary>
    private readonly Action<Reopen?>? relaunch;

    /// <summary>
    /// The patch the plugins window was opened for, which a restart from inside it opens
    /// again — by then Flyback has the plugin it was refused for. Null at every other
    /// moment, so installing something unrelated reopens nothing.
    /// </summary>
    private Reopen? refused;

    /// <summary>
    /// What that patch was short of, so an install can say how many are still to come.
    /// Empty at every other moment.
    /// </summary>
    private IReadOnlyList<SitePlugin> awaited = [];

    /// <summary>Where the gallery lists shared presets from and the plugins window shared plugins, or null for nowhere.</summary>
    private readonly Uri? presetSite;

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
            offerRestart: relaunch is not null && awaiting == 0,
            removable,
            awaiting);
        var answer = await this.ShowDialog<PluginAnswer>(PluginInstallView.Title(change), view);

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

        if (answer == PluginAnswer.InstallAndRestart && await RestartAsync()) return null;

        return $"{name} is {(change == PluginChange.Update ? "updated" : "installed")}, and loads the next time Flyback starts.";
    }

    /// <summary>
    /// The plugins window: what is installed, and what the plugin site offers, to
    /// search and install from.
    /// </summary>
    /// <param name="wanted">What a patch was short of, listed first, or null for an ordinary opening.</param>
    private async Task ShowPluginsAsync(IReadOnlyList<SitePlugin>? wanted = null)
    {
        var site = presetSite is null ? null : new PluginSite(SiteHttp ?? SiteClient.Value, presetSite);
        var run = PluginSummary.Run(plugins, pluginFolder ?? PluginHost.DefaultDirectory, sound.Failure);
        using var hub = new PluginHub(site, () => { var assisting = Assisting(); return Task.Run(() => InstalledPlugins(assisting)); }, (plugin, downloaded) => InstallFromSiteAsync(site!, plugin, downloaded), plugin => ShowInstalledAsync(site, plugin), wanted, run);

        // Read before the window goes up, so the rows do not arrive above whatever is showing.
        await hub.RereadAsync();
        _ = hub.AskSiteAsync();

        await this.ShowDialog<object?>("Plugins", hub.View, hub.Header, fill: true);
    }

    /// <summary>
    /// How many of what the patch was short of would still be missing after installing
    /// <paramref name="installing"/> — counting one waiting for the next start as had,
    /// since one start loads every plugin staged by then.
    /// </summary>
    private int Awaiting(PluginInstaller? installer, string? installing)
    {
        if (awaited.Count == 0 || installer is null) return 0;

        var had = plugins.Plugins
            .Select(plugin => Path.GetFileNameWithoutExtension(plugin.AssemblyPath))
            .Concat(installer.Waiting().Select(waiting => waiting.Description.Assembly))
            .Concat(installing is null ? [] : [installing]);

        return MissingPlugins.StillNeeded(awaited, had);
    }

    /// <summary>Shared by every question put to the preset site, as an <see cref="HttpClient"/> is meant to be.</summary>
    private static readonly Lazy<HttpClient> SiteClient = new(() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

    /// <summary>What the site is asked with in place of <see cref="SiteClient"/>, for a test.</summary>
    internal HttpClient? SiteHttp { get; init; }

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

            return (pluginFolder is null ? null : Path.Combine(pluginFolder, assembly), waiting, waiting?.Description);
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

        return await this.ShowDialog<PluginAnswer>(plugin.Plugin.Name, view) switch
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
    private IReadOnlyList<HubInstalled> InstalledPlugins((string Assembly, string Said)? assisting)
    {
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

            listed.Add(new HubInstalled(plugin, next?.Description.Version, Loaded: true, described?.Preview?.Bytes, removing.Contains(plugin.Assembly), said));
        }

        listed.AddRange(waiting.Values.Select(p =>
            new HubInstalled(ListedPlugin.Of(p.Description), p.Description.Version, Loaded: false, p.Description.Preview?.Bytes)));

        return listed;
    }

    /// <summary>The folder of the plugin whose assistant Ask sends a patch to, and where the patch and its key go, or null where none is chosen.</summary>
    private (string Assembly, string Said)? Assisting()
    {
        if (assistant?.Chosen is not { } chosen || plugins.Provider(chosen) is not { } info) return null;
        if (plugins.Plugins.FirstOrDefault(p => p.Info.Id == info.Id) is not { } loaded) return null;

        return (Path.GetFileNameWithoutExtension(loaded.AssemblyPath), string.Join(Environment.NewLine, PluginSummary.Assistant(plugins, assistant.Summary)));
    }

    /// <summary>
    /// Closes the window, asking about unsaved work as any close does, and starts
    /// Flyback again behind it. False where the window stays: the question was
    /// canceled, or a recording is running, which only its own button should end.
    /// </summary>
    private async Task<bool> RestartAsync()
    {
        if (Recording.InHand || !await MayReplaceThePatchAsync()) return false;

        relaunch!(refused);

        leaving = true;
        Close();

        return true;
    }
}
