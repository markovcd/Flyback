using Flyback.Plugins.Hosting;

namespace Flyback.Plugins.Tests;

/// <summary>The shipped plugins, loaded once for the whole assembly.</summary>
/// <remarks>
/// Each load puts every plugin into a load context of its own, and under coverage each of
/// those is another module to instrument: loading per test ran the coverage job's pool up
/// to thousands of threads.
/// </remarks>
internal static class ShippedPlugins
{
    public static PluginCatalog Loaded { get; } = PluginHost.Load();
}
