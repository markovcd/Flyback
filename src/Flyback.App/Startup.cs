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
    }
}
