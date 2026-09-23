using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flyback.Core.Graph;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace Flyback.Server.Tests;

/// <summary>
/// The presets the site starts with: files in its defaults folder, added as it
/// starts and kept to the file (ADR-0138).
/// </summary>
public sealed class DefaultsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-defaults-").FullName;

    private string Shipped => Path.Combine(folder, "defaults");

    public DefaultsTests() => Directory.CreateDirectory(Shipped);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    /// <summary>The site started on this folder's database and defaults, as a restart would find them.</summary>
    private WebApplicationFactory<Program> Start(string? defaults = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Presets:Database", Path.Combine(folder, "presets.db"));
            web.UseSetting("Presets:Media", Path.Combine(folder, "media"));
            web.UseSetting("Presets:Defaults", defaults ?? Shipped);
            web.UseSetting("Presets:Admin:User", "admin");
            web.UseSetting("Presets:Admin:Password", "hunter2");
        });

    private void Ship(string fileName, string description)
    {
        var patch = new Patch();
        patch.EnsureOutput(NodeCatalog.Current);
        patch.Describe(description);

        File.WriteAllText(Path.Combine(Shipped, fileName), PatchIO.ToJson(patch));
    }

    private static async Task<JsonElement[]> Shelf(WebApplicationFactory<Program> site)
    {
        using var client = site.CreateClient();
        var list = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/v1/presets", UriKind.Relative), TestContext.Current.CancellationToken);

        return [.. list.GetProperty("items").EnumerateArray()];
    }

    [Fact]
    public async Task The_site_starts_with_Tranquility()
    {
        using var site = Start(Path.Combine(AppContext.BaseDirectory, "Defaults"));
        var shelf = await Shelf(site);

        var tranquility = shelf.Single(p => p.GetProperty("name").GetString() == "Tranquility");
        tranquility.GetProperty("author").GetString().ShouldBe("Flyback");
        tranquility.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldContain("psytrance");
    }

    [Fact]
    public async Task A_default_is_on_the_shelf_when_the_site_starts()
    {
        Ship("Drone.fbk", "A slow drone.");

        using var site = Start();
        var shelf = await Shelf(site);

        shelf.Length.ShouldBe(1);
        shelf[0].GetProperty("name").GetString().ShouldBe("Drone");
        shelf[0].GetProperty("description").GetString().ShouldBe("A slow drone.");
    }

    [Fact]
    public async Task Starting_again_adds_a_default_once()
    {
        Ship("Drone.fbk", "A slow drone.");

        using (var first = Start()) await Shelf(first);
        using var again = Start();

        (await Shelf(again)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_migrated_default_replaces_the_stored_file_under_the_same_id()
    {
        Ship("Drone.fbk", "A slow drone.");

        string id;
        using (var first = Start()) id = (await Shelf(first))[0].GetProperty("id").GetString()!;

        Ship("Drone.fbk", "A slower drone.");

        using var again = Start();
        var shelf = await Shelf(again);

        shelf.Length.ShouldBe(1);
        shelf[0].GetProperty("id").GetString().ShouldBe(id);
        shelf[0].GetProperty("description").GetString().ShouldBe("A slower drone.");

        using var client = again.CreateClient();
        var file = await client.GetByteArrayAsync(
            new Uri($"/api/v1/presets/{id}/file?count=false", UriKind.Relative), TestContext.Current.CancellationToken);

        file.ShouldBe(await File.ReadAllBytesAsync(Path.Combine(Shipped, "Drone.fbk"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_default_the_admin_deleted_stays_deleted()
    {
        Ship("Drone.fbk", "A slow drone.");

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
                new Uri("/api/v1/presets/" + id, UriKind.Relative), TestContext.Current.CancellationToken);
            deleted.IsSuccessStatusCode.ShouldBeTrue();
        }

        using var again = Start();

        (await Shelf(again)).ShouldBeEmpty();
    }
}
