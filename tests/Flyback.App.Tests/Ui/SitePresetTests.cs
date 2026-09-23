using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Ui;

/// <summary>The presets shared on the preset site, listed in the gallery after everything on this machine.</summary>
public sealed class SitePresetTests : UiTest
{
    private const string Program = GlobalConstants.ApplicationName;

    private (Window Window, GalleryParts Parts) Gallery(FakePresetSite site)
    {
        var parts = PresetGallery.Build(
            [.. Presets.All.OrderBy(preset => preset.Kind)],
            showing: null,
            new PresetThumbnails(NodeCatalog.BuiltIn),
            site: site.Site());

        var content = new DockPanel();

        DockPanel.SetDock(parts.Filter, Dock.Top);
        content.Children.Add(parts.Filter);
        content.Children.Add(parts.Tiles);

        var window = Show(content, width: 900);

        Pump(() => Status(parts) != "Looking…");
        Settle(window);

        return (window, parts);
    }

    /// <summary>The site answers on the thread pool, and a search waits a quarter of a second for typing to stop.</summary>
    private static void Pump(Func<bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);

        while (!until() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static string?[] Shared(Control root) =>
        [.. All<Button>(root).Where(b => b.Name == "site-tile").Select(b => ((SitePreset)b.Tag!).Name)];

    private static string? Status(GalleryParts parts) => All<TextBlock>(parts.Tiles).Single(t => t.Name == "site-status").Text;

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static byte[] PatchFile()
    {
        var patch = new Patch();

        patch.EnsureOutput();
        patch.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 100, 100));

