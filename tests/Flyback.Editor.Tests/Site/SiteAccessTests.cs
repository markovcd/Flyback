using Flyback.Editor.Shell;
using Flyback.Editor.Site;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Flyback.Editor.Composition;
using Flyback.Editor.PluginPackages;

namespace Flyback.Editor.Tests.Site;

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
        PluginSite.For(page, provider.GetRequiredService<EditorHost>()).ShouldBeNull();
    }

    [Fact]
    public void A_desktop_window_asks_its_site_for_both()
    {
        using var provider = EditorServices.Provider(new EditorSetup { Host = new() { PresetSite = Root } });
        var window = provider.GetRequiredService<SiteAccess>();

        window.Presets().ShouldNotBeNull();
        PluginSite.For(window, provider.GetRequiredService<EditorHost>()).ShouldNotBeNull();
    }
}
