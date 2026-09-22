namespace Flyback.Plugins.Hosting;

/// <summary>What a scan of the plugin folder found, as the lines every program prints to its terminal.</summary>
/// <remarks>
/// Where it looked comes first whatever the answer, because an empty folder and the
/// wrong folder read identically from a list of nothing.
/// </remarks>
internal static class PluginReport
{
    public static IEnumerable<string> Lines(PluginCatalog catalog, string directory)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        yield return $"plugins: {directory}";

        if (catalog.Plugins.Count == 0) yield return "  nothing loaded";

        foreach (var plugin in catalog.Plugins)
            yield return $"  loaded {plugin.Info.Name}  ({plugin.Info.Id})";

        foreach (var problem in catalog.Problems)
            yield return $"  problem: {problem}";
    }
}
