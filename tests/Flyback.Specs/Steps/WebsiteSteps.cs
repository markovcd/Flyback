using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Specs.Support;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The website's files, as the Worker serves them from the repository's <c>site/</c> and the viewer's own pages.</summary>
[Binding]
public sealed partial class WebsiteSteps(IUnitTestRuntimeProvider runtime)
{
    private string page = string.Empty;
    private readonly List<string> missing = [];

    /// <summary>The presets shared on the site, as its API answers them.</summary>
    private readonly List<JsonObject> shared = [];

    /// <summary>What the presets page's script built, by element id.</summary>
    private JsonObject built = [];

    [When("someone opens the preset site")]
    public void WhenTheSiteIsOpened() => page = SiteFiles.Read("/");

    [Then("they read what Flyback is")]
    public void ThenTheOverviewIsRead() => page.ShouldContain("patchable video and audio synthesizer");

    /// <summary>Every <c>href</c> and <c>src</c> on every page reachable from the root, pages followed in turn.</summary>
    [When("someone follows every link between the preset site's pages")]
    public void WhenEveryLinkIsFollowed()
    {
        var root = new Uri("http://localhost/");
        var seen = new HashSet<string>(StringComparer.Ordinal) { "/" };
        var pages = new Queue<Uri>([root]);

        while (pages.TryDequeue(out var at))
        {
            var html = SiteFiles.Read(at.AbsolutePath);

            foreach (Match link in Link().Matches(html))
            {
                var to = new Uri(at, link.Groups["to"].Value);
                var path = to.AbsolutePath;

                if (to.Host != root.Host || !seen.Add(path)) continue;

                // The API, the media and the build's stills are the Worker's, and the editor is checked by its own scenarios.
                if (path.StartsWith("/api/", StringComparison.Ordinal) || path.StartsWith("/media/", StringComparison.Ordinal)
                    || path.StartsWith("/stills/", StringComparison.Ordinal) || path.StartsWith("/editor/", StringComparison.Ordinal)) continue;

                if (SiteFiles.Find(path) is null)
                    missing.Add($"{at.AbsolutePath} links {link.Groups["to"].Value}: not there");
                else if (path.EndsWith(".html", StringComparison.Ordinal) || path.EndsWith('/'))
                    pages.Enqueue(to);
            }
        }

        seen.Count.ShouldBeGreaterThan(20);
    }

    [Then("none of them is missing")]
    public void ThenNoneIsMissing() => missing.ShouldBeEmpty(string.Join(Environment.NewLine, missing));

    [Given("someone has shared a preset")]
    public void GivenShared() => shared.Add(Shared("Drift", lacks: null));

    [Given("someone has shared a preset that needs a plugin a browser lacks")]
    public void GivenSharedLacking() =>
        shared.Add(Shared("Murmur", new JsonObject { ["plugins"] = new JsonArray(), ["modules"] = 1, ["said"] = "Needs the Flock plugin" }));

    [When("someone opens the preset site's presets page")]
    public void WhenThePresetsPageIsOpened()
    {
        page = SiteFiles.Read("/presets.html");
        built = PresetsPage.Open(runtime, "shelf", "", Answers());
    }

    [When("someone opens the shared preset's page")]
    public void WhenThePresetsOwnPageIsOpened()
    {
        var preset = shared.ShouldHaveSingleItem();
        var answers = Answers();
        answers[$"api/v1/presets/{preset["id"]}"] = preset.DeepClone();

        built = PresetsPage.Open(runtime, "preset", $"?id={preset["id"]}", answers);
    }

    [Then("they can submit a preset there")]
    public void ThenAPresetCanBeSubmitted() => page.ShouldContain("href=\"submit.html\"");

    [Then("it lists the presets Flyback ships with, marked as built in, beside the shared one")]
    public void ThenShippedPresetsAreListed()
    {
        var cards = PresetsPage.OfClass(built["shipped"], "card").ToList();

        cards.Select(Named).ShouldBe(Shipped().Select(p => p.Name), ignoreOrder: true);
        cards.ShouldAllBe(card => PresetsPage.Text(PresetsPage.OfClass(card, "badge").Single()) == "Built in");

        PresetsPage.OfClass(built["shelf"], "card").Select(Named).ShouldBe(shared.Select(p => (string)p["name"]!));
        ((bool)built["shared"]!["hidden"]!).ShouldBeFalse();
    }

    [Then("it lists the built-in showcases first, then sound and picture, then one idea")]
    public void ThenShippedPresetsAreListedShowcaseFirst() =>
        PresetsPage.All(built["shipped"]).Where(e => (string?)e["tag"] == "h2").Select(h => (string)h["text"]!)
            .ShouldBe([.. new[] { PresetKind.Showcase, PresetKind.Interplay, PresetKind.Idea }.Select(k => "BUILT IN · " + PresetKinds.Heading(k))]);

    [Then("its card offers it to download, not to play or edit in the browser")]
    public void ThenTheCardOnlyDownloads()
    {
        var card = PresetsPage.OfClass(built["shelf"], "card").ShouldHaveSingleItem();

        PresetsPage.OfClass(card, "button").Select(PresetsPage.Text).ShouldBe(["Download"]);
        PresetsPage.Text(PresetsPage.OfClass(card, "lacks").Single()).ShouldBe("Needs the Flock plugin");
    }

