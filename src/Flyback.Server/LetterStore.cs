using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Flyback.Server;

/// <summary>The letters people have sent, beside the presets in the one SQLite file.</summary>
internal sealed class LetterStore
{
    /// <summary>What a letter may be about, as the editor offers them.</summary>
    public static readonly IReadOnlySet<string> Moods = new HashSet<string>(StringComparer.Ordinal)
    {
        "good", "bad", "idea", "other",
    };

    public const int MessageLimit = 2000;

    public const int ContactLimit = 200;

    /// <summary>What the editor may say about itself, per field, before it is cut short.</summary>
    public const int AboutLimit = 600;

    private readonly string connection;

    public LetterStore(string path)
    {
        connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();

        using var db = Open();
        using var create = db.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS letters (
                id TEXT PRIMARY KEY,
                mood TEXT NOT NULL,
                message TEXT NOT NULL,
                contact TEXT,
                version TEXT,
                platform TEXT,
                plugins TEXT,
                submitted_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS letters_submitted ON letters(submitted_at);
            """;
        create.ExecuteNonQuery();
    }

    public void Add(string mood, string message, string? contact, string? version, string? platform, string? plugins, DateTimeOffset at)
    {
        using var db = Open();
        using var insert = db.CreateCommand();
        insert.CommandText = """
            INSERT INTO letters (id, mood, message, contact, version, platform, plugins, submitted_at)
            VALUES ($id, $mood, $message, $contact, $version, $platform, $plugins, $at)
            """;
        insert.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString("N"));
        insert.Parameters.AddWithValue("$mood", mood);
        insert.Parameters.AddWithValue("$message", message);
        insert.Parameters.AddWithValue("$contact", (object?)contact ?? DBNull.Value);
        insert.Parameters.AddWithValue("$version", (object?)version ?? DBNull.Value);
        insert.Parameters.AddWithValue("$platform", (object?)platform ?? DBNull.Value);
        insert.Parameters.AddWithValue("$plugins", (object?)plugins ?? DBNull.Value);
        insert.Parameters.AddWithValue("$at", at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        insert.ExecuteNonQuery();
    }

    /// <summary>Every letter, newest first.</summary>
    public IReadOnlyList<StoredLetter> List()
    {
        using var db = Open();
        using var query = db.CreateCommand();
        query.CommandText = """
            SELECT id, mood, message, contact, version, platform, plugins, submitted_at
            FROM letters ORDER BY submitted_at DESC, id DESC
            """;

        var letters = new List<StoredLetter>();

        using var reader = query.ExecuteReader();

        while (reader.Read())
            letters.Add(new StoredLetter(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)));

        return letters;
    }

    /// <summary>Removes the letter, false where there is no such letter.</summary>
    public bool Dismiss(string id)
    {
        using var db = Open();
        using var delete = db.CreateCommand();
        delete.CommandText = "DELETE FROM letters WHERE id = $id";
        delete.Parameters.AddWithValue("$id", id);

        return delete.ExecuteNonQuery() > 0;
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connection);
        db.Open();

        return db;
    }
}
