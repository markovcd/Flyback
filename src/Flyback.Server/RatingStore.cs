using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Flyback.Server;

/// <summary>The stars people gave shared presets and plugins, one rating per address, beside them in the one SQLite file.</summary>
/// <remarks>An address is kept only as an HMAC under a key the store makes once, so the table says nothing about who rated.</remarks>
internal sealed class RatingStore
{
    public const int Most = 5;

    private readonly string connection;
    private readonly byte[] key;

    public RatingStore(string path)
    {
        connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();

        using var db = Open();
        using var create = db.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS ratings (
                kind TEXT NOT NULL,
                subject_id TEXT NOT NULL,
                voter TEXT NOT NULL,
                stars INTEGER NOT NULL,
                rated_at TEXT NOT NULL,
                PRIMARY KEY (kind, subject_id, voter)
            );
            CREATE TABLE IF NOT EXISTS rating_key (key BLOB NOT NULL);
            """;
        create.ExecuteNonQuery();

        using var made = db.CreateCommand();
        made.CommandText = "INSERT INTO rating_key (key) SELECT $key WHERE NOT EXISTS (SELECT 1 FROM rating_key)";
        made.Parameters.AddWithValue("$key", RandomNumberGenerator.GetBytes(32));
        made.ExecuteNonQuery();

        using var read = db.CreateCommand();
        read.CommandText = "SELECT key FROM rating_key LIMIT 1";
        key = (byte[])read.ExecuteScalar()!;
    }

    /// <summary>Who is rating, as the table keeps them.</summary>
    public string Voter(IPAddress? address) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(Visitor.Of(address))));

    /// <summary>Gives the stars, replacing whatever the voter gave before.</summary>
    public void Rate(string kind, string subject, string voter, int stars, DateTimeOffset at)
    {
        using var db = Open();
        using var upsert = db.CreateCommand();
        upsert.CommandText = """
            INSERT INTO ratings (kind, subject_id, voter, stars, rated_at) VALUES ($kind, $subject, $voter, $stars, $at)
            ON CONFLICT (kind, subject_id, voter) DO UPDATE SET stars = excluded.stars, rated_at = excluded.rated_at
            """;
        upsert.Parameters.AddWithValue("$kind", kind);
        upsert.Parameters.AddWithValue("$subject", subject);
        upsert.Parameters.AddWithValue("$voter", voter);
        upsert.Parameters.AddWithValue("$stars", stars);
        upsert.Parameters.AddWithValue("$at", at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        upsert.ExecuteNonQuery();
    }

    public Rating Of(string kind, string subject) =>
        Of(kind, [subject]).GetValueOrDefault(subject, Rating.None);

    /// <summary>The rating of each of <paramref name="subjects"/> that has one.</summary>
    public IReadOnlyDictionary<string, Rating> Of(string kind, IEnumerable<string> subjects)
    {
        var ids = subjects.Distinct(StringComparer.Ordinal).ToList();
        var found = new Dictionary<string, Rating>(StringComparer.Ordinal);

        if (ids.Count == 0) return found;

        using var db = Open();
        using var query = db.CreateCommand();
        query.CommandText = $"""
            SELECT subject_id, AVG(stars), COUNT(*) FROM ratings
            WHERE kind = $kind AND subject_id IN ({string.Join(", ", ids.Select((_, i) => "$s" + i))})
            GROUP BY subject_id
            """;
        query.Parameters.AddWithValue("$kind", kind);

        for (var i = 0; i < ids.Count; i++) query.Parameters.AddWithValue("$s" + i, ids[i]);

        using var reader = query.ExecuteReader();

        while (reader.Read())
            found[reader.GetString(0)] = new Rating(Math.Round(reader.GetDouble(1), 2), reader.GetInt32(2));

        return found;
    }

    /// <summary>The stars the voter gave, or null where they have not rated it.</summary>
    public int? Mine(string kind, string subject, string voter)
    {
        using var db = Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT stars FROM ratings WHERE kind = $kind AND subject_id = $subject AND voter = $voter";
        query.Parameters.AddWithValue("$kind", kind);
        query.Parameters.AddWithValue("$subject", subject);
        query.Parameters.AddWithValue("$voter", voter);

        return query.ExecuteScalar() is long stars ? (int)stars : null;
    }

    /// <summary>Removes every rating of the preset or plugin, once it is deleted.</summary>
    public void Forget(string kind, string subject)
    {
        using var db = Open();
        using var delete = db.CreateCommand();
        delete.CommandText = "DELETE FROM ratings WHERE kind = $kind AND subject_id = $subject";
        delete.Parameters.AddWithValue("$kind", kind);
        delete.Parameters.AddWithValue("$subject", subject);
        delete.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connection);
        db.Open();

        return db;
    }
}
