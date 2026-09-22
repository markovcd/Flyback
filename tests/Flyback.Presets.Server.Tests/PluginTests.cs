using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Flyback.Plugins.Picture;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace Flyback.Presets.Server.Tests;

public sealed class PluginTests : IDisposable
{
    private static readonly byte[] Assembly = File.ReadAllBytes(typeof(PicturePlugin).Assembly.Location);

    private readonly string folder = Directory.CreateTempSubdirectory("flyback-plugins-").FullName;
    private readonly WebApplicationFactory<Program> host;
    private readonly HttpClient client;

    public PluginTests()
    {
        host = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Presets:Database", Path.Combine(folder, "presets.db"));
            web.UseSetting("Presets:Media", Path.Combine(folder, "media"));
            web.UseSetting("Presets:Admin:User", "admin");
            web.UseSetting("Presets:Admin:Password", "hunter2");
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

    private static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
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
        foreach (var page in new[] { "/plugins.html", "/plugin.html", "/submit-plugin.html", "/assets/plugins.js" })
            (await Status(HttpMethod.Get, page)).ShouldBe(HttpStatusCode.OK, page);
    }
}
