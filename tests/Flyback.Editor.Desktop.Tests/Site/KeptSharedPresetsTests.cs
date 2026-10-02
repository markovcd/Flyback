using System.Globalization;
using System.Text.Json;
using Flyback.Editor.Site;
using Flyback.Editor.Desktop.Tests.Ui;
using Flyback.Engine.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Desktop.Tests.Site;

/// <summary>The shared presets opened before, kept on disk to open while the preset site does not answer.</summary>
public sealed class KeptSharedPresetsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-shared-" + Guid.NewGuid().ToString("N"));

    private static readonly Uri Elsewhere = new("http://other.test/");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static SitePreset Listed(string json, Uri? root = null)
    {
        using var document = JsonDocument.Parse(json);

        return PresetSite.One(document.RootElement, root ?? FakePresetSite.Root).ShouldNotBeNull();
    }

    private static SitePreset Nebula(double average = 4.5, int count = 2) => Listed($$"""
        { "id": "n1", "name": "Nebula", "author": "Ann", "description": "Gas and dust.", "tags": ["space", "calm"],
          "fileName": "nebula.fbkb", "size": 1234, "submitted": "2026-09-01T10:00:00+00:00", "downloads": 42,
          "published": true, "file": "/api/v1/presets/n1/file", "media": { "still": "/media/n1.webp", "state": "ready" },
          "rating": { "average": {{average.ToString(CultureInfo.InvariantCulture)}}, "count": {{count}} } }
        """);

    [Fact]
    public void A_kept_preset_keeps_everything_the_site_said_of_it()
    {
        var kept = new KeptSharedPresets(folder);
        var nebula = Nebula();

        kept.Keep(FakePresetSite.Root, nebula, [1, 2, 3], [9, 8]);

        var found = new KeptSharedPresets(folder).Find(FakePresetSite.Root, "n1").ShouldNotBeNull();

        found.Preset.Listed.ShouldBe(nebula.Listed);
        found.Preset.Rating.ShouldBe(new SiteRating(4.5, 2));
        found.Preset.Tags.ShouldBe(["space", "calm"]);
        found.Preset.Author.ShouldBe("Ann");
        found.Preset.FileName.ShouldBe("nebula.fbkb");
        found.Preset.Still.ShouldBe(new Uri("http://site.test/media/n1.webp"));
        found.Preset.Listed.ShouldContain("\"downloads\": 42");
        kept.File(found).ShouldBe([1, 2, 3]);
        kept.Still(found).ShouldBe([9, 8]);
    }

    [Fact]
    public void Opening_it_again_with_no_still_keeps_the_still_from_before()
    {
        var kept = new KeptSharedPresets(folder);

        kept.Keep(FakePresetSite.Root, Nebula(), [1], [9]);
        kept.Keep(FakePresetSite.Root, Nebula(), [2], still: null);

        var found = kept.Find(FakePresetSite.Root, "n1").ShouldNotBeNull();

        kept.File(found).ShouldBe([2]);
        kept.Still(found).ShouldBe([9]);
    }

    [Fact]
    public void A_listing_brings_a_kept_preset_its_new_rating_and_leaves_its_file()
    {
        var kept = new KeptSharedPresets(folder);

        kept.Keep(FakePresetSite.Root, Nebula(4.5, 2), [1, 2, 3], [9]);
        var opened = kept.Find(FakePresetSite.Root, "n1").ShouldNotBeNull().Opened;

        kept.Refresh(FakePresetSite.Root, Nebula(3, 5));

        var found = kept.Find(FakePresetSite.Root, "n1").ShouldNotBeNull();

        found.Preset.Rating.ShouldBe(new SiteRating(3, 5));
        found.Opened.ShouldBe(opened);
        kept.File(found).ShouldBe([1, 2, 3]);
        kept.Still(found).ShouldBe([9]);
    }

    [Fact]
    public void A_listing_keeps_nothing_that_was_never_opened()
    {
        new KeptSharedPresets(folder).Refresh(FakePresetSite.Root, Nebula());

        new KeptSharedPresets(folder).All(FakePresetSite.Root).ShouldBeEmpty();
    }

    [Fact]
    public void Each_site_lists_only_what_was_opened_from_it_the_last_opened_first()
    {
        var kept = new KeptSharedPresets(folder);

        kept.Keep(FakePresetSite.Root, Nebula(), [1], null);
        kept.Keep(FakePresetSite.Root, Listed("""{ "id": "d1", "name": "Drift", "file": "/f" }"""), [2], null);
        kept.Keep(Elsewhere, Listed("""{ "id": "n1", "name": "Other nebula", "file": "/f" }""", Elsewhere), [3], null);

        kept.All(FakePresetSite.Root).Select(k => k.Name).ShouldBe(["Drift", "Nebula"]);
        kept.All(Elsewhere).Select(k => k.Name).ShouldBe(["Other nebula"]);
        kept.Find(Elsewhere, "d1").ShouldBeNull();
    }

    [Fact]
    public void An_id_never_names_a_path()
    {
        var kept = new KeptSharedPresets(folder);

        kept.Keep(FakePresetSite.Root, Listed("""{ "id": "../../escaped", "name": "Climber", "file": "/f" }"""), [1], null);

        Directory.GetFiles(folder).ShouldHaveSingleItem().ShouldEndWith(".shared");
        Directory.GetFiles(Path.GetDirectoryName(folder)!, "escaped*").ShouldBeEmpty();
        kept.Find(FakePresetSite.Root, "../../escaped").ShouldNotBeNull().Name.ShouldBe("Climber");
    }

    [Fact]
    public void A_kept_file_cut_short_is_no_preset_rather_than_an_error()
    {
        var kept = new KeptSharedPresets(folder);

        kept.Keep(FakePresetSite.Root, Nebula(), new byte[1000], [9]);
        var found = kept.Find(FakePresetSite.Root, "n1").ShouldNotBeNull();

        var path = Directory.GetFiles(folder).Single();
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^500]);

        kept.File(found).ShouldBeNull();

        File.WriteAllBytes(path, bytes[..10]);

        kept.Find(FakePresetSite.Root, "n1").ShouldBeNull();
        kept.All(FakePresetSite.Root).ShouldBeEmpty();
    }

    [Fact]
    public void A_forgotten_preset_is_gone_and_the_rest_stay()
    {
        var kept = new KeptSharedPresets(folder);

        kept.Keep(FakePresetSite.Root, Nebula(), [1], [9]);
        kept.Keep(FakePresetSite.Root, Listed("""{ "id": "d1", "name": "Drift", "file": "/f" }"""), [2], null);

        kept.Forget(FakePresetSite.Root, "n1");

        kept.Find(FakePresetSite.Root, "n1").ShouldBeNull();
        kept.All(FakePresetSite.Root).Select(k => k.Name).ShouldBe(["Drift"]);
    }

    [Fact]
    public void No_folder_keeps_nothing()
    {
        KeptSharedPresets.None.Keep(FakePresetSite.Root, Nebula(), [1], null);

        KeptSharedPresets.None.Find(FakePresetSite.Root, "n1").ShouldBeNull();
        KeptSharedPresets.None.All(FakePresetSite.Root).ShouldBeEmpty();
    }
}
