using Flyback.Plugins.Hosting;
using Flyback.Site.Reading;
using Shouldly;
using Xunit;

namespace Flyback.Site.Tests;

public sealed class BrowserPluginsTests
{
    /// <summary>A plugin the pages link that the site cannot load would report every preset using it as lacking.</summary>
    [Fact]
    public void Every_plugin_the_pages_link_loads()
    {
        var linked = PluginHost.LoadLinked(typeof(BrowserPlugins).Assembly, "WebPlugin");

        linked.Problems.ShouldBeEmpty();
    }
}
