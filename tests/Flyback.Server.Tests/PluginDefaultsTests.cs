using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Flyback.Plugins.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace Flyback.Server.Tests;

/// <summary>
/// The plugins the site starts with: signed packages in its defaults folder, and
/// plugins built beside it, seeded published as it starts (ADR-0141).
/// </summary>
public sealed class PluginDefaultsTests : IDisposable
{
    private static readonly byte[] Figures = File.ReadAllBytes(typeof(Flyback.Plugins.Figures.FiguresPlugin).Assembly.Location);

    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>What RELEASE_SIGNING_KEY holds for these sites, standing in for the release workflow's secret.</summary>
    private static readonly string ReleaseKey = PackageSigner.NewKey();

    private readonly string folder = Directory.CreateTempSubdirectory("flyback-plugin-defaults-").FullName;

    private string Shipped => Path.Combine(folder, "defaults");

    public PluginDefaultsTests() => Directory.CreateDirectory(Shipped);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private string Builds => Path.Combine(folder, "plugins");

    private WebApplicationFactory<Program> Start() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Site:Database", Path.Combine(folder, "presets.db"));
            web.UseSetting("Site:Media", Path.Combine(folder, "media"));
            web.UseSetting("Site:Defaults", Shipped);
            web.UseSetting("Site:Builds", Builds);
            web.UseSetting("RELEASE_SIGNING_KEY", ReleaseKey);
            web.UseSetting("Site:Admin:User", "admin");
            web.UseSetting("Site:Admin:Password", "hunter2");
        });

    /// <summary>Figures laid out beside the site the way a build lays it out.</summary>
    private void Build()
    {
        Directory.CreateDirectory(Path.Combine(Builds, "Figures"));
        File.WriteAllBytes(Path.Combine(Builds, "Figures", "Flyback.Plugins.Figures.dll"), Figures);
    }

    /// <summary>The Figures plugin as a package for every system, signed unless said otherwise.</summary>
    private static byte[] Package(bool signed = true)
    {
        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry("any/Flyback.Plugins.Figures.dll").Open();
            stream.Write(Figures);
        }

        return signed ? PackageSigner.Sign(memory.ToArray(), Key) : memory.ToArray();
    }

    private byte[] Ship(string fileName, bool signed = true)
    {
        var package = Package(signed);
        File.WriteAllBytes(Path.Combine(Shipped, fileName), package);
        return package;
    }

    private static async Task<JsonElement[]> Shelf(WebApplicationFactory<Program> site)
    {
        using var client = site.CreateClient();
        var list = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/v1/plugins", UriKind.Relative), TestContext.Current.CancellationToken);

        return [.. list.GetProperty("items").EnumerateArray()];
    }

    [Fact]
    public async Task A_default_plugin_is_published_when_the_site_starts()
    {
        var shipped = Ship("Figures.fbkp");

        using var site = Start();
        var shelf = await Shelf(site);

        var figures = shelf.ShouldHaveSingleItem();
        figures.GetProperty("name").GetString().ShouldBe("Figures");
        figures.GetProperty("author").GetString().ShouldBe("Flyback");
        figures.GetProperty("published").GetBoolean().ShouldBeTrue();
        figures.GetProperty("signer").GetString().ShouldBe(PackageSigner.Of(Key).Fingerprint);
        figures.GetProperty("modules").EnumerateArray().Select(m => m.GetProperty("name").GetString())
            .ShouldBe(["Plate", "Harmonograph", "Overtones"], ignoreOrder: true);

        using var client = site.CreateClient();
        var file = await client.GetByteArrayAsync(
            new Uri(figures.GetProperty("file").GetString()! + "?count=false", UriKind.Relative), TestContext.Current.CancellationToken);

        file.ShouldBe(shipped);
    }

    [Fact]
    public async Task Starting_again_seeds_a_plugin_once()
    {
        Ship("Figures.fbkp");

        using (var first = Start()) await Shelf(first);
        using var again = Start();

        (await Shelf(again)).Length.ShouldBe(1);
    }

    /// <summary>A rebuilt package is signed afresh, so it is another file; it takes the same shelf entry.</summary>
    [Fact]
    public async Task A_rebuilt_default_replaces_the_stored_package_under_the_same_id()
    {
        Ship("Figures.fbkp");

        string id, sha;
        using (var first = Start())
        {
            var figures = (await Shelf(first))[0];
            id = figures.GetProperty("id").GetString()!;
            sha = figures.GetProperty("sha256").GetString()!;
        }

        var rebuilt = Ship("Figures.fbkp");

        using var again = Start();
        var shelf = await Shelf(again);

        shelf.Length.ShouldBe(1);
        shelf[0].GetProperty("id").GetString().ShouldBe(id);
        shelf[0].GetProperty("sha256").GetString().ShouldNotBe(sha);

        using var client = again.CreateClient();
        var file = await client.GetByteArrayAsync(
            new Uri($"/api/v1/plugins/{id}/file?count=false", UriKind.Relative), TestContext.Current.CancellationToken);

        file.ShouldBe(rebuilt);
    }

    [Fact]
    public async Task A_default_plugin_the_admin_deleted_stays_deleted()
    {
        Ship("Figures.fbkp");

        using (var first = Start())
        {
            var id = (await Shelf(first))[0].GetProperty("id").GetString();

            using var admin = first.CreateClient();
            using (var signIn = await admin.PostAsJsonAsync(
                       new Uri("/api/v1/admin/session", UriKind.Relative),
                       new { user = "admin", password = "hunter2" },
                       TestContext.Current.CancellationToken))
                signIn.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            using var deleted = await admin.DeleteAsync(
                new Uri("/api/v1/plugins/" + id, UriKind.Relative), TestContext.Current.CancellationToken);
            deleted.IsSuccessStatusCode.ShouldBeTrue();
        }

        using var again = Start();

        (await Shelf(again)).ShouldBeEmpty();
    }

    /// <summary>A default nobody could install is a broken build, and the site says so rather than starting short.</summary>
    [Fact]
    public void An_unsigned_default_plugin_stops_the_site()
    {
        Assert.SkipUnless(PackageSigner.Checked, "a Debug build installs unsigned packages");

        Ship("Figures.fbkp", signed: false);

        var site = Start();

        Should.Throw<InvalidOperationException>(() => site.CreateClient())
            .Message.ShouldContain("Figures.fbkp");
    }

    /// <summary>A run from the source: the plugin built beside the site is packed and signed with the release key.</summary>
    [Fact]
    public async Task A_plugin_built_beside_the_site_is_packed_and_signed_with_the_release_key()
    {
        Build();

        using var site = Start();
        var figures = (await Shelf(site)).ShouldHaveSingleItem();

        figures.GetProperty("name").GetString().ShouldBe("Figures");
        figures.GetProperty("published").GetBoolean().ShouldBeTrue();
        figures.GetProperty("builds").EnumerateArray().Select(b => b.GetString()).ShouldBe(["any"]);

        using var key = PackageSigner.Load(ReleaseKey);
        figures.GetProperty("signer").GetString().ShouldBe(PackageSigner.Of(key).Fingerprint);
    }

    /// <summary>Packed at every start, so what was just built is what the shelf offers, and to an editor it is the same plugin.</summary>
    [Fact]
    public async Task A_rebuilt_plugin_beside_the_site_replaces_the_last_build_under_the_same_id()
    {
        Build();

        string id, signer;
        using (var first = Start())
        {
            var figures = (await Shelf(first))[0];
            id = figures.GetProperty("id").GetString()!;
            signer = figures.GetProperty("signer").GetString()!;
        }

        var rebuilt = Path.Combine(Builds, "Figures", "Flyback.Plugins.Figures.xml");
        File.WriteAllText(rebuilt, "<doc />");

        using var again = Start();
        var shelf = await Shelf(again);

        shelf.Length.ShouldBe(1);
        shelf[0].GetProperty("id").GetString().ShouldBe(id);
        shelf[0].GetProperty("signer").GetString().ShouldBe(signer);

        using var client = again.CreateClient();
        var file = await client.GetByteArrayAsync(
            new Uri($"/api/v1/plugins/{id}/file?count=false", UriKind.Relative), TestContext.Current.CancellationToken);

        using var zip = new ZipArchive(new MemoryStream(file));
        zip.GetEntry("any/Flyback.Plugins.Figures.xml").ShouldNotBeNull();
    }

    [Fact]
    public async Task A_shipped_package_takes_precedence_over_a_build_of_the_same_plugin()
    {
        Ship("Figures.fbkp");
        Build();

        using var site = Start();
        var figures = (await Shelf(site)).ShouldHaveSingleItem();

        figures.GetProperty("signer").GetString().ShouldBe(PackageSigner.Of(Key).Fingerprint);
    }

    [Fact]
    public void A_file_that_is_not_a_package_stops_the_site()
    {
        File.WriteAllText(Path.Combine(Shipped, "Broken.fbkp"), "not a zip");

        var site = Start();

        Should.Throw<InvalidOperationException>(() => site.CreateClient())
            .Message.ShouldContain("Broken.fbkp");
    }
}
