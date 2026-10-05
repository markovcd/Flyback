using System.Text.Json;
using Flyback.Editor.PluginPackages;
using Flyback.Editor.Site;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Site;

/// <summary>
/// The editor's site clients read what the Worker answers. The listings are the ones the
/// Worker's own tests hold it to (worker/test/fixtures/contract), so a change on either
/// side that a shipped editor would read wrong fails one of the two.
/// </summary>
public sealed class SiteContractTests
{
    private static readonly Uri Root = new("https://presets.example.org/");

    private static JsonDocument Recorded(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository(), "worker", "test", "fixtures", "contract", name + ".json")));

    [Fact]
    public void The_preset_listing_reads_as_the_gallery_shows_it()
    {
        using var recorded = Recorded("presets");

        var page = PresetSite.Read(recorded.RootElement, Root);
        var preset = page.Items.ShouldHaveSingleItem();

        page.Total.ShouldBe(1);
        page.PageSize.ShouldBe(24);
        preset.Id.ShouldBe("0199a000000070008000000000000001");
        preset.Name.ShouldBe("Łódź lantern");
        preset.Author.ShouldBe("Ada");
        preset.Tags.ShouldBe(["ambient", "glow"]);
        preset.FileName.ShouldBe("Lantern.fbk");
        preset.File.ShouldBe(new Uri(Root, "/api/v1/presets/0199a000000070008000000000000001/file"));
        preset.Still.ShouldBe(new Uri(Root, "/media/0199a000000070008000000000000001.webp"));
        preset.Rating.ShouldBe(new SiteRating(4, 1));
        preset.PageLacks.ShouldBe("Needs the Lantern plugin");
    }

    [Fact]
    public void The_plugin_listing_reads_as_the_plugins_window_shows_it()
    {
        using var recorded = Recorded("plugins");

        var plugin = PluginSite.Read(recorded.RootElement, Root).Items.ShouldHaveSingleItem();

        plugin.Plugin.Assembly.ShouldBe("Flyback.Plugins.Figures");
        plugin.Plugin.Name.ShouldBe("Figures");
        plugin.Plugin.Modules.ShouldBe(["Circle"]);
        plugin.Builds.ShouldBe(["win", "linux"]);
        plugin.Sha256.Length.ShouldBe(64);
        plugin.Downloads.ShouldBe(3);
        plugin.File.ShouldBe(new Uri(Root, "/api/v1/plugins/0199a000000070008000000000000003/file"));
        plugin.Preview.ShouldBe(new Uri(Root, "/api/v1/plugins/0199a000000070008000000000000003/preview"));
    }

    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
            if (File.Exists(Path.Combine(at.FullName, "Flyback.slnx"))) return at.FullName;

        throw new InvalidOperationException("The tests are not running inside the repository.");
    }
}
