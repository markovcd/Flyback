using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Engine.Graph;
using Flyback.Editor.Gallery;
using Flyback.Editor.Site;
using Flyback.Core;
using Flyback.Core.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Flyback.Tests;

namespace Flyback.Editor.Tests.Site;

/// <summary>
/// What the preset site answers, read without a window: HttpClient and JSON. Apart
/// from <see cref="SitePresetTests"/> so that it runs off the one UI thread headless
/// gives the assembly rather than queueing on it.
/// </summary>
public sealed class SiteAnswerTests
{
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

    /// <summary>A proxy's error is a site not answering, which is not a site saying the preset is gone.</summary>
    [Fact]
    public async Task A_site_answering_an_error_is_not_a_preset_taken_down()
    {
        using var site = new FakePresetSite(new Posted("a1", "Nebula")) { Answering = System.Net.HttpStatusCode.BadGateway };

        await Should.ThrowAsync<HttpRequestException>(() => site.Site().FindAsync("a1", CancellationToken.None));
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

    /// <summary>Said as the site says it, and something rather than nothing from a site that says no more than that it lacks.</summary>
    [Theory]
    [InlineData("""{"plugins":[{"id":"lantern","name":"Lantern"}],"modules":2,"said":"Needs the Lantern plugin"}""", "Needs the Lantern plugin")]
    [InlineData("""{"plugins":[{"name":"A"}],"modules":1}""", "Needs modules a browser lacks")]
    [InlineData("""null""", null)]
    public void What_a_page_lacks_to_open_a_preset_is_read_from_the_listing(string lacks, string? said)
    {
        using var document = JsonDocument.Parse($$"""{"id":"a","name":"Aurora","file":"/api/v1/presets/a/file","lacks":{{lacks}}}""");

        PresetSite.One(document.RootElement, FakePresetSite.Root).ShouldNotBeNull().PageLacks.ShouldBe(said);
    }

    [Fact]
    public void A_rating_of_another_shape_is_no_rating()
    {
        using var document = JsonDocument.Parse("""{"rating":{"count":"3","average":4}}""");

        SiteRating.Read(document.RootElement).ShouldBe(SiteRating.None);
    }
}
