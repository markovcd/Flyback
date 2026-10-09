using Flyback.Plugins.Hosting;

namespace Flyback.Specs.Support;

/// <summary>The plugins laid out under <c>plugins\</c>, loaded once for the run and trusted as shipped.</summary>
internal static class ShippedPlugins
{
    private static readonly Lazy<PluginCatalog> Catalog =
        new(() => PluginHost.Load(PluginHost.DefaultDirectory, PluginTrust.Shipped(PluginHost.DefaultDirectory)));

    public static PluginCatalog Loaded => Catalog.Value;
}
