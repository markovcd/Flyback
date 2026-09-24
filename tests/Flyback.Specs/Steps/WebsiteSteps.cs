using System.Net;
using System.Text.RegularExpressions;
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

                using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

                if (response.StatusCode != HttpStatusCode.OK)
                    missing.Add($"{at.AbsolutePath} links {link.Groups["to"].Value}: {(int)response.StatusCode}");
                else if (path.EndsWith(".html", StringComparison.Ordinal) || path.EndsWith('/'))
                    pages.Enqueue(to);
            }
        }

        seen.Count.ShouldBeGreaterThan(20);
    }

    [Then("none of them is missing")]
    public void ThenNoneIsMissing() => missing.ShouldBeEmpty(string.Join(Environment.NewLine, missing));

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
