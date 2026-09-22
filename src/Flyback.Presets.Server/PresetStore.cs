using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Flyback.Presets.Server;

/// <summary>A stored preset, without its file.</summary>
internal sealed record StoredPreset(
    string Id,
    string Name,
    string? Author,
    string? Description,
    IReadOnlyList<string> Tags,
    string FileName,
    long Size,
    DateTimeOffset Submitted,
    long Downloads);

/// <summary>A page of presets and how many match in all.</summary>
internal sealed record PresetPage(IReadOnlyList<StoredPreset> Items, int Total);

/// <summary>The presets people have submitted, in one SQLite file.</summary>
internal sealed class PresetStore
{
    private const char Separator = '\u001F';

    private const string Columns = """
        p.id, p.name, p.author, p.description,
        (SELECT group_concat(t.tag, char(31)) FROM preset_tags t WHERE t.preset_id = p.id),
        p.file_name, p.size, p.submitted_at, p.downloads
        """;

    private readonly string connection;

    public PresetStore(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder) Directory.CreateDirectory(folder);

        connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();

        using var db = Open();
        Run(db, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS presets (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                author TEXT,
                description TEXT,
                file_name TEXT NOT NULL,
                file BLOB NOT NULL,
                size INTEGER NOT NULL,
                submitted_at TEXT NOT NULL,
                downloads INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS preset_tags (
                preset_id TEXT NOT NULL REFERENCES presets(id) ON DELETE CASCADE,
                tag TEXT NOT NULL,
                PRIMARY KEY (preset_id, tag)
            );
            CREATE INDEX IF NOT EXISTS preset_tags_tag ON preset_tags(tag);
            CREATE INDEX IF NOT EXISTS presets_submitted ON presets(submitted_at);
            """);
    }

    public StoredPreset Add(Submission submission, DateTimeOffset at)
    {
        var id = Guid.CreateVersion7().ToString("N");

        using var db = Open();
        using var transaction = db.BeginTransaction();

        using (var insert = db.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO presets (id, name, author, description, file_name, file, size, submitted_at)
                VALUES ($id, $name, $author, $description, $file_name, $file, $size, $at)
                """;
            insert.Parameters.AddWithValue("$id", id);
            insert.Parameters.AddWithValue("$name", submission.Name);
            insert.Parameters.AddWithValue("$author", (object?)submission.Author ?? DBNull.Value);
            insert.Parameters.AddWithValue("$description", (object?)submission.Description ?? DBNull.Value);
            insert.Parameters.AddWithValue("$file_name", submission.FileName);
            insert.Parameters.AddWithValue("$file", submission.File);
            insert.Parameters.AddWithValue("$size", submission.File.LongLength);
            insert.Parameters.AddWithValue("$at", at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            insert.ExecuteNonQuery();
        }

        foreach (var tag in submission.Tags)
        {
            using var tagged = db.CreateCommand();
            tagged.CommandText = "INSERT OR IGNORE INTO preset_tags (preset_id, tag) VALUES ($id, $tag)";
            tagged.Parameters.AddWithValue("$id", id);
            tagged.Parameters.AddWithValue("$tag", tag);
            tagged.ExecuteNonQuery();
        }

        transaction.Commit();

        return Find(id)!;
    }

    public StoredPreset? Find(string id)
    {
        using var db = Open();
        using var query = db.CreateCommand();
        query.CommandText = $"SELECT {Columns} FROM presets p WHERE p.id = $id";
        query.Parameters.AddWithValue("$id", id);

        using var reader = query.ExecuteReader();

        return reader.Read() ? Row(reader) : null;
    }

    /// <summary>Newest first, matching all of <paramref name="search"/>'s words and <paramref name="tag"/>.</summary>
    public PresetPage List(string? search, string? tag, int page, int size)
    {
        var where = new List<string>();

        using var db = Open();
        using var query = db.CreateCommand();

        var words = (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < words.Length && i < 8; i++)
        {
            where.Add($"(p.name LIKE $w{i} ESCAPE '\\' OR p.author LIKE $w{i} ESCAPE '\\' OR p.description LIKE $w{i} ESCAPE '\\')");
            query.Parameters.AddWithValue($"$w{i}", "%" + Escaped(words[i]) + "%");
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            where.Add("EXISTS (SELECT 1 FROM preset_tags t WHERE t.preset_id = p.id AND t.tag = $tag)");
            query.Parameters.AddWithValue("$tag", tag.Trim().ToLowerInvariant());
        }

        var filter = where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where);

        query.CommandText = $"SELECT count(*) FROM presets p {filter}";
        var total = Convert.ToInt32(query.ExecuteScalar(), CultureInfo.InvariantCulture);

        query.CommandText = $"SELECT {Columns} FROM presets p {filter} ORDER BY p.submitted_at DESC, p.id DESC LIMIT $size OFFSET $skip";
        query.Parameters.AddWithValue("$size", size);
        query.Parameters.AddWithValue("$skip", Math.Max(0, page - 1) * size);

        var items = new List<StoredPreset>();

        using (var reader = query.ExecuteReader())
            while (reader.Read()) items.Add(Row(reader));

        return new PresetPage(items, total);
    }

    /// <summary>Every preset, oldest first, for finding the ones still to render.</summary>
    public IEnumerable<StoredPreset> Oldest()
    {
        using var db = Open();
        using var query = db.CreateCommand();
        query.CommandText = $"SELECT {Columns} FROM presets p ORDER BY p.submitted_at, p.id";

        using var reader = query.ExecuteReader();

        while (reader.Read()) yield return Row(reader);
    }

    /// <summary>The preset's file, counted as a download where <paramref name="counted"/>.</summary>
    public (StoredPreset Preset, byte[] File)? Download(string id, bool counted)
    {
        using var db = Open();

        if (counted)
        {
            using var count = db.CreateCommand();
            count.CommandText = "UPDATE presets SET downloads = downloads + 1 WHERE id = $id";
            count.Parameters.AddWithValue("$id", id);

            if (count.ExecuteNonQuery() == 0) return null;
        }

        using var query = db.CreateCommand();
        query.CommandText = $"SELECT {Columns}, p.file FROM presets p WHERE p.id = $id";
        query.Parameters.AddWithValue("$id", id);

        using var reader = query.ExecuteReader();

        if (!reader.Read()) return null;

        return (Row(reader), (byte[])reader.GetValue(9));
    }

    public IReadOnlyList<(string Tag, int Count)> Tags(int limit)
    {
        using var db = Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT tag, count(*) AS n FROM preset_tags GROUP BY tag ORDER BY n DESC, tag LIMIT $limit";
        query.Parameters.AddWithValue("$limit", limit);

        var tags = new List<(string, int)>();

        using var reader = query.ExecuteReader();

        while (reader.Read()) tags.Add((reader.GetString(0), reader.GetInt32(1)));

        return tags;
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connection);
        db.Open();
        Run(db, "PRAGMA foreign_keys = ON;");

        return db;
    }

    private static void Run(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Escaped(string word) =>
        word.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static StoredPreset Row(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? [] : [.. reader.GetString(4).Split(Separator).Order(StringComparer.Ordinal)],
        reader.GetString(5),
        reader.GetInt64(6),
        DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
        reader.GetInt64(8));
}
