using System.Net;
using System.Security.Cryptography;
using System.Text;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>The preset site's packs as the app reads them: the listing, and a file held to what it lists.</summary>
public sealed class PackSiteTests
{
    private static readonly Uri Root = new("https://presets.example.org/");

    private const string Echoes = "pack ada.echoes 1.1.0\nmodule wobble(rate = 3) = sine(freq: rate)\n";

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Listing(string sha256, string file = "/api/v1/packs/v2/file") => $$"""
        {
          "items": [
            { "id": "v2", "pack": "ada.echoes", "version": "1.1.0", "sha256": "{{sha256}}", "size": 60, "downloads": 3,
              "file": "{{file}}", "modules": [ { "id": "ada.echoes.wobble", "name": "Wobble" } ] }
          ],
          "total": 1, "page": 1, "pageSize": 24
        }
        """;

    private static (PackSite Site, Site Fake) Serving(string listing, byte[] file)
    {
        var fake = new Site(listing, file);
        return (new PackSite(new HttpClient(fake), Root), fake);
    }

    [Fact]
    public async Task The_listing_names_each_pack_its_version_and_modules()
    {
        var (site, fake) = Serving(Listing(Hash(Echoes)), Encoding.UTF8.GetBytes(Echoes));

        var found = await site.SearchAsync("wobble", 1, TestContext.Current.CancellationToken);

        found.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            pack => pack.Pack.ShouldBe("ada.echoes"),
            pack => pack.Version.ShouldBe("1.1.0"),
            pack => pack.Modules.ShouldBe([("ada.echoes.wobble", "Wobble")]),
            pack => pack.File.ShouldBe(new Uri(Root, "/api/v1/packs/v2/file")));
        fake.Asked.ShouldContain("/api/v1/packs?page=1&q=wobble");
    }

    [Fact]
    public async Task The_newest_version_of_a_pack_is_downloaded_as_its_text()
    {
        var (site, fake) = Serving(Listing(Hash(Echoes)), Encoding.UTF8.GetBytes(Echoes));
        var cancel = TestContext.Current.CancellationToken;

        var newest = (await site.NewestAsync("ada.echoes", cancel)).ShouldNotBeNull();

        (await site.DownloadAsync(newest, cancel)).ShouldBe(Echoes);
        fake.Asked.ShouldContain("/api/v1/packs?page=1&pack=ada.echoes");
    }

    [Fact]
    public async Task A_file_that_is_not_the_one_listed_is_refused()
    {
        var (site, _) = Serving(Listing(Hash(Echoes)), Encoding.UTF8.GetBytes(Echoes.Replace("3", "4")));
        var cancel = TestContext.Current.CancellationToken;

        var newest = (await site.NewestAsync("ada.echoes", cancel)).ShouldNotBeNull();

        await Should.ThrowAsync<InvalidDataException>(() => site.DownloadAsync(newest, cancel));
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_before_it_is_all_read()
    {
        var huge = new byte[InstalledPacks.MostBytes + 1];
        var (site, _) = Serving(Listing(Convert.ToHexStringLower(SHA256.HashData(huge))), huge);
        var cancel = TestContext.Current.CancellationToken;

        var newest = (await site.NewestAsync("ada.echoes", cancel)).ShouldNotBeNull();

        (await Should.ThrowAsync<InvalidDataException>(() => site.DownloadAsync(newest, cancel))).Message.ShouldContain("KB");
    }

    [Fact]
    public async Task A_pack_whose_file_is_on_another_site_is_left_out()
    {
        var (site, _) = Serving(Listing(Hash(Echoes), "https://elsewhere.example.com/echoes.fbks"), []);

        (await site.SearchAsync(null, 1, TestContext.Current.CancellationToken)).Items.ShouldBeEmpty();
    }

    private sealed class Site(string listing, byte[] file) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!.PathAndQuery);

            return Task.FromResult(request.RequestUri.AbsolutePath switch
            {
                "/api/v1/packs" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listing, Encoding.UTF8, "application/json") },
                "/api/v1/packs/v2/file" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }
    }
}