        return System.Text.Encoding.UTF8.GetBytes(PatchIO.ToJson(patch));
    }

    /// <summary>
    /// How a preset opened from the site is found again after a restart, which is what
    /// installing a plugin it needed costs.
    /// </summary>
    [Fact]
    public async Task One_shared_preset_is_found_by_its_id()
    {
        using var site = new FakePresetSite(new Posted("a1", "Nebula", Author: "Ann"), new Posted("b1", "Drift"));

        var found = await site.Site().FindAsync("a1", CancellationToken.None);

        found.ShouldNotBeNull().Name.ShouldBe("Nebula");
        found.Author.ShouldBe("Ann");
        site.Asked.ShouldHaveSingleItem().AbsolutePath.ShouldBe("/api/v1/presets/a1");
    }

    [Fact]
    public async Task A_preset_the_site_no_longer_has_is_not_found()
    {
        using var site = new FakePresetSite(new Posted("a1", "Nebula"));

        (await site.Site().FindAsync("gone", CancellationToken.None)).ShouldBeNull();
    }

    /// <summary>What a proxy or an older site might answer is a listing with nothing in it, not an error nobody catches.</summary>
    [Theory]
    [InlineData("""{"items":[],"total":"5"}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    public void A_listing_of_another_shape_lists_nothing(string json)
    {
        using var document = JsonDocument.Parse(json);

        PresetSite.Read(document.RootElement, FakePresetSite.Root).Items.ShouldBeEmpty();
    }

    [Fact]
    public void A_rating_of_another_shape_is_no_rating()
    {
        using var document = JsonDocument.Parse("""{"rating":{"count":"3","average":4}}""");

        SiteRating.Read(document.RootElement).ShouldBe(SiteRating.None);
    }

    [AvaloniaFact]
    public void The_gallery_lists_what_the_preset_site_shares_after_the_presets_here()
    {
        using var site = new FakePresetSite(new Posted("a", "Aurora", "Ann"), new Posted("b", "Breakers", "Bob"));
        var (_, parts) = Gallery(site);

        Shared(parts.Tiles).ShouldBe(["Aurora", "Breakers"]);
        All<Button>(parts.Tiles).ShouldContain(b => b.Name == "tile", "the presets here are still listed");
        All<TextBlock>(parts.Tiles).ShouldContain(t => t.Text == PresetGallery.SiteHeading);
    }

    [AvaloniaFact]
    public void Typing_in_the_filter_asks_the_site_for_what_matches()
    {
        using var site = new FakePresetSite(new Posted("a", "Aurora", "Ann"), new Posted("b", "Breakers", "Bob"));
        var (window, parts) = Gallery(site);

        parts.Filter.Text = "bob";
        Pump(() => Shared(parts.Tiles).Length == 1);
        Settle(window);

        Shared(parts.Tiles).ShouldBe(["Breakers"]);
        site.Asked.ShouldContain(uri => uri.Query.Contains("q=bob"));
    }

    [AvaloniaFact]
    public void A_search_the_site_has_nothing_for_says_so()
    {
        using var site = new FakePresetSite(new Posted("a", "Aurora"));
        var (window, parts) = Gallery(site);

        parts.Filter.Text = "plasma";
        Pump(() => Shared(parts.Tiles).Length == 0 && Status(parts) != "Looking…");
        Settle(window);

        Status(parts).ShouldBe("Nothing on the preset site matches “plasma”.");
        All<Button>(parts.Tiles).ShouldContain(b => b.Name == "tile" && b.IsVisible, "Plasma is still found here");
    }

    [AvaloniaFact]
    public void More_brings_the_next_page_of_shared_presets()
    {
        using var site = new FakePresetSite(new Posted("a", "Aurora"), new Posted("b", "Breakers")) { PageSize = 1 };
        var (window, parts) = Gallery(site);

        var more = All<Button>(parts.Tiles).Single(b => b.Name == "more-presets");

        more.IsVisible.ShouldBeTrue();

        Press(more);
        Pump(() => Shared(parts.Tiles).Length == 2);
        Settle(window);

        Shared(parts.Tiles).ShouldBe(["Aurora", "Breakers"]);
        more.IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_site_that_does_not_answer_says_so_and_leaves_the_presets_here()
    {
        var parts = PresetGallery.Build(
            [.. Presets.All.OrderBy(preset => preset.Kind)],
            showing: null,
            new PresetThumbnails(NodeCatalog.BuiltIn),
            site: new PresetSite(new HttpClient(new Unreachable()), FakePresetSite.Root));

        var window = Show(parts.Tiles, width: 900);

        Pump(() => Status(parts)!.Contains("did not answer"));
        Settle(window);

        Status(parts).ShouldBe($"The preset site at {FakePresetSite.Root} did not answer.");
        All<Button>(parts.Tiles).ShouldContain(b => b.Name == "tile");
    }

    [AvaloniaFact]
    public void Picking_a_shared_preset_opens_it_under_its_name()
    {
        using var site = new FakePresetSite(new Posted("n1", "Nebula", "Ann", File: PatchFile()));

        var window = Owned(new MainWindow(presetSite: FakePresetSite.Root) { SiteHttp = new HttpClient(site) });

        window.Show();
        Settle(window);

        Press(All<Button>(window).Single(b => b.Name == "presets-glyph"));
        Pump(() => All<Button>(window).Any(b => b.Name == "site-tile"));
        Settle(window);

        site.Asked.ShouldNotContain(uri => uri.AbsolutePath.EndsWith("/file"), "nothing is downloaded before one is picked");

        Press(All<Button>(window).Single(b => b.Name == "site-tile"));
        Pump(() => window.Title == $"Nebula — {Program}");
        Settle(window);

        window.Title.ShouldBe($"Nebula — {Program}");
        All<ModalOverlay>(window).ShouldBeEmpty();
        All<NodeEditor>(window).Single().Patch.Nodes.ShouldContain(n => n.TypeId == "value");
    }

    [AvaloniaFact]
    public void The_listing_is_read_with_its_links_resolved_and_a_preset_with_no_file_left_out()
    {
        using var document = JsonDocument.Parse("""
            {
              "items": [
                { "id": "a", "name": "Aurora", "author": "Ann", "tags": ["calm"], "fileName": "aurora.fbkb",
                  "file": "/api/v1/presets/a/file", "media": { "still": "/media/a.webp" } },
                { "id": "b", "name": "No file" }
              ],
              "total": 30, "page": 1, "pageSize": 24
            }
            """);

        var page = PresetSite.Read(document.RootElement, FakePresetSite.Root);

        var aurora = page.Items.ShouldHaveSingleItem();

        aurora.File.ShouldBe(new Uri("http://site.test/api/v1/presets/a/file"));
        aurora.Still.ShouldBe(new Uri("http://site.test/media/a.webp"));
        aurora.Tags.ShouldBe(["calm"]);
        aurora.FileName.ShouldBe("aurora.fbkb");
        page.More.ShouldBeTrue();
    }

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection could be made.");
    }

    [AvaloniaFact]
    public void A_shared_preset_is_reported_from_its_tile()
    {
        using var site = new FakePresetSite(new Posted("a", "Aurora", "Ann"));
        var (window, parts) = Gallery(site);

        var tile = All<Button>(parts.Tiles).Single(b => b.Name == "site-tile");
        var report = ((MenuFlyout)tile.ContextFlyout!).Items.OfType<MenuItem>().Single(m => m.Name == "report-preset");

        report.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Pump(() => All<RadioButton>(window).Any());

        All<RadioButton>(window).Single(r => (string?)r.Tag == "stolen").IsChecked = true;
        Press(All<Button>(window).Single(b => b.Name == "send"));
        Pump(() => site.Reports.Count == 1 && Status(parts)!.StartsWith("Reported", StringComparison.Ordinal));

        site.Reports.ShouldHaveSingleItem().Path.ShouldBe("/api/v1/presets/a/reports");
        Status(parts).ShouldBe("Reported “Aurora” to the preset site's admin.");
        All<TextBlock>(parts.Tiles).Single(t => t.Name == "site-status").IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_shared_preset_shows_its_stars_to_read()
    {
        using var site = new FakePresetSite(new Posted("a", "Aurora", Average: 2.6, Ratings: 1), new Posted("b", "Bloom"));
        var (_, parts) = Gallery(site);

        TextBlock Stars(string id) => All<TextBlock>(All<Button>(parts.Tiles).Single(b => b.Tag is SitePreset p && p.Id == id))
            .Single(t => t.Name == "siteRating");

        ((SitePreset)All<Button>(parts.Tiles).Single(b => b.Tag is SitePreset { Id: "a" }).Tag!).Rating.Stars.ShouldBe(3);
        Stars("a").Inlines!.Text.ShouldBe("★★★★★  2.6 (1 rating)");
        Stars("b").Inlines!.Text.ShouldBe("★★★★★  Not rated yet");
        site.Asked.ShouldNotContain(u => u.AbsolutePath.EndsWith("/rating", StringComparison.Ordinal), "ratings are given on the site");
    }
}
