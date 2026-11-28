using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Editor.PluginPackages;
using Flyback.Editor.Tests.Packs;
using Flyback.Plugins.Hosting;
using Shouldly;

namespace Flyback.Editor.Tests.PluginPackages;

/// <summary>The plugins window's module packs from the preset site: listed, installed, and told apart from what is installed.</summary>
public sealed class PackSiteSectionTests : EditorTest
{
    private const string Echoes = "pack ada.echoes 1.2.0\nmodule wobble(rate = 3) = sine(freq: rate)\n";

    private (PackSiteSection Section, Window Window) Open(Dictionary<string, string> installed, Func<SitePack, Task<string?>> install)
    {
        var search = new TextBox();
        var section = new PackSiteSection(new FakePackSite(Echoes).Site(), () => installed, install, search);
        var window = Show(new StackPanel { Children = { search, section.View } }, width: 640);

        _ = section.AskAsync();
        Pump(() => All<Grid>(section.View).Any(row => row.Tag is SitePack), window);

        return (section, window);
    }

    [AvaloniaFact]
    public void A_shared_pack_is_listed_with_its_modules_and_installs_on_a_press()
    {
        var installed = new Dictionary<string, string>();
        SitePack? asked = null;

        var (section, window) = Open(installed, pack =>
        {
            asked = pack;
            installed[pack.Pack] = pack.Version;
            return Task.FromResult<string?>("ada.echoes 1.2.0 is installed.");
        });

        using (section)
        {
            section.View.IsVisible.ShouldBeTrue();
            All<TextBlock>(section.View).Select(t => t.Text).ShouldContain("ada.echoes 1.2.0");
            All<TextBlock>(section.View).Select(t => t.Text).ShouldContain("Wobble");

            Press(Named<Button>(section.View, "installPack"));
            Pump(() => All<TextBlock>(section.View).Any(t => t.Name == "packState"), window);

            asked.ShouldNotBeNull().Pack.ShouldBe("ada.echoes");
            Named<TextBlock>(section.View, "packState").Text.ShouldBe("Installed");
            Named<TextBlock>(section.View, "packNotice").Text.ShouldBe("ada.echoes 1.2.0 is installed.");
        }
    }

    [AvaloniaFact]
    public void An_older_installed_version_is_offered_an_update()
    {
        var (section, _) = Open(new() { ["ada.echoes"] = "1.0.0" }, _ => Task.FromResult<string?>(null));

        using (section) Named<Button>(section.View, "installPack").Content.ShouldBe("Update");
    }

    [AvaloniaFact]
    public void A_site_with_no_packs_shows_no_section()
    {
        var search = new TextBox();
        var empty = new FakePackSite(Echoes) { Empty = true };
        using var section = new PackSiteSection(empty.Site(), () => new Dictionary<string, string>(), _ => Task.FromResult<string?>(null), search);
        var window = Show(new StackPanel { Children = { search, section.View } }, width: 640);

        _ = section.AskAsync();
        Pump(() => Named<TextBlock>(section.View, "packStatus").Text is { Length: > 0 } said && said != "Looking…", window);

        section.View.IsVisible.ShouldBeFalse();
    }
}
