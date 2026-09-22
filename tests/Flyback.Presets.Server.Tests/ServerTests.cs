using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flyback.Core.Graph;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace Flyback.Presets.Server.Tests;

public sealed class ServerTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-presets-").FullName;
    private readonly WebApplicationFactory<Program> host;
    private readonly HttpClient client;

    public ServerTests() : this(20) { }

    private ServerTests(int postsPerHour)
    {
        host = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Presets:Database", Path.Combine(folder, "presets.db"));
            web.UseSetting("Presets:Media", Media);
            web.UseSetting("Presets:PostsPerHour", postsPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
        client = host.CreateClient();
    }

    private string Media => Path.Combine(folder, "media");

    public void Dispose()
    {
        client.Dispose();
        host.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    private static byte[] PatchFile(string? description = "A slow drone.", string? author = "Ada", params string[] tags)
    {
        var patch = new Patch();
        patch.EnsureOutput(NodeCatalog.Current);
        patch.Describe(description);
        patch.Credit(author);
        patch.Tag(tags);

        return Encoding.UTF8.GetBytes(PatchIO.ToJson(patch));
    }

    private async Task<JsonElement> Submit(byte[] file, string fileName = "Drone.fbk", string? name = null)
    {
        using var response = await Post(file, fileName, name);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> Post(byte[] file, string fileName, string? name = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(file), "file", fileName);
        if (name is not null) form.Add(new StringContent(name), "name");

        return await client.PostAsync(new Uri("/api/v1/presets", UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

    private async Task<JsonElement> Get(string path) =>
        await client.GetFromJsonAsync<JsonElement>(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_submitted_preset_is_listed_with_what_the_patch_says_about_itself()
    {
        await Submit(PatchFile("A slow drone.", "Ada", "Drone", "ambient"));

        var list = await Get("/api/v1/presets");
        var item = list.GetProperty("items")[0];

        item.GetProperty("name").GetString().ShouldBe("Drone");
        item.GetProperty("description").GetString().ShouldBe("A slow drone.");
        item.GetProperty("author").GetString().ShouldBe("Ada");
        item.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["ambient", "drone"]);
        list.GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_name_given_with_the_file_is_the_one_listed()
    {
        var stored = await Submit(PatchFile(), name: "Night bus");

        stored.GetProperty("name").GetString().ShouldBe("Night bus");
    }

    [Fact]
    public async Task A_download_is_the_file_that_was_submitted()
    {
        var file = PatchFile();
        var stored = await Submit(file);

        var downloaded = await client.GetByteArrayAsync(
            new Uri(stored.GetProperty("file").GetString()!, UriKind.Relative), TestContext.Current.CancellationToken);

        downloaded.ShouldBe(file);
        (await Get("/api/v1/presets/" + stored.GetProperty("id").GetString())).GetProperty("downloads").GetInt64().ShouldBe(1);
    }

    [Theory]
    [InlineData("notes.fbk", "{\"hello\": 1}")]
    [InlineData("notes.fbk", "not json at all")]
    [InlineData("notes.txt", "{\"Nodes\": [{}]}")]
    [InlineData("broken.fbkb", "not a zip")]
    public async Task A_file_that_is_not_a_patch_is_refused(string fileName, string text)
    {
        using var response = await Post(Encoding.UTF8.GetBytes(text), fileName);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Get("/api/v1/presets")).GetProperty("total").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Presets_are_found_by_tag_and_by_words()
    {
        await Submit(PatchFile("Rain on a tin roof.", "Ada", "ambient"), "Rain.fbk");
        await Submit(PatchFile("A four-to-the-floor kick.", "Bo", "techno"), "Kick.fbk");

        var tagged = await Get("/api/v1/presets?tag=techno");
        tagged.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ShouldBe(["Kick"]);

        var searched = await Get("/api/v1/presets?q=tin%20roof");
        searched.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ShouldBe(["Rain"]);

        var tags = await Get("/api/v1/tags");
        tags.EnumerateArray().Select(t => t.GetProperty("tag").GetString()).ShouldBe(["ambient", "techno"]);
    }

    [Fact]
    public async Task A_preset_shows_its_media_once_the_render_app_has_written_it()
    {
        var id = (await Submit(PatchFile())).GetProperty("id").GetString()!;

        var before = (await Get("/api/v1/presets/" + id)).GetProperty("media");
        before.GetProperty("still").ValueKind.ShouldBe(JsonValueKind.Null);
        before.GetProperty("state").GetString().ShouldBe("pending");

        await File.WriteAllBytesAsync(Path.Combine(Media, id + ".webp"), [1, 2, 3], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Media, id + ".peaks.json"), "[0.5, 1]", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Media, id + ".done"), "", TestContext.Current.CancellationToken);

        var after = (await Get("/api/v1/presets/" + id)).GetProperty("media");
        after.GetProperty("still").GetString().ShouldBe($"/media/{id}.webp");
        after.GetProperty("peaks").EnumerateArray().Select(p => p.GetDouble()).ShouldBe([0.5, 1]);
        after.GetProperty("state").GetString().ShouldBe("done");

        (await client.GetByteArrayAsync(new Uri($"/media/{id}.webp", UriKind.Relative), TestContext.Current.CancellationToken))
            .ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task A_preset_stops_waiting_for_a_render_once_it_is_done_or_has_failed()
    {
        var done = (await Submit(PatchFile(), "Done.fbk")).GetProperty("id").GetString()!;
        var failed = (await Submit(PatchFile(), "Failed.fbk")).GetProperty("id").GetString()!;
        var waiting = (await Submit(PatchFile(), "Waiting.fbk")).GetProperty("id").GetString()!;

        await File.WriteAllTextAsync(Path.Combine(Media, done + ".done"), "", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Media, failed + ".failed"), "no plugin", TestContext.Current.CancellationToken);

        var pending = await Get("/api/v1/presets?pending=true");

        pending.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ShouldBe([waiting]);
    }

    [Fact]
    public async Task Markers_are_not_served()
    {
        var id = (await Submit(PatchFile())).GetProperty("id").GetString()!;
        await File.WriteAllTextAsync(Path.Combine(Media, id + ".failed"), "stack trace", TestContext.Current.CancellationToken);

        using var response = await client.GetAsync(new Uri($"/media/{id}.failed", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_pages_and_the_shared_stylesheet_are_served()
    {
        (await client.GetStringAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken)).ShouldContain("Submit a preset");
        (await client.GetStringAsync(new Uri("/assets/site.css", UriKind.Relative), TestContext.Current.CancellationToken)).ShouldContain("--attention");
    }

    public sealed class Flooding : IDisposable
    {
        private readonly ServerTests server = new(postsPerHour: 2);

        public void Dispose() => server.Dispose();

        [Fact]
        public async Task A_flood_of_submissions_is_turned_away()
        {
            await server.Submit(PatchFile());
            await server.Submit(PatchFile());

            using var third = await server.Post(PatchFile(), "Third.fbk");

            third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        }
    }
}
