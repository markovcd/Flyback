using System.Security.Cryptography;
using System.Text;
using Flyback.Site;
using Flyback.Site.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace Flyback.Server.Tests;

/// <summary>
/// The site's database as this site writes it, exported for the Worker. The rows are the
/// fixture the Worker's own tests load (worker/test/fixtures/export-site.sql), so a change
/// to either side that breaks the move fails one of the two.
/// </summary>
public sealed class ExportSiteTests : IDisposable
{
    private const string Lantern = "0199a000000070008000000000000001";
    private const string Bundle = "0199a000000070008000000000000002";
    private const string Figures = "0199a000000070008000000000000003";

    /// <summary>The rating key, fixed so the voter below is the one the Worker computes for 203.0.113.1.</summary>
    private static readonly byte[] RatingKey = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];

    private readonly string folder = Directory.CreateTempSubdirectory("flyback-export-").FullName;

    public void Dispose()
    {
        Databases.Release(folder);
        Directory.Delete(folder, recursive: true);
    }

    private string Database => Path.Combine(folder, "presets.db");

    private static readonly byte[] LanternPatch = Encoding.UTF8.GetBytes(
        """{"Requires":[{"Id":"example.lantern","Name":"Lantern"}],"Nodes":[{"Id":"8f9d1d3e-0000-4000-8000-000000000012","TypeId":"example.lantern.glow"}],"Connections":[]}""");

    private static readonly byte[] BundleBytes = [0x50, 0x4B, 0x05, 0x06, .. new byte[18]];

    private static readonly string FiguresHash = string.Concat(Enumerable.Repeat("f00d", 16));

    private static readonly string SignerKey = Convert.ToBase64String(ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportSubjectPublicKeyInfo());

    /// <summary>The schema, made by starting the site on an empty database, then rows of every kind with fixed ids.</summary>
    private void Fill()
    {
        using (var host = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseSetting("Site:Database", Database);
            web.UseSetting("Site:Defaults", Path.Combine(folder, "no-defaults"));
            web.UseSetting("Site:Builds", Path.Combine(folder, "no-builds"));
            web.UseSetting("Site:Media", Path.Combine(folder, "unused-media"));
        }))
        using (var client = host.CreateClient())
            client.GetAsync(new Uri("/api/v1/presets", UriKind.Relative), TestContext.Current.CancellationToken).GetAwaiter().GetResult().EnsureSuccessStatusCode();

        Databases.Release(folder);

        using var db = new SqliteConnection($"Data Source={Database};Pooling=false");
        db.Open();

        void Run(string sql, params (string Name, object? Value)[] values)
        {
            using var command = db.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        const string preset = """
            INSERT INTO presets (id, name, author, description, file_name, file, size, submitted_at, downloads, published, packed)
            VALUES ($id, $name, $author, $description, $file_name, $file, $size, $at, $downloads, $published, $packed)
            """;

        Run(preset, ("$id", Lantern), ("$name", "Łódź lantern"), ("$author", "Ada"), ("$description", "A glow; it says \"hi\" --twice."),
            ("$file_name", "Lantern.fbk"), ("$file", Packing.Pack(LanternPatch)), ("$size", LanternPatch.LongLength),
            ("$at", "2026-09-01T10:00:00.0000000Z"), ("$downloads", 7), ("$published", 1), ("$packed", 1));
        Run(preset, ("$id", Bundle), ("$name", "Tape"), ("$author", null), ("$description", null),
            ("$file_name", "Tape.fbkb"), ("$file", BundleBytes), ("$size", BundleBytes.LongLength),
            ("$at", "2026-09-02T10:00:00.0000000Z"), ("$downloads", 0), ("$published", 0), ("$packed", 0));

        Run("INSERT INTO preset_tags (preset_id, tag) VALUES ($id, 'ambient'), ($id, 'glow')", ("$id", Lantern));
        Run("INSERT INTO defaults (file_name, preset_id, hash) VALUES ('Lantern.fbk', $id, 'abc123')", ("$id", Lantern));

        Run("""
            INSERT INTO plugins (id, assembly, name, version, author, description, adds, reaches, builds, contract, sha256, file_name, file, size,
                                 submitted_at, downloads, published, tags, preview, preview_type, modules, signer)
            VALUES ($id, 'Flyback.Plugins.Figures', 'Figures', '1.2.0', 'Flyback', 'Shapes to draw with.', 'modules', '', $builds, $contract, $sha256,
                    'Flyback.Plugins.Figures.fbkp', $file, 4, '2026-09-03T10:00:00.0000000Z', 3, 1, $tags, $preview, 'image/png', $modules, $signer)
            """,
            ("$id", Figures), ("$builds", "win\u001Flinux"), ("$contract", "Flyback.Core 1.2.0\u001FFlyback.Plugins 1.2.0"),
            ("$file", new byte[] { 0x50, 0x4B, 0x03, 0x04 }), ("$tags", "shapes"), ("$preview", new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            ("$modules", "flyback.figures.circle\u001ECircle"), ("$signer", SignerKey), ("$sha256", FiguresHash));
        Run("INSERT INTO plugin_defaults (file_name, plugin_id, hash) VALUES ('Figures.fbkp', $id, $sha256)", ("$id", Figures), ("$sha256", FiguresHash));

        Run("INSERT INTO reports (id, kind, subject_id, reason, details, submitted_at) VALUES ('r1', 'preset', $id, 'broken', 'No sound.', '2026-09-04T10:00:00.0000000Z')", ("$id", Lantern));

        Run("DELETE FROM rating_key");
        Run("INSERT INTO rating_key (key) VALUES ($key)", ("$key", RatingKey));
        var voter = Convert.ToHexStringLower(HMACSHA256.HashData(RatingKey, Encoding.UTF8.GetBytes("203.0.113.1")));
        Run("INSERT INTO ratings (kind, subject_id, voter, stars, rated_at) VALUES ('preset', $id, $voter, 4, '2026-09-05T10:00:00.0000000Z')",
            ("$id", Lantern), ("$voter", voter));

        Run("""
            INSERT INTO letters (id, mood, message, contact, version, platform, plugins, submitted_at)
            VALUES ('l1', 'idea', $message, NULL, '1.4.0', 'Linux', NULL, '2026-09-06T10:00:00.0000000Z')
            """, ("$message", "Two lines:\nthe second; with a quote ' in it."));

        var media = Path.Combine(folder, "media");
        Directory.CreateDirectory(media);
        File.WriteAllBytes(Path.Combine(media, Lantern + ".webp"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(media, Lantern + ".peaks.json"), "[0.5, 1]");
        File.WriteAllText(Path.Combine(media, Lantern + ".done"), "");
        File.WriteAllText(Path.Combine(media, Bundle + ".failed"), "no plugin");
    }

    private DirectoryInfo Exported()
    {
        Fill();

        var into = new DirectoryInfo(Path.Combine(folder, "out"));
        var output = new StringWriter();

        ExportSiteCommand.Run(new FileInfo(Database), new DirectoryInfo(Path.Combine(folder, "media")), into, BrowserPlugins.Linked(), output, TextWriter.Null)
            .ShouldBe(0);
        output.ToString().ShouldStartWith("2 presets, 1 plugins and 6 files written");

        return into;
    }

    [Fact]
    public void Every_file_is_written_unpacked_at_the_key_the_worker_keeps_it_under()
    {
        var files = Path.Combine(Exported().FullName, "files");

        File.ReadAllBytes(Path.Combine(files, "presets", Lantern)).ShouldBe(LanternPatch);
        File.ReadAllBytes(Path.Combine(files, "presets", Bundle)).ShouldBe(BundleBytes);
        File.ReadAllBytes(Path.Combine(files, "plugins", Figures)).ShouldBe([0x50, 0x4B, 0x03, 0x04]);
        File.ReadAllBytes(Path.Combine(files, "plugins", Figures + ".preview")).ShouldBe([0x89, 0x50, 0x4E, 0x47]);
        File.ReadAllBytes(Path.Combine(files, "media", Lantern + ".webp")).ShouldBe([1, 2, 3]);
        File.Exists(Path.Combine(files, "media", Lantern + ".done")).ShouldBeFalse("a marker is a column now");
    }

    /// <summary>On a mismatch the new rows are written beside the fixture as .received.sql, to read and move over it.</summary>
    [Fact]
    public void The_rows_are_the_ones_the_worker_loads()
    {
        var rows = File.ReadAllText(Path.Combine(Exported().FullName, "rows.sql"));
        rows = rows.Replace(SignerKey, "SIGNER-KEY", StringComparison.Ordinal)
            .Replace(Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(SignerKey))), "SIGNER-FINGERPRINT", StringComparison.Ordinal);

        var fixture = Path.Combine(Repository(), "worker", "test", "fixtures", "export-site.sql");
        var received = Path.ChangeExtension(fixture, ".received.sql");

        if (File.Exists(fixture) && File.ReadAllText(fixture) == rows)
        {
            File.Delete(received);
            return;
        }

        File.WriteAllText(received, rows, new UTF8Encoding(false));
        Assert.Fail($"The exported rows are not {fixture}. Read {received}, and move it over the fixture if it is right.");
    }

    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
            if (File.Exists(Path.Combine(at.FullName, "Flyback.slnx"))) return at.FullName;

        throw new InvalidOperationException("The tests are not running inside the repository.");
    }
}
