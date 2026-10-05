using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Flyback.Site;

/// <summary>
/// The .NET site's database and media folder, written out for the Worker: <c>rows.sql</c>
/// for D1, and under <c>files/</c> every file at the key R2 keeps it under. Ids are kept,
/// so every link, rating and kept shared preset still resolves.
/// </summary>
internal static class SiteExport
{
    private const char Separator = '\u001F';

    /// <summary>What render-presets left in the media folder that the Worker serves, by the name it is uploaded under.</summary>
    private static readonly (string Name, string Suffix)[] Served = [("mp3", ".mp3"), ("peaks.json", ".peaks.json"), ("webm", ".webm"), ("webp", ".webp")];

    public sealed record Counted(int Presets, int Plugins, int Files);

    public static Counted Write(string database, string? media, string into, BrowserPlugins browser)
    {
        var files = Path.Combine(into, "files");
        Directory.CreateDirectory(files);

        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();

        var sql = new List<string>();
        var written = 0;

        void Put(string key, byte[] bytes)
        {
            var path = Path.Combine(files, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            written++;
        }

        var presets = 0;

        foreach (var row in Rows(db, "SELECT id, name, author, description, file_name, file, packed, size, submitted_at, downloads, published FROM presets ORDER BY id"))
        {
            var id = (string)row["id"]!;
            var file = (long)row["packed"]! != 0 ? Packing.Unpack((byte[])row["file"]!) : (byte[])row["file"]!;
            var fileName = (string)row["file_name"]!;
            var (state, present, reason, peaks) = MediaOf(media, id, Put);

            Put($"presets/{id}", file);

            sql.Add(Sql.Insert("presets",
            [
                ("id", id),
                ("name", row["name"]),
                ("author", row["author"]),
                ("description", row["description"]),
                ("file_name", fileName),
                ("size", row["size"]),
                ("submitted_at", row["submitted_at"]),
                ("downloads", row["downloads"]),
                ("published", row["published"]),
                ("status", "checked"),
                ("search", Folded(row["name"], row["author"], row["description"])),
                ("lacks", browser.Lacking(fileName, file) is { } lack ? JsonSerializer.Serialize(lack, Checks.Json) : null),
                ("media_state", state),
                ("media", present),
                ("media_reason", reason),
                ("peaks", peaks),
            ]));
            presets++;
        }

        foreach (var row in Rows(db, "SELECT preset_id, tag FROM preset_tags ORDER BY preset_id, tag"))
            sql.Add(Sql.Insert("preset_tags", [("preset_id", row["preset_id"]), ("tag", row["tag"])]));

        foreach (var row in Rows(db, "SELECT file_name, preset_id, hash FROM defaults ORDER BY file_name"))
            sql.Add(Sql.Insert("defaults", [("file_name", row["file_name"]), ("preset_id", row["preset_id"]), ("hash", row["hash"])]));

        var plugins = 0;

        foreach (var row in Rows(db, """
            SELECT id, assembly, name, version, author, description, adds, reaches, builds, contract, sha256, file_name, file, size,
                   submitted_at, downloads, published, tags, preview, preview_type, modules, signer
            FROM plugins ORDER BY id
            """))
        {
            var id = (string)row["id"]!;

            Put($"plugins/{id}", (byte[])row["file"]!);
            if (row["preview"] is byte[] preview) Put($"plugins/{id}.preview", preview);

            sql.Add(Sql.Insert("plugins",
            [
                ("id", id),
                ("assembly", row["assembly"]),
                ("name", row["name"]),
                ("version", row["version"]),
                ("author", row["author"]),
                ("description", row["description"]),
                ("adds", row["adds"]),
                ("reaches", row["reaches"]),
                ("builds", row["builds"]),
                ("contract", row["contract"]),
                ("sha256", row["sha256"]),
                ("file_name", row["file_name"]),
                ("size", row["size"]),
                ("submitted_at", row["submitted_at"]),
                ("downloads", row["downloads"]),
                ("published", row["published"]),
                ("tags", row["tags"]),
                ("preview_type", row["preview"] is byte[] ? row["preview_type"] : null),
                ("modules", row["modules"]),
                ("signer", row["signer"]),
                ("signer_fingerprint", row["signer"] is string key ? Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(key))) : null),
                ("status", "checked"),
                ("search", Folded(row["name"], row["author"], row["description"], row["assembly"], row["tags"], row["modules"])),
            ]));
            plugins++;
        }

        foreach (var row in Rows(db, "SELECT file_name, plugin_id, hash FROM plugin_defaults ORDER BY file_name"))
            sql.Add(Sql.Insert("plugin_defaults", [("file_name", row["file_name"]), ("plugin_id", row["plugin_id"]), ("hash", row["hash"])]));

        foreach (var row in Rows(db, "SELECT id, kind, subject_id, reason, details, submitted_at FROM reports ORDER BY id"))
            sql.Add(Sql.Insert("reports", [.. row.Select(c => (c.Key, c.Value))]));

        foreach (var row in Rows(db, "SELECT kind, subject_id, voter, stars, rated_at FROM ratings ORDER BY kind, subject_id, voter"))
            sql.Add(Sql.Insert("ratings", [.. row.Select(c => (c.Key, c.Value))]));

        // The key every voter was kept under, so a visitor who rated before still has one rating each.
        foreach (var row in Rows(db, "SELECT key FROM rating_key LIMIT 1"))
        {
            sql.Add("DELETE FROM rating_key;");
            sql.Add(Sql.Insert("rating_key", [("key", row["key"])]));
        }

        foreach (var row in Rows(db, "SELECT id, mood, message, contact, version, platform, plugins, submitted_at FROM letters ORDER BY id"))
            sql.Add(Sql.Insert("letters", [.. row.Select(c => (c.Key, c.Value))]));

        File.WriteAllText(Path.Combine(into, "rows.sql"), string.Join('\n', sql) + "\n", new UTF8Encoding(false));

        return new Counted(presets, plugins, written);
    }

    /// <summary>What render-presets made of the preset: done or failed with why, which files are there, and the bars.</summary>
    private static (string State, string Present, string? Reason, string? Peaks) MediaOf(string? media, string id, Action<string, byte[]> put)
    {
        if (media is null) return ("pending", "", null, null);

        var present = new List<string>();
        string? peaks = null;

        foreach (var (name, suffix) in Served)
        {
            var path = Path.Combine(media, id + suffix);
            if (!File.Exists(path)) continue;

            var bytes = File.ReadAllBytes(path);

            if (name == "peaks.json")
            {
                try
                {
                    peaks = JsonSerializer.Serialize(JsonSerializer.Deserialize<double[]>(bytes));
                }
                catch (JsonException)
                {
                    continue;
                }
            }

            put($"media/{id}{suffix}", bytes);
            present.Add(name);
        }

        var failed = Path.Combine(media, id + ".failed");

        var (state, reason) = File.Exists(failed) ? ("failed", File.ReadAllText(failed))
            : File.Exists(Path.Combine(media, id + ".done")) ? ("done", null)
            : ("pending", (string?)null);

        return (state, string.Join(',', present), reason, peaks);
    }

    /// <summary>What the Worker searches: each part lowercased, kept apart so no word spans two.</summary>
    private static string Folded(params object?[] parts) =>
        string.Join(Separator, parts.Select(p => (p as string ?? "").ToLowerInvariant()));

    private static IEnumerable<Dictionary<string, object?>> Rows(SqliteConnection db, string query)
    {
        using var command = db.CreateCommand();
        command.CommandText = query;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);

            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);

            yield return row;
        }
    }
}
