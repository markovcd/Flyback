using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flyback.Specs.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The website as the preset site serves it, the Pages site and the server's own pages together.</summary>
[Binding]
public sealed partial class WebsiteSteps : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-website-").FullName;
    private readonly WebApplicationFactory<Program> host;
    private readonly HttpClient client;

    private string page = string.Empty;
    private readonly List<string> missing = [];
    private JsonElement shared;

    public WebsiteSteps()
    {
        host = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Site:Database", Path.Combine(folder, "presets.db"));
            web.UseSetting("Site:Defaults", Path.Combine(folder, "no-defaults"));
            web.UseSetting("Site:Media", Path.Combine(folder, "media"));
        });
        client = host.CreateClient();
    }

    [When("someone opens the preset site")]
    public async Task WhenTheSiteIsOpened() => page = await client.GetStringAsync(new Uri("/", UriKind.Relative));

    [Then("they read what Flyback is")]
    public void ThenTheOverviewIsRead() => page.ShouldContain("patchable video and audio synthesizer");

    /// <summary>Every <c>href</c> and <c>src</c> on every page reachable from the root, pages followed in turn.</summary>
    [When("someone follows every link between the preset site's pages")]
    public async Task WhenEveryLinkIsFollowed()
    {
        var root = new Uri("http://localhost/");
        var seen = new HashSet<string>(StringComparer.Ordinal) { "/" };
        var pages = new Queue<Uri>([root]);

        while (pages.TryDequeue(out var at))
        {
            var html = await client.GetStringAsync(at);

            foreach (Match link in Link().Matches(html))
            {
                var to = new Uri(at, link.Groups["to"].Value);
                var path = to.AbsolutePath;

                if (to.Host != root.Host || !seen.Add(path)) continue;

                // A site with no web editor beside it, as a test host is, has no /editor/ and shows no link to it.
                if (path.StartsWith("/editor/", StringComparison.Ordinal) && !EditorBuilt) continue;

                using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

                if (response.StatusCode != HttpStatusCode.OK)
                    missing.Add($"{at.AbsolutePath} links {link.Groups["to"].Value}: {(int)response.StatusCode}");
                else if (path.EndsWith(".html", StringComparison.Ordinal) || path.EndsWith('/'))
                    pages.Enqueue(to);
            }
        }

        seen.Count.ShouldBeGreaterThan(20);
    }

    /// <summary>Whether this build published the web editor where the site serves it from.</summary>
    private static bool EditorBuilt => Directory.Exists(Path.Combine(AppContext.BaseDirectory, "editor", "wwwroot"));

    [Then("none of them is missing")]
    public void ThenNoneIsMissing() => missing.ShouldBeEmpty(string.Join(Environment.NewLine, missing));

    [When("someone opens the preset site's presets page")]
    public async Task WhenThePresetsPageIsOpened() => page = await client.GetStringAsync(new Uri("/presets.html", UriKind.Relative));

    [Then("they can submit a preset there")]
    public void ThenAPresetCanBeSubmitted() => page.ShouldContain("href=\"submit.html\"");

    /// <summary>The shipped presets come from the build's stills, which GitHub Pages has too, and the shared ones from the API.</summary>
    [Then("it lists the presets Flyback ships with, marked as built in, beside the shared ones")]
    public async Task ThenShippedPresetsAreListed()
    {
        page.ShouldContain("id=\"shipped\"");
        page.ShouldContain("id=\"shared\"");

        var script = await client.GetStringAsync(new Uri("/assets/presets.js", UriKind.Relative));
        script.ShouldContain("\"stills/index.json\"");
        script.ShouldContain("\"Built in\"");
        script.ShouldContain("api + \"presets?\"");
    }

    [Then("it lists the built-in showcases first, then sound and picture, then one idea")]
    public async Task ThenShippedPresetsAreListedShowcaseFirst()
    {
        var script = await client.GetStringAsync(new Uri("/assets/presets.js", UriKind.Relative));
        script.ShouldContain("found.reverse();");
    }

    [When("a preset needing the {string} plugin is shared on the preset site")]
    public async Task WhenAPresetNeedingAPluginIsShared(string plugin)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(PluginPatch.Needing(plugin)), "file", plugin + ".fbk" } };
        using var posted = await client.PostAsync(new Uri("/api/v1/presets", UriKind.Relative), form);
        posted.StatusCode.ShouldBe(HttpStatusCode.Created);

        var id = (await posted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        shared = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/presets/" + id, UriKind.Relative));
    }

    [Then("the preset site says Flyback in a browser lacks the {string} plugin for it")]
    public void ThenTheBrowserLacksIt(string plugin)
    {
        var lacks = shared.GetProperty("lacks");

        lacks.GetProperty("plugins").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ShouldBe([plugin]);
        lacks.GetProperty("said").GetString().ShouldBe($"Needs the {plugin} plugin");
    }

    /// <summary>The card's and the preset page's Play and Edit give way to a line saying why.</summary>
    [Then("the presets page offers it to download rather than to play or edit in the browser")]
    public async Task ThenOnlyADownload()
    {
        var script = await client.GetStringAsync(new Uri("/assets/presets.js", UriKind.Relative));

        script.ShouldContain("var lacks = preset.lacks;");
        script.ShouldMatch("""if \(!lacks\) \{\s*buttons\.appendChild\([^\n]*inEditor\(preset\)[^\n]*\n\s*buttons\.appendChild\([^\n]*inBrowser\(preset""");
        script.ShouldMatch("""if \(!lacks\) \{\s*actions\.appendChild\([^\n]*inBrowser\(preset\)[^\n]*\n\s*actions\.appendChild\([^\n]*inEditor\(preset\)""");
        script.ShouldContain("download it to open it in Flyback.");
    }

    [When("someone opens the web viewer on the preset site")]
    public async Task WhenTheViewerIsOpened() => page = await client.GetStringAsync(new Uri("/viewer/", UriKind.Relative));

    [Then("it offers no presets of its own, only a way back to the presets page")]
    public async Task ThenNoPresetsOfItsOwn()
    {
        page.ShouldNotContain("id=\"gallery\"");
        page.ShouldNotContain("id=\"presets\"");
        page.ShouldContain("<a id=\"back\" class=\"back\">");

        var script = await client.GetStringAsync(new Uri("/viewer/main.js", UriKind.Relative));
        script.ShouldContain("params.get('back') ?? 'presets.html'");
    }

    /// <summary>A screen wake lock, asked for and let go as playing and full screen come and go.</summary>
    [Then("it keeps the screen on while its picture has the whole screen and plays")]
    public async Task ThenTheScreenStaysOn()
    {
        var script = await client.GetStringAsync(new Uri("/viewer/main.js", UriKind.Relative));

        script.ShouldContain("const wanted = playing && document.fullscreenElement != null && !document.hidden;");
        script.ShouldContain("navigator.wakeLock.request('screen')");
        script.ShouldContain("document.addEventListener('fullscreenchange', keepAwake);");
        script.ShouldMatch("""playing = true;(?:[^\n]*\n){1,6}\s*keepAwake\(\);""");
        script.ShouldMatch("""playing = false;(?:[^\n]*\n){1,6}\s*keepAwake\(\);""");
    }

    /// <summary>1080p among the sizes, and an Off that stops drawing without touching the sound.</summary>
    [Then("it offers 1920 x 1080 and a picture turned off, which draws nothing and leaves the sound playing")]
    public async Task ThenOffersFullHdAndOff()
    {
        var script = await client.GetStringAsync(new Uri("/viewer/main.js", UriKind.Relative));

        script.ShouldContain("[1920, 1080]");
        script.ShouldContain("ui.size.add(new Option('Picture off', OFF));");
        script.ShouldContain("if (noPicture !== null || !pictureOn) return;");
        script.ShouldContain("ui.size.value === OFF ? setPicture(false)");
    }

    /// <summary>The orientation locked to landscape once full screen is granted, which a browser allows only then.</summary>
    [Then("it asks for landscape once its picture has the whole screen")]
    public async Task ThenTurnsSideways()
    {
        var script = await client.GetStringAsync(new Uri("/viewer/main.js", UriKind.Relative));

        script.ShouldContain("requestFullscreen?.().then(turnSideways");
        script.ShouldContain("screen.orientation?.lock?.('landscape')");
    }

    /// <summary>
    /// The page's own script and style, and every file of the runtime its loader names,
    /// each there and a WebAssembly module served as one, which a browser insists on.
    /// </summary>
    [Then("its page and everything it loads to start are there")]
    public async Task ThenTheViewerLoads()
    {
        page.ShouldContain("Flyback Viewer");

        var loader = await client.GetStringAsync(new Uri("/viewer/_framework/dotnet.js", UriKind.Relative));
        var files = Framework().Matches(loader).Select(m => "_framework/" + m.Groups["name"].Value)
            .Concat(["main.js", "gl.js", "program.js", "sound.js", "speaker.js", "viewer.css"])
            .Distinct()
            .ToList();

        files.Count(f => f.EndsWith(".wasm", StringComparison.Ordinal)).ShouldBeGreaterThan(5);

        foreach (var file in files)
        {
            using var response = await client.GetAsync(new Uri($"/viewer/{file}", UriKind.Relative));

            if (response.StatusCode != HttpStatusCode.OK)
                missing.Add($"{file}: {(int)response.StatusCode}");
            else if (file.EndsWith(".wasm", StringComparison.Ordinal) && response.Content.Headers.ContentType?.MediaType != "application/wasm")
                missing.Add($"{file}: served as {response.Content.Headers.ContentType}");
        }

        missing.ShouldBeEmpty(string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// The files whose names stay put across builds, the loader among them, are checked
    /// with the site before each use; a file named with its fingerprint is kept for good.
    /// </summary>
    [Then("a browser that kept an earlier build of it asks for this one")]
    public async Task ThenANewBuildReachesAKeptPage()
    {
        foreach (var file in (string[])["", "main.js", "_framework/dotnet.js"])
        {
            using var response = await client.GetAsync(new Uri($"/viewer/{file}", UriKind.Relative));
            (response.Headers.CacheControl?.NoCache ?? false).ShouldBeTrue($"/viewer/{file}");
        }

        var loader = await client.GetStringAsync(new Uri("/viewer/_framework/dotnet.js", UriKind.Relative));
        var fingerprinted = Framework().Match(loader).Groups["name"].Value;

        using var kept = await client.GetAsync(new Uri($"/viewer/_framework/{fingerprinted}", UriKind.Relative));
        (kept.Headers.CacheControl?.MaxAge).ShouldBe(TimeSpan.FromDays(365), fingerprinted);
    }

    /// <summary>A file the runtime's loader fetches: named with its fingerprint, where its logical name has none.</summary>
    [GeneratedRegex("""["'](?<name>[A-Za-z0-9_.\-]+\.[a-z0-9]{10}\.(?:wasm|js|pdb))["']""")]
    private static partial Regex Framework();

    /// <summary>A link within the site: not another host, an anchor, mail or inline data.</summary>
    [GeneratedRegex("""(?:href|src)="(?<to>(?![a-z]+:|#|//)[^"]+)""")]
    private static partial Regex Link();

    public void Dispose()
    {
        client.Dispose();
        host.Dispose();
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }
}
