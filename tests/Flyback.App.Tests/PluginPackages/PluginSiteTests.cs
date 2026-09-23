using Flyback.App.PluginPackages;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.PluginPackages;

/// <summary>The plugin site as the plugins window reads it.</summary>
public sealed class PluginSiteTests
{
    [Fact]
    public async Task A_search_asks_for_this_systems_plugins_by_words_tag_and_page()
    {
        using var fake = new FakePluginSite();

        await fake.Site().SearchAsync(" warm reverb ", "space", 2, CancellationToken.None);

        var asked = fake.Asked.ShouldHaveSingleItem();

        asked.AbsolutePath.ShouldBe("/api/v1/plugins");
        asked.Query.ShouldContain("q=warm%20reverb");
        asked.Query.ShouldContain("tag=space");
        asked.Query.ShouldContain("page=2");
        asked.Query.ShouldContain($"platform={PluginPackage.ThisPlatform}");
    }

    [Fact]
    public async Task A_listed_plugin_is_read_with_its_file_on_the_site()
    {
        using var fake = new FakePluginSite(new Shared("a1", "Ripple", Author: "Ann", Tags: ["water"], Modules: ["Ripple", "Halve"], Package: [1, 2, 3]));

        var found = await fake.Site().SearchAsync(null, null, 1, CancellationToken.None);
        var plugin = found.Items.ShouldHaveSingleItem();

        plugin.Plugin.Name.ShouldBe("Ripple");
        plugin.Plugin.Author.ShouldBe("Ann");
        plugin.Plugin.Tags.ShouldBe(["water"]);
        plugin.Plugin.Modules.ShouldBe(["Ripple", "Halve"]);
        plugin.File.ShouldBe(new Uri("http://site.test/api/v1/plugins/a1/file"));
        found.More.ShouldBeFalse();
    }

    [Fact]
    public async Task A_download_that_is_not_the_listed_package_is_refused()
    {
        using var fake = new FakePluginSite(new Shared("a1", "Ripple", Package: [1, 2, 3]));
        var site = fake.Site();
        var plugin = (await site.SearchAsync(null, null, 1, CancellationToken.None)).Items[0];

        (await site.DownloadAsync(plugin, CancellationToken.None)).ShouldBe(new byte[] { 1, 2, 3 });

        fake.Tampered = [1, 2, 4];

        await Should.ThrowAsync<InvalidDataException>(() => site.DownloadAsync(plugin, CancellationToken.None));
    }

    [Theory]
    [InlineData("ripple", true)]
    [InlineData("RIPPLE ann", true)]
    [InlineData("halve", true)]
    [InlineData("wat", true)]
    [InlineData("Shared", true)]
    [InlineData("ripple bob", false)]
    public void A_search_matches_every_word_somewhere_in_the_plugin(string search, bool matches)
    {
        var plugin = new ListedPlugin("Flyback.Plugins.Shared", "Ripple", "1.0.0", "Ann", "Rings on a pond.", ["water"], ["Halve"]);

        plugin.Matches(search).ShouldBe(matches);
    }

    [Fact]
    public void A_tag_matches_only_the_whole_tag()
    {
        var plugin = new ListedPlugin("A", "Ripple", "1.0.0", "", "", ["water"], []);

        plugin.Matches(null, "water").ShouldBeTrue();
        plugin.Matches(null, "wat").ShouldBeFalse();
    }

    /// <summary>What a proxy or an older site might answer is a listing with nothing in it, not an error nobody catches.</summary>
    [Theory]
    [InlineData("""{"items":[],"total":"5"}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    public void A_listing_of_another_shape_lists_nothing(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        PluginSite.Read(document.RootElement, new Uri("http://site.test/")).Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("file:///C:/ripple.fbkp")]
    [InlineData("ftp://site.test/ripple.fbkp")]
    public void A_plugin_listed_somewhere_other_than_the_web_is_left_out(string file)
    {
        using var document = System.Text.Json.JsonDocument.Parse($$"""{"items":[{"id":"a1","name":"Ripple","sha256":"{{new string('0', 64)}}","file":"{{file}}"}]}""");

        PluginSite.Read(document.RootElement, new Uri("http://site.test/")).Items.ShouldBeEmpty();
    }
}