    [Then("it offers it to download, not to play or edit in the browser, and says why")]
    public void ThenThePageOnlyDownloads()
    {
        var actions = PresetsPage.OfClass(built["preset"], "actions").ShouldHaveSingleItem();

        PresetsPage.OfClass(actions, "button").Select(PresetsPage.Text).ShouldBe(["Download", "All presets"]);
        PresetsPage.Text(PresetsPage.OfClass(built["preset"], "lacks").Single())
            .ShouldBe("Needs the Flock plugin, which Flyback in a browser does not have: download it to open it in Flyback.");
    }

    /// <summary>The shipped presets the presets page offers: every one but the blank canvas.</summary>
    private static IEnumerable<PatchPreset> Shipped() =>
        ShippedPlugins.Loaded.Presets.Where(p => p.Kind != PresetKind.Blank);

    /// <summary>What the site answers: the stills' index as <c>flyback-cli stills</c> writes it, the shared presets, no admin and no tags.</summary>
    private JsonObject Answers()
    {
        var index = new StillIndex(
            StillIndex.ThisBuild,
            [.. ShippedPlugins.Loaded.Presets.Select(p => new StillEntry(p.Name, p.Kind, StillKind.Picture, null, p.Description))]);

        return new JsonObject
        {
            ["api/v1/admin"] = new JsonObject { ["enabled"] = false, ["signedIn"] = false },
            ["stills/index.json"] = JsonNode.Parse(index.Write()),
            ["api/v1/presets"] = new JsonObject
            {
                ["items"] = new JsonArray([.. shared.Select(p => p.DeepClone())]),
                ["total"] = shared.Count,
                ["pageSize"] = 24,
            },
            ["api/v1/tags"] = new JsonArray(),
        };
    }

    /// <summary>A checked, published preset as the site's API answers it.</summary>
    private static JsonObject Shared(string name, JsonObject? lacks) => new()
    {
        ["id"] = name.ToLowerInvariant(),
        ["name"] = name,
        ["author"] = "Ada",
        ["description"] = null,
        ["tags"] = new JsonArray(),
        ["fileName"] = name.ToLowerInvariant() + ".fbk",
        ["size"] = 2048,
        ["submitted"] = "2026-10-01T12:00:00Z",
        ["downloads"] = 3,
        ["published"] = true,
        ["file"] = $"/api/v1/presets/{name.ToLowerInvariant()}/file",
        ["media"] = new JsonObject { ["state"] = "done" },
        ["rating"] = new JsonObject { ["average"] = 0, ["count"] = 0 },
        ["lacks"] = lacks,
        ["status"] = "checked",
        ["reason"] = null,
    };

    /// <summary>The name a card's header links.</summary>
    private static string Named(JsonObject card) =>
        PresetsPage.Text(PresetsPage.All(card).First(e => (string?)e["tag"] == "header")["children"]![0]);

    [When("someone opens the web viewer on the preset site")]
    public void WhenTheViewerIsOpened() => page = SiteFiles.Read("/viewer/");

    [Then("it offers no presets of its own, only a way back to the presets page")]
    public void ThenNoPresetsOfItsOwn()
    {
        page.ShouldNotContain("id=\"gallery\"");
        page.ShouldNotContain("id=\"presets\"");
        page.ShouldContain("<a id=\"back\" class=\"back\">");
    }

    /// <summary>The page's own script and style, each there.</summary>
    [Then("its page and everything it loads to start are there")]
    public void ThenTheViewerLoads()
    {
        page.ShouldContain("Flyback Viewer");

        foreach (var file in (string[])["main.js", "gl.js", "microphone.js", "program.js", "sound.js", "speaker.js", "speakers.js", "viewer.css"])
            if (SiteFiles.Find($"/viewer/{file}") is null) missing.Add(file);

        missing.ShouldBeEmpty(string.Join(Environment.NewLine, missing));
    }

    [When("someone opens the tutorials page")]
    public void WhenTheTutorialsPageIsOpened() => page = SiteFiles.Read("/tutorials.html");

    [Then("every video in its list has a name of its own to link to, a title and a line on what it shows")]
    public void ThenEveryVideoIsNamed()
    {
        var entries = Tutorial().Matches(page);
        entries.Count.ShouldBeGreaterThan(0);
        entries.Select(e => e.Groups["slug"].Value).ShouldBeUnique();
        entries.Select(e => e.Groups["video"].Value).ShouldBeUnique();
        foreach (Match entry in entries)
        {
            entry.Groups["href"].Value.ShouldBe("https://www.youtube.com/watch?v=" + entry.Groups["video"].Value);
            entry.Groups["blurb"].Value.ShouldNotBeNullOrWhiteSpace();
            entry.Groups["title"].Value.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Then("its player starts on the first video in the list")]
    public void ThenThePlayerStartsOnTheFirst()
    {
        var first = Tutorial().Match(page).Groups["video"].Value;
        page.ShouldContain($"""<iframe src="https://www.youtube-nocookie.com/embed/{first}" """);
        page.ShouldContain("""<script src="assets/tutorials.js"></script>""");
    }

    /// <summary>One entry of the tutorials page's playlist.</summary>
    [GeneratedRegex("""<a href="(?<href>[^"]+)" data-video="(?<video>[\w-]{11})" data-slug="(?<slug>[a-z0-9-]+)"[^>]*\s+data-blurb="(?<blurb>[^"]*)">[\s\S]*?<strong>(?<title>[^<]+)</strong>""")]
    private static partial Regex Tutorial();

    /// <summary>A link within the site: not another host, an anchor, mail or inline data.</summary>
    [GeneratedRegex("""(?:href|src)="(?<to>(?![a-z]+:|#|//)[^"]+)""")]
    private static partial Regex Link();
}
