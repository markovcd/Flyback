using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

public class PluginReportTests
{
    [Fact]
    public void Every_loaded_plugin_is_named_under_the_folder_it_came_from()
    {
        var catalog = PluginHost.Load();

        var lines = PluginReport.Lines(catalog, PluginHost.DefaultDirectory).ToList();

        lines[0].ShouldBe($"plugins: {PluginHost.DefaultDirectory}");

        foreach (var plugin in catalog.Plugins)
            lines.ShouldContain($"  loaded {plugin.Info.Name}  ({plugin.Info.Id})");
    }

    [Fact]
    public void An_empty_folder_says_nothing_loaded()
    {
        PluginReport.Lines(PluginCatalog.Empty, "nowhere").ShouldBe(["plugins: nowhere", "  nothing loaded"]);
    }
}
