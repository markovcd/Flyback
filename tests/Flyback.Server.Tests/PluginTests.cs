using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Picture;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace Flyback.Server.Tests;

public sealed class PluginTests : IDisposable
{
    private static readonly byte[] Assembly = File.ReadAllBytes(typeof(PicturePlugin).Assembly.Location);

    private static readonly byte[] Sample = File.ReadAllBytes(typeof(Plugins.Sample.SampleModulesPlugin).Assembly.Location);

    private static readonly byte[] Bare = File.ReadAllBytes(typeof(Plugins.FakeAssistant.RehearsedAssistantPlugin).Assembly.Location);

    private readonly string folder = Directory.CreateTempSubdirectory("flyback-plugins-").FullName;
    private readonly WebApplicationFactory<Program> host;
    private readonly HttpClient client;

    public PluginTests()
    {
        host = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Site:Database", Path.Combine(folder, "presets.db"));
            web.UseSetting("Site:Defaults", Path.Combine(folder, "no-defaults"));
            web.UseSetting("Site:Media", Path.Combine(folder, "media"));
            web.UseSetting("Site:Admin:User", "admin");
            web.UseSetting("Site:Admin:Password", "hunter2");
        });
        client = host.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        host.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private static readonly ECDsa OtherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>A package of <paramref name="entries"/>, signed with <see cref="Key"/>.</summary>
    private static byte[] Zip(params (string Name, byte[] Bytes)[] entries) => PackageSigner.Sign(Unsigned(entries), Key);

    private static byte[] Unsigned(params (string Name, byte[] Bytes)[] entries)
    {
        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }

        return memory.ToArray();
    }

    /// <summary>The picture plugin built for each of <paramref name="platforms"/>.</summary>
    private static byte[] Package(params string[] platforms) =>
        Zip([.. platforms.Select(p => ($"{p}/Flyback.Plugins.Picture.dll", Assembly))]);

    private async Task<HttpResponseMessage> Post(byte[] file, string fileName = "picture.fbkp")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(file), "file", fileName);

        return await client.PostAsync(new Uri("/api/v1/plugins", UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

    private async Task<JsonElement> Submit(byte[] file)
    {
        using var response = await Post(file);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private async Task<JsonElement> Get(string path) =>
        await client.GetFromJsonAsync<JsonElement>(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    private async Task<HttpStatusCode> Status(HttpMethod method, string path, HttpContent? content = null, HttpClient? by = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        using var response = await (by ?? client).SendAsync(request, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    /// <summary>A client of its own, signed in as the admin.</summary>
    private async Task<HttpClient> Admin()
    {
        var admin = host.CreateClient();
        using var response = await admin.PostAsJsonAsync(
            new Uri("/api/v1/admin/session", UriKind.Relative), new { user = "admin", password = "hunter2" }, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        return admin;
    }

    private async Task Publish(string id)
    {
        using var admin = await Admin();

        (await Status(HttpMethod.Patch, $"/api/v1/plugins/{id}", JsonContent.Create(new { published = true }), admin)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_submitted_plugin_is_seen_by_nobody_until_it_is_published()
    {
        var sent = await Submit(Package("win"));
        var id = sent.GetProperty("id").GetString()!;

        sent.GetProperty("published").GetBoolean().ShouldBeFalse();
        (await Get("/api/v1/plugins")).GetProperty("total").GetInt32().ShouldBe(0);
        (await Status(HttpMethod.Get, $"/api/v1/plugins/{id}")).ShouldBe(HttpStatusCode.NotFound);
        (await Status(HttpMethod.Get, $"/api/v1/plugins/{id}/file")).ShouldBe(HttpStatusCode.NotFound);

        await Publish(id);

        (await Get("/api/v1/plugins")).GetProperty("total").GetInt32().ShouldBe(1);
        (await Status(HttpMethod.Get, $"/api/v1/plugins/{id}")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_plugin_is_listed_with_what_its_assembly_says_about_itself()
    {
        var file = Package("win", "linux");
        var id = (await Submit(file)).GetProperty("id").GetString()!;
        await Publish(id);

        var plugin = (await Get("/api/v1/plugins")).GetProperty("items")[0];

        plugin.GetProperty("assembly").GetString().ShouldBe("Flyback.Plugins.Picture");
        plugin.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        plugin.GetProperty("adds").EnumerateArray().Select(a => a.GetString()).ShouldContain("modules");
        plugin.GetProperty("builds").EnumerateArray().Select(b => b.GetString()).ShouldBe(["win", "linux"]);
        plugin.GetProperty("contract").GetProperty("Flyback.Plugins").GetString().ShouldNotBeNullOrEmpty();
        plugin.GetProperty("sha256").GetString().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(file)));
        plugin.GetProperty("fileName").GetString().ShouldBe("Flyback.Plugins.Picture.fbkp");
    }

    [Fact]
    public async Task A_plugins_author_tags_and_preview_are_shown_and_its_tags_find_it()
    {
        var id = (await Submit(Zip(("any/Flyback.Plugins.Sample.dll", Sample)))).GetProperty("id").GetString()!;
        var bare = (await Submit(Zip(("any/Flyback.Plugins.FakeAssistant.dll", Bare)))).GetProperty("id").GetString()!;
        await Publish(id);
        await Publish(bare);

        var plugin = await Get($"/api/v1/plugins/{id}");

        plugin.GetProperty("author").GetString().ShouldBe("Flyback");
        plugin.GetProperty("description").GetString().ShouldStartWith("Example modules");
        plugin.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["example", "ripple", "test-fixture"]);

        using var preview = await client.GetAsync(new Uri(plugin.GetProperty("preview").GetString()!, UriKind.Relative), TestContext.Current.CancellationToken);

        preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        preview.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await preview.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Length.ShouldBeGreaterThan(0);

        (await Get($"/api/v1/plugins/{bare}")).GetProperty("preview").ValueKind.ShouldBe(JsonValueKind.Null);
        (await Status(HttpMethod.Get, $"/api/v1/plugins/{bare}/preview")).ShouldBe(HttpStatusCode.NotFound);

        var tagged = (await Get("/api/v1/plugins?tag=ripple")).GetProperty("items");
        tagged.GetArrayLength().ShouldBe(1);
        tagged[0].GetProperty("id").GetString().ShouldBe(id);
        (await Get("/api/v1/plugins?tag=%20Ripple")).GetProperty("total").GetInt32().ShouldBe(1, "a tag is found as a preset's is, whatever its case");
        (await Get("/api/v1/plugins?q=fixture")).GetProperty("total").GetInt32().ShouldBe(1, "a search matches tags too");
    }

    [Fact]
    public async Task A_page_far_past_the_last_is_empty()
    {
        await Publish((await Submit(Package("win"))).GetProperty("id").GetString()!);

        (await Get("/api/v1/plugins?page=89478487")).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_package_stored_before_tags_and_previews_were_read_has_them_read_from_its_file()
    {
        var database = Path.Combine(folder, "earlier", "presets.db");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);

        using (var db = new SqliteConnection($"Data Source={database};Pooling=false"))
        {
            db.Open();

            using var create = db.CreateCommand();
            create.CommandText = """
                CREATE TABLE plugins (
                    id TEXT PRIMARY KEY, assembly TEXT NOT NULL, name TEXT NOT NULL, version TEXT NOT NULL,
                    author TEXT NOT NULL, description TEXT NOT NULL, adds TEXT NOT NULL, reaches TEXT NOT NULL,
                    builds TEXT NOT NULL, contract TEXT NOT NULL, sha256 TEXT NOT NULL UNIQUE, file_name TEXT NOT NULL,
                    file BLOB NOT NULL, size INTEGER NOT NULL, submitted_at TEXT NOT NULL,
                    downloads INTEGER NOT NULL DEFAULT 0, published INTEGER NOT NULL DEFAULT 0);
                INSERT INTO plugins VALUES ('earlier', 'Flyback.Plugins.Sample', 'Sample modules', '0.1.0', '', '', 'modules', '',
                    'any', '', 'hash', 'Flyback.Plugins.Sample.fbkp', $file, 1, '2026-09-22T00:00:00Z', 0, 1);
                """;
            create.Parameters.AddWithValue("$file", Zip(("any/Flyback.Plugins.Sample.dll", Sample)));
            create.ExecuteNonQuery();
        }

        using var earlier = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Site:Database", database);
            web.UseSetting("Site:Defaults", Path.Combine(folder, "no-defaults"));
            web.UseSetting("Site:Media", Path.Combine(folder, "earlier", "media"));
        });
        using var reader = earlier.CreateClient();

        var plugin = await reader.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/plugins/earlier", UriKind.Relative), TestContext.Current.CancellationToken);

        plugin.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["example", "ripple", "test-fixture"]);
        plugin.GetProperty("preview").GetString().ShouldBe("/api/v1/plugins/earlier/preview");
    }

    [Fact]
    public async Task An_unpublished_plugins_preview_is_not_served()
    {
        var id = (await Submit(Zip(("any/Flyback.Plugins.Sample.dll", Sample)))).GetProperty("id").GetString()!;

        (await Status(HttpMethod.Get, $"/api/v1/plugins/{id}/preview")).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_download_is_the_package_that_was_submitted_and_is_counted()
    {
        var file = Package("win");
        var id = (await Submit(file)).GetProperty("id").GetString()!;
        await Publish(id);

        using var response = await client.GetAsync(new Uri($"/api/v1/plugins/{id}/file", UriKind.Relative), TestContext.Current.CancellationToken);

        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(file);
        response.Content.Headers.ContentDisposition!.FileName.ShouldBe("Flyback.Plugins.Picture.fbkp");
        (await Get($"/api/v1/plugins/{id}")).GetProperty("downloads").GetInt64().ShouldBe(1);

        using var uncounted = await client.GetAsync(new Uri($"/api/v1/plugins/{id}/file?count=false", UriKind.Relative), TestContext.Current.CancellationToken);
        (await Get($"/api/v1/plugins/{id}")).GetProperty("downloads").GetInt64().ShouldBe(1);
    }

    [Fact]
    public async Task A_plugin_lists_the_modules_it_declares_and_is_found_by_one()
    {
        var sample = (await Submit(Zip(("any/Flyback.Plugins.Sample.dll", Sample)))).GetProperty("id").GetString()!;
        var picture = (await Submit(Package("win"))).GetProperty("id").GetString()!;
        await Publish(sample);
        await Publish(picture);

        var modules = (await Get($"/api/v1/plugins/{sample}")).GetProperty("modules").EnumerateArray()
            .Select(m => (m.GetProperty("id").GetString(), m.GetProperty("name").GetString()));
        modules.ShouldBe([("flyback.sample.ripple", "Ripple"), ("flyback.sample.halve", "Halve")]);

        var found = (await Get("/api/v1/plugins?module=flyback.picture.circle")).GetProperty("items");
        found.GetArrayLength().ShouldBe(1);
        found[0].GetProperty("id").GetString().ShouldBe(picture);

        (await Get("/api/v1/plugins?module=flyback.picture")).GetProperty("total").GetInt32().ShouldBe(0, "a module is found by its whole id");
        (await Get("/api/v1/plugins?q=Halve")).GetProperty("items")[0].GetProperty("id").GetString().ShouldBe(sample);
    }

    [Fact]
    public async Task Plugins_are_found_by_the_system_they_install_on()
    {
        await Publish((await Submit(Package("win"))).GetProperty("id").GetString()!);

        var everywhere = Zip(("any/Flyback.Plugins.Picture.dll", Assembly), ("readme.txt", [1]));
        await Publish((await Submit(everywhere)).GetProperty("id").GetString()!);

        (await Get("/api/v1/plugins?platform=win")).GetProperty("total").GetInt32().ShouldBe(2);
        (await Get("/api/v1/plugins?platform=linux")).GetProperty("total").GetInt32().ShouldBe(1);
        (await Status(HttpMethod.Get, "/api/v1/plugins?platform=amiga")).ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_same_package_is_taken_once()
    {
        // One array: a zip stamps its entries with the time, so a second one can hash differently.
        var file = Package("win");
        await Submit(file);

        using var again = await Post(file);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("picture.zip", "a plugin package")]
    [InlineData("picture.fbkp", "no plugin")]
    public async Task A_file_the_editor_would_refuse_is_refused_saying_why(string fileName, string why)
    {
        using var response = await Post(Zip(("win/readme.txt", [1])), fileName);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("error").GetString()!.ShouldContain(why);
    }

    [Fact]
    public async Task A_package_that_would_unpack_outside_its_folder_is_refused()
    {
        using var response = await Post(Zip(("win/Flyback.Plugins.Picture.dll", Assembly), ("../evil.dll", [1])));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_admin_sees_a_plugin_waiting_for_review_and_can_delete_it()
    {
        var id = (await Submit(Package("win"))).GetProperty("id").GetString()!;
        using var admin = await Admin();

        var waiting = await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/plugins", UriKind.Relative), TestContext.Current.CancellationToken);
        waiting.GetProperty("items")[0].GetProperty("published").GetBoolean().ShouldBeFalse();
        (await Status(HttpMethod.Get, $"/api/v1/plugins/{id}/file", by: admin)).ShouldBe(HttpStatusCode.OK);

        (await Status(HttpMethod.Delete, $"/api/v1/plugins/{id}", by: admin)).ShouldBe(HttpStatusCode.NoContent);
        (await Status(HttpMethod.Get, $"/api/v1/plugins/{id}", by: admin)).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Publishing_and_deleting_need_the_admin()
    {
        var id = (await Submit(Package("win"))).GetProperty("id").GetString()!;

        (await Status(HttpMethod.Patch, $"/api/v1/plugins/{id}", JsonContent.Create(new { published = true }))).ShouldBe(HttpStatusCode.Unauthorized);
        (await Status(HttpMethod.Delete, $"/api/v1/plugins/{id}")).ShouldBe(HttpStatusCode.Unauthorized);
        (await Get("/api/v1/plugins")).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task The_plugin_pages_are_served()
    {
        foreach (var page in new[] { "/shared-plugins.html", "/plugin.html", "/submit-plugin.html", "/assets/plugins.js" })
            (await Status(HttpMethod.Get, page)).ShouldBe(HttpStatusCode.OK, page);
    }

    [Fact]
    public async Task An_unsigned_package_is_refused()
    {
        Assert.SkipUnless(PackageSigner.Checked, "a Debug build takes a package whoever signed it");

        using var response = await Post(Unsigned(("win/Flyback.Plugins.Picture.dll", Assembly)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("error").GetString()!.ShouldContain("not signed");
    }

    [Fact]
    public async Task A_plugin_shows_the_key_that_signed_it()
    {
        var sent = await Submit(Package("win"));

        sent.GetProperty("signer").GetString().ShouldBe(PackageSigner.Of(Key).Fingerprint);
    }

    [Fact]
    public async Task A_published_plugins_name_is_not_taken_by_another_key()
    {
        Assert.SkipUnless(PackageSigner.Checked, "a Debug build takes a package whoever signed it");

        await Publish((await Submit(Package("win"))).GetProperty("id").GetString()!);

        using var impostor = await Post(PackageSigner.Sign(Unsigned(("win/Flyback.Plugins.Picture.dll", Assembly)), OtherKey));

        impostor.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await impostor.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("error").GetString()!.ShouldContain("signed with another key");

        (await Submit(Package("win", "linux"))).GetProperty("published").GetBoolean().ShouldBeFalse("the same key may send its next build");
    }

    [Fact]
    public async Task Two_keys_waiting_under_one_name_cannot_both_be_published()
    {
        Assert.SkipUnless(PackageSigner.Checked, "a Debug build takes a package whoever signed it");

        var first = (await Submit(Package("win"))).GetProperty("id").GetString()!;
        var second = (await Submit(PackageSigner.Sign(Unsigned(("win/Flyback.Plugins.Picture.dll", Assembly)), OtherKey))).GetProperty("id").GetString()!;
        using var admin = await Admin();

        await Publish(first);

        (await Status(HttpMethod.Patch, $"/api/v1/plugins/{second}", JsonContent.Create(new { published = true }), admin)).ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_published_plugin_can_be_reported_and_the_admin_sees_it_by_name()
    {
        var sent = await Submit(Package("win"));
        var id = sent.GetProperty("id").GetString()!;
        JsonContent Report() => JsonContent.Create(new { reason = "harmful", details = "It deleted my presets." });

        (await Status(HttpMethod.Post, $"/api/v1/plugins/{id}/reports", Report())).ShouldBe(HttpStatusCode.NotFound);

        await Publish(id);

        (await Status(HttpMethod.Post, $"/api/v1/plugins/{id}/reports", Report())).ShouldBe(HttpStatusCode.NoContent);

        using var admin = await Admin();
        var reports = await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/reports", UriKind.Relative), TestContext.Current.CancellationToken);
        var only = reports.EnumerateArray().ShouldHaveSingleItem();

        only.GetProperty("kind").GetString().ShouldBe("plugin");
        only.GetProperty("subject").GetString().ShouldBe(id);
        only.GetProperty("name").GetString().ShouldBe(sent.GetProperty("name").GetString());
        only.GetProperty("reason").GetString().ShouldBe("harmful");
        only.GetProperty("details").GetString().ShouldBe("It deleted my presets.");

        (await Status(HttpMethod.Delete, $"/api/v1/plugins/{id}", by: admin)).ShouldBe(HttpStatusCode.NoContent);

        (await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/reports", UriKind.Relative), TestContext.Current.CancellationToken))
            .GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_published_plugin_is_rated_from_the_site_and_listed_with_its_average()
    {
        var id = (await Submit(Package("win"))).GetProperty("id").GetString()!;

        HttpRequestMessage Rate(int stars)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/v1/plugins/{id}/rating", UriKind.Relative)) { Content = JsonContent.Create(new { stars }) };
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            return request;
        }

        using (var unpublished = await client.SendAsync(Rate(3), TestContext.Current.CancellationToken))
            unpublished.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await Publish(id);

        using (var rated = await client.SendAsync(Rate(3), TestContext.Current.CancellationToken))
            rated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rating = (await Get("/api/v1/plugins?platform=win")).GetProperty("items").EnumerateArray().Single().GetProperty("rating");

        rating.GetProperty("average").GetDouble().ShouldBe(3);
        rating.GetProperty("count").GetInt32().ShouldBe(1);

        (await Status(HttpMethod.Put, $"/api/v1/plugins/{id}/rating", JsonContent.Create(new { stars = 5 }))).ShouldBe(HttpStatusCode.Forbidden);
    }
}
