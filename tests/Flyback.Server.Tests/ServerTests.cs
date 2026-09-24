using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flyback.Core.Graph;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.Server.Tests;

public sealed class ServerTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-presets-").FullName;
    private readonly WebApplicationFactory<Program> host;
    private readonly HttpClient client;

    public ServerTests() : this(20) { }

    private ServerTests(int postsPerHour, string adminPassword = "hunter2", int lettersPerHour = 20, string? connectedFrom = null)
    {
        host = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Site:Database", Path.Combine(folder, "presets.db"));
            web.UseSetting("Site:Defaults", Path.Combine(folder, "no-defaults"));
            web.UseSetting("Site:Media", Media);
            web.UseSetting("Site:PostsPerHour", postsPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture));
            web.UseSetting("Site:LettersPerHour", lettersPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture));
            web.UseSetting("Site:Admin:User", "admin");
            web.UseSetting("Site:Admin:Password", adminPassword);

            if (connectedFrom is not null)
                web.ConfigureServices(services =>
                    services.AddSingleton<IStartupFilter>(new ConnectedFrom(IPAddress.Parse(connectedFrom))));
        });
        client = host.CreateClient();
    }

    /// <summary>
    /// The address the test server's connections appear to come from, which it
    /// otherwise has none of. In front of everything the application adds, so
    /// the forwarded-headers middleware sees it.
    /// </summary>
    private sealed class ConnectedFrom(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                context.Connection.RemoteIpAddress = address;
                await following(context);
            });

            next(app);
        };
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

    private async Task<JsonElement> Submit(byte[] file, string fileName = "Drone.fbk", string? name = null, string? forwardedFor = null)
    {
        using var response = await Post(file, fileName, name, forwardedFor);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> Post(byte[] file, string fileName, string? name = null, string? forwardedFor = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(file), "file", fileName);
        if (name is not null) form.Add(new StringContent(name), "name");

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/presets", UriKind.Relative)) { Content = form };

        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<JsonElement> Get(string path) =>
        await client.GetFromJsonAsync<JsonElement>(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

    private async Task<HttpStatusCode> Status(string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private static IEnumerable<string?> Names(JsonElement list) =>
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString());

    /// <summary>A client of its own, signed in as the admin.</summary>
    private async Task<HttpClient> Admin()
    {
        var admin = host.CreateClient();
        (await SignIn(admin, "hunter2")).ShouldBe(HttpStatusCode.NoContent);

        return admin;
    }

    private static async Task<HttpStatusCode> SignIn(HttpClient to, string password)
    {
        using var response = await to.PostAsJsonAsync(
            new Uri("/api/v1/admin/session", UriKind.Relative), new { user = "admin", password }, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> Change(HttpClient by, string id, object change)
    {
        using var response = await by.PatchAsJsonAsync(new Uri("/api/v1/presets/" + id, UriKind.Relative), change, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> Delete(HttpClient by, string path)
    {
        using var response = await by.DeleteAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

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
    public async Task A_page_far_past_the_last_is_empty()
    {
        await Submit(PatchFile());

        (await Get("/api/v1/presets?page=89478487")).GetProperty("items").GetArrayLength().ShouldBe(0);
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

    [Fact]
    public async Task An_unpublished_preset_is_hidden_from_everyone_but_the_admin()
    {
        var id = (await Submit(PatchFile("Rain.", "Ada", "ambient"), "Rain.fbk")).GetProperty("id").GetString()!;
        using var admin = await Admin();

        (await Change(admin, id, new { published = false })).ShouldBe(HttpStatusCode.OK);

        Names(await Get("/api/v1/presets")).ShouldBeEmpty();
        (await Get("/api/v1/tags")).GetArrayLength().ShouldBe(0);
        (await Status("/api/v1/presets/" + id)).ShouldBe(HttpStatusCode.NotFound);
        (await Status($"/api/v1/presets/{id}/file")).ShouldBe(HttpStatusCode.NotFound);

        var seen = await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/presets", UriKind.Relative), TestContext.Current.CancellationToken);
        seen.GetProperty("items")[0].GetProperty("published").GetBoolean().ShouldBeFalse();

        (await Change(admin, id, new { published = true })).ShouldBe(HttpStatusCode.OK);

        Names(await Get("/api/v1/presets")).ShouldBe(["Rain"]);
    }

    [Fact]
    public async Task An_unpublished_preset_waits_for_its_render_until_it_is_published_again()
    {
        var id = (await Submit(PatchFile())).GetProperty("id").GetString()!;
        using var admin = await Admin();

        await Change(admin, id, new { published = false });

        (await Get("/api/v1/presets?pending=true")).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_admin_renames_a_preset()
    {
        var id = (await Submit(PatchFile())).GetProperty("id").GetString()!;
        using var admin = await Admin();

        (await Change(admin, id, new { name = "  Night   bus " })).ShouldBe(HttpStatusCode.OK);

        Names(await Get("/api/v1/presets")).ShouldBe(["Night bus"]);
        (await Change(admin, id, new { name = "   " })).ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_admin_deletes_a_preset()
    {
        var id = (await Submit(PatchFile("Rain.", "Ada", "ambient"))).GetProperty("id").GetString()!;
        using var admin = await Admin();

        (await Delete(admin, "/api/v1/presets/" + id)).ShouldBe(HttpStatusCode.NoContent);

        (await Get("/api/v1/presets")).GetProperty("total").GetInt32().ShouldBe(0);
        (await Get("/api/v1/tags")).GetArrayLength().ShouldBe(0);
        (await Delete(admin, "/api/v1/presets/" + id)).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nobody_else_changes_or_deletes_a_preset()
    {
        var id = (await Submit(PatchFile())).GetProperty("id").GetString()!;

        (await SignIn(client, "wrong")).ShouldBe(HttpStatusCode.Unauthorized);
        (await Change(client, id, new { name = "Mine now", published = false })).ShouldBe(HttpStatusCode.Unauthorized);
        (await Delete(client, "/api/v1/presets/" + id)).ShouldBe(HttpStatusCode.Unauthorized);

        Names(await Get("/api/v1/presets")).ShouldBe(["Drone"]);
    }

    [Fact]
    public async Task Signing_out_ends_admin_mode()
    {
        using var admin = await Admin();
        var state = new Uri("/api/v1/admin", UriKind.Relative);

        (await admin.GetFromJsonAsync<JsonElement>(state, TestContext.Current.CancellationToken)).GetProperty("signedIn").GetBoolean().ShouldBeTrue();

        (await Delete(admin, "/api/v1/admin/session")).ShouldBe(HttpStatusCode.NoContent);

        (await admin.GetFromJsonAsync<JsonElement>(state, TestContext.Current.CancellationToken)).GetProperty("signedIn").GetBoolean().ShouldBeFalse();
    }

    public sealed class WithoutAnAdmin : IDisposable
    {
        private readonly ServerTests server = new(20, adminPassword: "");

        public void Dispose() => server.Dispose();

        [Fact]
        public async Task Admin_mode_is_off_until_a_password_is_configured()
        {
            (await server.Get("/api/v1/admin")).GetProperty("enabled").GetBoolean().ShouldBeFalse();
            (await SignIn(server.client, "")).ShouldBe(HttpStatusCode.NotFound);
        }
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

    /// <summary>
    /// A limiter counts by address, and the address is read from a header where
    /// the proxy is the one who wrote it. From anywhere else the header is a
    /// claim, and believing it would hand every request an allowance of its own.
    /// </summary>
    public sealed class ClaimingToBeSomebodyElse : IDisposable
    {
        private readonly ServerTests server = new(postsPerHour: 2, connectedFrom: "203.0.113.7");

        public void Dispose() => server.Dispose();

        [Fact]
        public async Task A_flood_is_turned_away_however_it_signs_itself()
        {
            await server.Submit(PatchFile(), forwardedFor: "198.51.100.1");
            await server.Submit(PatchFile(), forwardedFor: "198.51.100.2");

            using var third = await server.Post(PatchFile(), "Third.fbk", forwardedFor: "198.51.100.3");

            third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        }
    }

    /// <summary>The other half: the proxy's word is what the limiters count by.</summary>
    public sealed class BehindTheProxy : IDisposable
    {
        private readonly ServerTests server = new(postsPerHour: 2, connectedFrom: "10.1.2.3");

        public void Dispose() => server.Dispose();

        [Fact]
        public async Task Each_client_the_proxy_names_has_an_allowance_of_its_own()
        {
            await server.Submit(PatchFile(), forwardedFor: "198.51.100.1");
            await server.Submit(PatchFile(), forwardedFor: "198.51.100.1");

            using var third = await server.Post(PatchFile(), "Third.fbk", forwardedFor: "198.51.100.1");
            third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

            using var another = await server.Post(PatchFile(), "Fourth.fbk", forwardedFor: "198.51.100.2");
            another.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
    }

    public sealed class TooManyLetters : IDisposable
    {
        private readonly ServerTests server = new(postsPerHour: 20, lettersPerHour: 2);

        public void Dispose() => server.Dispose();

        [Fact]
        public async Task A_flood_of_letters_is_turned_away()
        {
            (await server.Write(new { mood = "good", message = "One." })).ShouldBe(HttpStatusCode.NoContent);
            (await server.Write(new { mood = "good", message = "Two." })).ShouldBe(HttpStatusCode.NoContent);

            (await server.Write(new { mood = "good", message = "Three." })).ShouldBe(HttpStatusCode.TooManyRequests);
        }
    }

    private async Task<HttpStatusCode> Report(string id, object report)
    {
        using var response = await client.PostAsJsonAsync(new Uri($"/api/v1/presets/{id}/reports", UriKind.Relative), report, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    [Fact]
    public async Task Anyone_can_report_a_preset_and_only_the_admin_reads_the_reports()
    {
        var id = (await Submit(PatchFile(), name: "Drone")).GetProperty("id").GetString()!;

        (await Report(id, new { reason = "offensive", details = "  Look at the picture.  " })).ShouldBe(HttpStatusCode.NoContent);
        (await Report(id, new { reason = "broken" })).ShouldBe(HttpStatusCode.NoContent);

        (await Status("/api/v1/reports")).ShouldBe(HttpStatusCode.Unauthorized);

        using var admin = await Admin();
        var reports = (await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/reports", UriKind.Relative), TestContext.Current.CancellationToken))
            .EnumerateArray().ToList();

        reports.Select(r => r.GetProperty("reason").GetString()).ShouldBe(["broken", "offensive"]);
        reports.ShouldAllBe(r => r.GetProperty("kind").GetString() == "preset" && r.GetProperty("name").GetString() == "Drone");
        reports[1].GetProperty("details").GetString().ShouldBe("Look at the picture.");
        reports[0].GetProperty("details").ValueKind.ShouldBe(JsonValueKind.Null);

        (await Delete(client, "/api/v1/reports/" + reports[0].GetProperty("id").GetString())).ShouldBe(HttpStatusCode.Unauthorized);
        (await Delete(admin, "/api/v1/reports/" + reports[0].GetProperty("id").GetString())).ShouldBe(HttpStatusCode.NoContent);

        (await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/reports", UriKind.Relative), TestContext.Current.CancellationToken))
            .GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_report_needs_a_known_reason_and_a_preset_that_is_there_to_see()
    {
        var id = (await Submit(PatchFile())).GetProperty("id").GetString()!;

        (await Report(id, new { reason = "boring" })).ShouldBe(HttpStatusCode.BadRequest);
        (await Report(id, new { details = "No reason given." })).ShouldBe(HttpStatusCode.BadRequest);
        (await Report(id, new { reason = "other", details = new string('x', 1001) })).ShouldBe(HttpStatusCode.BadRequest);
        (await Report("nothing-here", new { reason = "other" })).ShouldBe(HttpStatusCode.NotFound);

        using var admin = await Admin();
        (await Change(admin, id, new { published = false })).ShouldBe(HttpStatusCode.OK);

        (await Report(id, new { reason = "other" })).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_a_preset_takes_its_reports_with_it()
    {
        var id = (await Submit(PatchFile())).GetProperty("id").GetString()!;

        (await Report(id, new { reason = "stolen" })).ShouldBe(HttpStatusCode.NoContent);

        using var admin = await Admin();
        (await Delete(admin, "/api/v1/presets/" + id)).ShouldBe(HttpStatusCode.NoContent);

        (await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/reports", UriKind.Relative), TestContext.Current.CancellationToken))
            .GetArrayLength().ShouldBe(0);
    }

    /// <summary>Puts the stars as the site's own page does, from <paramref name="address"/>.</summary>
    private async Task<(HttpStatusCode Status, JsonElement Said)> Rate(string id, object rating, string? address = "203.0.113.1", bool fromTheSite = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/v1/presets/{id}/rating", UriKind.Relative)) { Content = JsonContent.Create(rating) };

        if (fromTheSite) request.Headers.Add("Sec-Fetch-Site", "same-origin");
        if (address is not null) request.Headers.Add("X-Forwarded-For", address);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        return (response.StatusCode, response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)
            : default);
    }

    [Fact]
    public async Task A_preset_is_rated_once_per_address_and_listed_with_its_average()
    {
        var id = (await Submit(PatchFile(), name: "Drone")).GetProperty("id").GetString()!;

        (await Rate(id, new { stars = 2 }, "203.0.113.1")).Status.ShouldBe(HttpStatusCode.OK);
        (await Rate(id, new { stars = 5 }, "203.0.113.2")).Status.ShouldBe(HttpStatusCode.OK);

        var (_, again) = await Rate(id, new { stars = 4 }, "203.0.113.1");

        again.GetProperty("average").GetDouble().ShouldBe(4.5);
        again.GetProperty("count").GetInt32().ShouldBe(2);
        again.GetProperty("mine").GetInt32().ShouldBe(4);

        var listed = (await Get("/api/v1/presets")).GetProperty("items").EnumerateArray().Single().GetProperty("rating");

        listed.GetProperty("average").GetDouble().ShouldBe(4.5);
        listed.GetProperty("count").GetInt32().ShouldBe(2);
        (await Get("/api/v1/presets/" + id)).GetProperty("rating").GetProperty("count").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task A_preset_nobody_rated_says_so()
    {
        var id = (await Submit(PatchFile())).GetProperty("id").GetString()!;
        var rating = await Get($"/api/v1/presets/{id}/rating");

        rating.GetProperty("count").GetInt32().ShouldBe(0);
        rating.GetProperty("mine").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_rating_is_taken_only_from_the_site_and_only_one_to_five_stars()
    {
        var id = (await Submit(PatchFile())).GetProperty("id").GetString()!;

        (await Rate(id, new { stars = 5 }, fromTheSite: false)).Status.ShouldBe(HttpStatusCode.Forbidden, "the editor reads ratings and never gives them");
        (await Rate(id, new { stars = 0 })).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await Rate(id, new { stars = 6 })).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await Rate(id, new { })).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await Rate("nothing-here", new { stars = 3 })).Status.ShouldBe(HttpStatusCode.NotFound);

        using var admin = await Admin();
        (await Change(admin, id, new { published = false })).ShouldBe(HttpStatusCode.OK);

        (await Rate(id, new { stars = 3 })).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_a_preset_takes_its_ratings_with_it()
    {
        var id = (await Submit(PatchFile())).GetProperty("id").GetString()!;

        (await Rate(id, new { stars = 1 })).Status.ShouldBe(HttpStatusCode.OK);

        using var admin = await Admin();
        (await Delete(admin, "/api/v1/presets/" + id)).ShouldBe(HttpStatusCode.NoContent);

        using var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(folder, "presets.db"));
        db.Open();
        using var count = db.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM ratings";

        ((long)count.ExecuteScalar()!).ShouldBe(0);
    }

    private async Task<HttpStatusCode> Write(object letter)
    {
        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/letters", UriKind.Relative), letter, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    [Fact]
    public async Task Anyone_can_write_to_the_author_and_only_the_admin_reads_the_letters()
    {
        (await Write(new
        {
            mood = "good",
            message = "  The canvas is lovely.  ",
            contact = "  ada@example.org  ",
            version = "1.4.0",
            platform = "Microsoft Windows 10.0.26200",
            plugins = "WinIO, Picture; sound: WASAPI",
        })).ShouldBe(HttpStatusCode.NoContent);

        (await Write(new { mood = "bad", message = "The delay clicks." })).ShouldBe(HttpStatusCode.NoContent);

        (await Status("/api/v1/letters")).ShouldBe(HttpStatusCode.Unauthorized);

        using var admin = await Admin();
        var letters = (await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/letters", UriKind.Relative), TestContext.Current.CancellationToken))
            .EnumerateArray().ToList();

        letters.Select(l => l.GetProperty("mood").GetString()).ShouldBe(["bad", "good"]);
        letters[1].GetProperty("message").GetString().ShouldBe("The canvas is lovely.");
        letters[1].GetProperty("contact").GetString().ShouldBe("ada@example.org");
        letters[1].GetProperty("version").GetString().ShouldBe("1.4.0");
        letters[1].GetProperty("plugins").GetString().ShouldBe("WinIO, Picture; sound: WASAPI");

        letters[0].GetProperty("contact").ValueKind.ShouldBe(JsonValueKind.Null, "an address left blank is no address");
        letters[0].GetProperty("version").ValueKind.ShouldBe(JsonValueKind.Null);

        (await Delete(client, "/api/v1/letters/" + letters[0].GetProperty("id").GetString())).ShouldBe(HttpStatusCode.Unauthorized);
        (await Delete(admin, "/api/v1/letters/" + letters[0].GetProperty("id").GetString())).ShouldBe(HttpStatusCode.NoContent);

        (await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/letters", UriKind.Relative), TestContext.Current.CancellationToken))
            .GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_letter_needs_a_known_mood_and_something_in_it()
    {
        (await Write(new { mood = "cross", message = "Hello." })).ShouldBe(HttpStatusCode.BadRequest);
        (await Write(new { message = "No mood given." })).ShouldBe(HttpStatusCode.BadRequest);
        (await Write(new { mood = "other" })).ShouldBe(HttpStatusCode.BadRequest);
        (await Write(new { mood = "other", message = "   " })).ShouldBe(HttpStatusCode.BadRequest);
        (await Write(new { mood = "other", message = new string('x', 2001) })).ShouldBe(HttpStatusCode.BadRequest);
        (await Write(new { mood = "other", message = "Fine.", contact = new string('x', 201) })).ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task What_the_editor_says_about_itself_is_cut_rather_than_refused()
    {
        (await Write(new { mood = "bad", message = "It will not start.", plugins = new string('p', 900) })).ShouldBe(HttpStatusCode.NoContent);

        using var admin = await Admin();
        var letters = (await admin.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/letters", UriKind.Relative), TestContext.Current.CancellationToken))
            .EnumerateArray().ToList();

        letters[0].GetProperty("plugins").GetString()!.Length.ShouldBe(600);
    }
}
