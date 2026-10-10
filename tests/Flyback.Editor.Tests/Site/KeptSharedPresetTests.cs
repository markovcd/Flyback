using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Engine.Graph;
using Flyback.Editor.Site;
using Flyback.Editor.Windows;
using Flyback.Core;
using Flyback.Core.Graph;
using Shouldly;
using Flyback.Tests;

namespace Flyback.Editor.Tests.Site;

/// <summary>A shared preset opened once opens again while the preset site does not answer.</summary>
public sealed class KeptSharedPresetTests : EditorTest
{
    private const string Program = GlobalConstants.ApplicationName;

    /// <summary>A picture one pixel across, which is all a still needs to be to show.</summary>
    private static readonly byte[] Pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-shared-" + Guid.NewGuid().ToString("N"));

    public override void Dispose()
    {
        base.Dispose();

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static byte[] PatchFile()
    {
        var patch = new Patch();

        patch.EnsureOutput();
        patch.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 100, 100));

        return System.Text.Encoding.UTF8.GetBytes(PatchIO.ToJson(patch));
    }

    private static FakePresetSite Nebula() => new(
        new Posted("n1", "Nebula", "Ann", "Gas and dust.", File: PatchFile(), Average: 4.5, Ratings: 2, Tags: ["space"], Still: Pixel),
        new Posted("d1", "Drift", "Bob", File: PatchFile()));

    private MainWindow Window(FakePresetSite site)
    {
        var window = NewMainWindow(new EditorSetup { Host = new() { PresetSite = FakePresetSite.Root }, Folders = new() { SharedPresetFolder = folder } }, Site(site));

        window.Show();
        Settle(window);

        return window;
    }

    private static Button[] SiteTiles(Window window) => [.. All<Button>(window).Where(b => b.Name == "site-tile")];

    private static string? Status(Window window) => All<TextBlock>(window).Single(t => t.Name == "site-status").Text;

    private static void ShowGallery(MainWindow window)
    {
        Press(All<Button>(window).Single(b => b.Name == "presets-glyph"));
        Pump(() => All<TextBlock>(window).Any(t => t.Name == "site-status") && Status(window) != "Looking…");
        Settle(window);
    }

    /// <summary>Picks the tile and waits for the report line to say it opened, which it says even of the preset already open.</summary>
    /// <remarks>Heard as it is said: the report line's log keeps only its last few lines, so a count of them is no mark to wait past.</remarks>
    private static void Pick(MainWindow window, string name)
    {
        var line = All<ReportLine>(window).Single();
        var opened = false;
        EventHandler<string> heard = (_, message) => opened |= message.StartsWith($"Opened “{name}”", StringComparison.Ordinal);

        line.Said += heard;

        try
        {
            Press(SiteTiles(window).Single(b => ((SitePreset)b.Tag!).Name == name));
            Pump(() => opened);
        }
        finally
        {
            line.Said -= heard;
        }

        Settle(window);
    }

    private static void Opened(MainWindow window, string name)
    {
        window.Title.ShouldBe($"{name} — {Program}");
        All<ModalOverlay>(window).ShouldBeEmpty();
        All<NodeEditor>(window).Single().History.Patch.Nodes.ShouldContain(n => n.TypeId == "value");
    }

    [AvaloniaFact]
    public void A_shared_preset_opened_once_is_listed_with_its_stars_while_the_site_does_not_answer()
    {
        using var site = Nebula();
        var window = Window(site);

        ShowGallery(window);
        Pick(window, "Nebula");

        site.Down = true;
        ShowGallery(window);

        Status(window).ShouldBe($"The preset site at {FakePresetSite.Root} did not answer. These were kept when they were last opened from it.");

        var tile = SiteTiles(window).ShouldHaveSingleItem("Drift was never opened, so it was never kept");
        var words = All<TextBlock>(tile).Select(t => t.Text).ToList();

        All<TextBlock>(tile).Single(t => t.Name == "siteRating").Inlines!.Text.ShouldBe("★★★★★  4.5 (2 ratings)");
        words.ShouldContain("Gas and dust.");
        words.ShouldContain("by Ann");
        words.ShouldContain("space");
        Pump(() => All<Image>(tile).Any());
        All<Image>(tile).ShouldHaveSingleItem("its still was kept too");
    }

    [AvaloniaFact]
    public void Picking_a_kept_preset_opens_it_without_asking_the_site()
    {
        using var site = Nebula();
        var window = Window(site);

        ShowGallery(window);
        Pick(window, "Nebula");

        site.Down = true;
        ShowGallery(window);
        var asked = site.Asked.Count;

        Pick(window, "Nebula");

        Opened(window, "Nebula");
        site.Asked.Count.ShouldBe(asked);
        Reported(window).ShouldContain("Opened “Nebula” as it was kept, the preset site not answering.");
    }

    [AvaloniaFact]
    public void The_filter_narrows_the_kept_presets_as_the_site_would()
    {
        using var site = Nebula();
        var window = Window(site);

        ShowGallery(window);
        Pick(window, "Nebula");
        ShowGallery(window);
        Pick(window, "Drift");

        site.Down = true;
        ShowGallery(window);

        SiteTiles(window).Select(b => ((SitePreset)b.Tag!).Name).ShouldBe(["Drift", "Nebula"]);

        All<TextBox>(window).Single(b => b.Name == "preset-filter").Text = "ann";
        Pump(() => SiteTiles(window).Length == 1);
        Settle(window);

        SiteTiles(window).Select(b => ((SitePreset)b.Tag!).Name).ShouldBe(["Nebula"]);
    }

    [AvaloniaFact]
    public void A_preset_a_restart_was_carrying_opens_as_it_was_kept_while_the_site_does_not_answer()
    {
        using (var site = Nebula())
        {
            var first = Window(site);

            ShowGallery(first);
            Pick(first, "Nebula");
        }

        using var down = new FakePresetSite { Down = true };
        var window = NewMainWindow(
            new EditorSetup { Host = new() { PresetSite = FakePresetSite.Root }, Folders = new() { SharedPresetFolder = folder }, Launch = new() { OpenShared = "n1" } },
            Site(down));

        window.Show();
        Pump(() => window.Title == $"Nebula — {Program}");
        Settle(window);

        Opened(window, "Nebula");
    }

    private KeptPreset? Kept(string id) => new KeptSharedPresets(folder).Find(FakePresetSite.Root, id);

    [AvaloniaFact]
    public void A_site_answering_only_errors_is_a_site_that_does_not_answer()
    {
        using var site = Nebula();
        var window = Window(site);

        ShowGallery(window);
        Pick(window, "Nebula");

        site.Answering = System.Net.HttpStatusCode.BadGateway;
        ShowGallery(window);
        Pick(window, "Nebula");

        Opened(window, "Nebula");
        Reported(window).ShouldContain("Opened “Nebula” as it was kept, the preset site not answering.");
    }

    [AvaloniaFact]
    public void A_preset_a_restart_was_carrying_that_was_taken_off_the_site_is_forgotten_and_not_opened()
    {
        using (var site = Nebula())
        {
            var first = Window(site);

            ShowGallery(first);
            Pick(first, "Nebula");
        }

        using var taken = Nebula();
        taken.TakenDown.Add("n1");

        var window = NewMainWindow(
            new EditorSetup { Host = new() { PresetSite = FakePresetSite.Root }, Folders = new() { SharedPresetFolder = folder }, Launch = new() { OpenShared = "n1" } },
            Site(taken));

        window.Show();
        Pump(() => Reported(window).Contains("“Nebula” has been taken off the preset site."));
        Settle(window);

        window.Title.ShouldNotStartWith("Nebula");
        Kept("n1").ShouldBeNull();
    }

    private static int Asked(FakePresetSite site, string path) => site.Asked.Count(uri => uri.AbsolutePath == path);

    [AvaloniaFact]
    public void A_kept_preset_opens_without_downloading_it_again()
    {
        using var site = Nebula();
        var window = Window(site);

        ShowGallery(window);
        Pick(window, "Nebula");

        var files = Asked(site, "/api/v1/presets/n1/file");
        var stills = Asked(site, "/media/n1.webp");

        ShowGallery(window);
        Pick(window, "Nebula");

        Opened(window, "Nebula");
        Reported(window).ShouldContain("Opened “Nebula” from the preset site, as kept on this machine.");
        Asked(site, "/api/v1/presets/n1/file").ShouldBe(files);
        Asked(site, "/media/n1.webp").ShouldBe(stills, "the tile shows the still kept with it");
    }

    [AvaloniaFact]
    public void A_preset_a_restart_was_carrying_opens_as_kept_without_downloading_it()
    {
        using (var site = Nebula())
        {
            var first = Window(site);

            ShowGallery(first);
            Pick(first, "Nebula");
        }

        using var again = Nebula();
        var window = NewMainWindow(
            new EditorSetup { Host = new() { PresetSite = FakePresetSite.Root }, Folders = new() { SharedPresetFolder = folder }, Launch = new() { OpenShared = "n1" } },
            Site(again));

        window.Show();
        Pump(() => window.Title == $"Nebula — {Program}");
        Settle(window);

        Opened(window, "Nebula");
        again.Asked.ShouldNotContain(uri => uri.AbsolutePath.EndsWith("/file", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> Reported(MainWindow window) => All<ReportLine>(window).Single().History;
}
