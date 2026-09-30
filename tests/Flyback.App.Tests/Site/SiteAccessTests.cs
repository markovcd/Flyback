using Flyback.App.Site;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Site;

/// <summary>What the editor asks the preset site for, by where it runs.</summary>
public sealed class SiteAccessTests
{
    private static readonly Uri Root = new("http://site.test/");

    [Fact]
    public void A_page_asks_its_site_for_presets_but_never_for_plugins_to_install()
    {
        using var provider = EditorServices.Provider(new EditorSetup { Host = new() { InPage = true, PresetSite = Root } });
        var page = provider.GetRequiredService<SiteAccess>();

        page.Presets().ShouldNotBeNull();
        page.Plugins().ShouldBeNull();
    }

    [Fact]
    public void A_desktop_window_asks_its_site_for_both()
    {
        using var provider = EditorServices.Provider(new EditorSetup { Host = new() { PresetSite = Root } });
        var window = provider.GetRequiredService<SiteAccess>();

        window.Presets().ShouldNotBeNull();
        window.Plugins().ShouldNotBeNull();
    }
}
