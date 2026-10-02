using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace Flyback.Server.Tests;

/// <summary>A preset file is kept brotli-packed and sent to a browser that takes it, to anything else as it was submitted.</summary>
public sealed class PackedPresetTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("flyback-packed-").FullName;

    public void Dispose()
    {
        Databases.Release(folder);
        Directory.Delete(folder, recursive: true);
    }

    private WebApplicationFactory<Program> Start() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Site:Database", Path.Combine(folder, "presets.db"));
            web.UseSetting("Site:Defaults", Path.Combine(folder, "no-defaults"));
            web.UseSetting("Site:Media", Path.Combine(folder, "media"));
        });

    private static byte[] PatchFile()
    {
        var patch = new Patch();
        patch.EnsureOutput(NodeCatalog.Current);
        patch.Describe("A slow drone, with enough words in it to be worth packing.");

        return Encoding.UTF8.GetBytes(PatchIO.ToJson(patch));
    }

    private static byte[] Bundle(byte[] patch)
    {
        using var archive = new MemoryStream();

        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = zip.CreateEntry(PatchBundle.PatchEntry).Open())
            entry.Write(patch);

        return archive.ToArray();
    }

    private static async Task<string> Submit(HttpClient client, byte[] file, string fileName)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(file), "file", fileName } };
        using var response = await client.PostAsync(new Uri("/api/v1/presets", UriKind.Relative), form, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("id").GetString()!;
    }

    private static async Task<HttpResponseMessage> File(HttpClient client, string id, string? encoding)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/v1/presets/{id}/file", UriKind.Relative));

        if (encoding is not null) request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(encoding));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static byte[] Unbrotli(byte[] packed)
    {
        using var unpacked = new MemoryStream();
        using (var brotli = new BrotliStream(new MemoryStream(packed), CompressionMode.Decompress))
            brotli.CopyTo(unpacked);

        return unpacked.ToArray();
    }

    [Fact]
    public async Task A_patch_goes_out_packed_to_a_browser_that_takes_brotli()
    {
        using var site = Start();
        using var client = site.CreateClient();
        var file = PatchFile();
        var id = await Submit(client, file, "Drone.fbk");

        using var response = await File(client, id, "br");
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        response.Content.Headers.ContentEncoding.ShouldBe(["br"]);
        response.Headers.Vary.ShouldContain("Accept-Encoding");
        body.Length.ShouldBeLessThan(file.Length);
        Unbrotli(body).ShouldBe(file);
    }

    [Fact]
    public async Task A_patch_goes_out_as_submitted_to_anything_that_does_not_take_brotli()
    {
        using var site = Start();
        using var client = site.CreateClient();
        var file = PatchFile();
        var id = await Submit(client, file, "Drone.fbk");

        foreach (var encoding in new string?[] { null, "gzip" })
        {
            using var response = await File(client, id, encoding);

            response.Content.Headers.ContentEncoding.ShouldBeEmpty();
            (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(file);
        }
    }

    [Fact]
    public async Task A_bundle_is_not_packed_again()
    {
        using var site = Start();
        using var client = site.CreateClient();
        var file = Bundle(PatchFile());
        var id = await Submit(client, file, "Drone.fbkb");

        using var response = await File(client, id, "br");

        response.Content.Headers.ContentEncoding.ShouldBeEmpty();
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(file);
    }

    [Fact]
    public async Task A_file_stored_before_packing_is_packed_when_the_site_opens_it()
    {
        var file = PatchFile();
        var path = Path.Combine(folder, "presets.db");

        using (var old = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            old.Open();

            using var make = old.CreateCommand();
            make.CommandText = """
                CREATE TABLE presets (
                    id TEXT PRIMARY KEY, name TEXT NOT NULL, author TEXT, description TEXT,
                    file_name TEXT NOT NULL, file BLOB NOT NULL, size INTEGER NOT NULL,
                    submitted_at TEXT NOT NULL, downloads INTEGER NOT NULL DEFAULT 0
                );
                INSERT INTO presets (id, name, file_name, file, size, submitted_at)
                VALUES ('old', 'Old', 'Old.fbk', $file, $size, '2026-01-01T00:00:00.0000000Z');
                """;
            make.Parameters.AddWithValue("$file", file);
            make.Parameters.AddWithValue("$size", file.LongLength);
            make.ExecuteNonQuery();
        }

        using var site = Start();
        using var client = site.CreateClient();

        using var plain = await File(client, "old", null);
        plain.Content.Headers.ContentEncoding.ShouldBeEmpty();
        (await plain.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(file);

        using var packed = await File(client, "old", "br");
        packed.Content.Headers.ContentEncoding.ShouldBe(["br"]);
        Unbrotli(await packed.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(file);
    }
}
