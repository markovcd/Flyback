using System.Diagnostics;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// Everything that has to happen before there is a window. Plugins are read
/// once here rather than by the window, because the module catalogue must be
/// final before a palette is built or a patch is opened — and because a second
/// window must never get a second answer.
/// </summary>
internal static class Startup
{
    public static PluginCatalog Plugins { get; private set; } = PluginCatalog.Empty;

    public static void Load()
    {
        Plugins = PluginHost.Load();
        NodeCatalog.Install(Plugins.Modules);

        Announce(Plugins);
    }

    /// <summary>
    /// What the scan found, on the terminal.
    /// </summary>
    /// <remarks>
    /// The window says as much in a tooltip, which is no use to somebody who
    /// started the program from a shell to find out why their plugin is not in
    /// the list — and a plugin that failed to load failed here, before there was
    /// a window to hang a tooltip on. Where it looked is said whatever the
    /// answer was, because an empty folder and the wrong folder read identically
    /// from a list of nothing.
    /// </remarks>
    private static void Announce(PluginCatalog catalog)
    {
        Trace.WriteLine($"plugins: {PluginHost.DefaultDirectory}");

        if (catalog.Plugins.Count == 0) Trace.WriteLine("  nothing loaded");

        foreach (var plugin in catalog.Plugins)
            Trace.WriteLine($"  loaded {plugin.Info.Name}  ({plugin.Info.Id})");

        foreach (var problem in catalog.Problems)
            Trace.WriteLine($"  problem: {problem}");
    }
}
