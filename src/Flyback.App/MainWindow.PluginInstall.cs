using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.App.PluginPackages;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>What the window keeps for <see cref="PluginInstalls"/>: where plugins go, where the site is, and how to start again.</summary>
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

    /// <summary>Where the gallery lists shared presets from and the plugins window shared plugins, or null for nowhere.</summary>
    private readonly Uri? presetSite;

    /// <summary>Shared by every question put to the preset site, as an <see cref="HttpClient"/> is meant to be.</summary>
    private static readonly Lazy<HttpClient> SiteClient = new(() => new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

    /// <summary>What the site is asked with in place of <see cref="SiteClient"/>, for a test.</summary>
    internal HttpClient? SiteHttp { get; init; }

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
