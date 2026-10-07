using System.Text.RegularExpressions;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The website's files, as the Worker serves them from the repository's <c>site/</c> and the viewer's own pages.</summary>
[Binding]
public sealed partial class WebsiteSteps
{
    private string page = string.Empty;
    private readonly List<string> missing = [];

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

    [When("someone opens the preset site's presets page")]
    public void WhenThePresetsPageIsOpened() => page = SiteFiles.Read("/presets.html");

    [Then("they can submit a preset there")]
    public void ThenAPresetCanBeSubmitted() => page.ShouldContain("href=\"submit.html\"");

    /// <summary>The shipped presets come from the build's stills, which GitHub Pages has too, and the shared ones from the API.</summary>
    [Then("it lists the presets Flyback ships with, marked as built in, beside the shared ones")]
    public void ThenShippedPresetsAreListed()
    {
        page.ShouldContain("id=\"shipped\"");
        page.ShouldContain("id=\"shared\"");

        var script = SiteFiles.Read("/assets/presets.js");
        script.ShouldContain("\"stills/index.json\"");
        script.ShouldContain("\"Built in\"");
        script.ShouldContain("api + \"presets?\"");
    }

    [Then("it lists the built-in showcases first, then sound and picture, then one idea")]
    public static void ThenShippedPresetsAreListedShowcaseFirst()
    {
        var script = SiteFiles.Read("/assets/presets.js");
        script.ShouldContain("found.reverse();");
    }

    /// <summary>The card's and the preset page's Play and Edit give way to a line saying why.</summary>
    [Then("the presets page offers it to download rather than to play or edit in the browser")]
    public static void ThenOnlyADownload()
    {
        var script = SiteFiles.Read("/assets/presets.js");

        script.ShouldContain("var lacks = preset.lacks;");
        script.ShouldMatch("""if \(!lacks\) \{\s*buttons\.appendChild\([^\n]*inEditor\(preset\)[^\n]*\n\s*buttons\.appendChild\([^\n]*inBrowser\(preset""");
        script.ShouldMatch("""if \(!lacks\) \{\s*actions\.appendChild\([^\n]*inBrowser\(preset\)[^\n]*\n\s*actions\.appendChild\([^\n]*inEditor\(preset\)""");
        script.ShouldContain("download it to open it in Flyback.");
    }

    [When("someone opens the web viewer on the preset site")]
    public void WhenTheViewerIsOpened() => page = SiteFiles.Read("/viewer/");

    [Then("it offers no presets of its own, only a way back to the presets page")]
    public void ThenNoPresetsOfItsOwn()
    {
        page.ShouldNotContain("id=\"gallery\"");
        page.ShouldNotContain("id=\"presets\"");
        page.ShouldContain("<a id=\"back\" class=\"back\">");

        var script = SiteFiles.Read("/viewer/main.js");
        script.ShouldContain("params.get('back') ?? 'presets.html'");
    }

    /// <summary>A screen wake lock, asked for and let go as playing and full screen come and go.</summary>
    [Then("it keeps the screen on while its picture has the whole screen and plays")]
    public static void ThenTheScreenStaysOn()
    {
        var script = SiteFiles.Read("/viewer/main.js");

        script.ShouldContain("const wanted = playing && document.fullscreenElement != null && !document.hidden;");
        script.ShouldContain("navigator.wakeLock.request('screen')");
        script.ShouldContain("document.addEventListener('fullscreenchange', keepAwake);");
        script.ShouldMatch("""playing = true;(?:[^\n]*\n){1,6}\s*keepAwake\(\);""");
        script.ShouldMatch("""playing = false;(?:[^\n]*\n){1,6}\s*keepAwake\(\);""");
    }

    /// <summary>1080p among the sizes, and an Off that stops drawing without touching the sound.</summary>
    [Then("it offers 1920 x 1080 and a picture turned off, which draws nothing and leaves the sound playing")]
    public static void ThenOffersFullHdAndOff()
    {
        var script = SiteFiles.Read("/viewer/main.js");

        script.ShouldContain("[1920, 1080]");
        script.ShouldContain("ui.size.add(new Option('Picture off', OFF));");
        script.ShouldContain("if (noPicture !== null || !pictureOn) return;");
        script.ShouldContain("ui.size.value === OFF ? setPicture(false)");
    }

    /// <summary>The orientation locked to landscape once full screen is granted, which a browser allows only then.</summary>
    [Then("it asks for landscape once its picture has the whole screen")]
    public static void ThenTurnsSideways()
    {
        var script = SiteFiles.Read("/viewer/main.js");

        script.ShouldContain("requestFullscreen?.().then(turnSideways");
        script.ShouldContain("screen.orientation?.lock?.('landscape')");
    }

    /// <summary>The page's own script and style, each there.</summary>
    [Then("its page and everything it loads to start are there")]
    public void ThenTheViewerLoads()
    {
        page.ShouldContain("Flyback Viewer");

        foreach (var file in (string[])["main.js", "gl.js", "microphone.js", "program.js", "sound.js", "speaker.js", "viewer.css"])
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
