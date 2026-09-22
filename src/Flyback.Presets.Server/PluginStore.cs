using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Flyback.Presets.Server;

/// <summary>A stored plugin package, without its file.</summary>
internal sealed record StoredPlugin(
    string Id,
    string Assembly,
    string Name,
    string Version,
    string Author,
    string Description,
    IReadOnlyList<string> Adds,
    IReadOnlyList<string> Reaches,
    IReadOnlyList<string> Builds,
    IReadOnlyDictionary<string, string> Contract,
    string Sha256,
    string FileName,
    long Size,
    DateTimeOffset Submitted,
    long Downloads,
    bool Published);

/// <summary>A page of plugins and how many match in all.</summary>
internal sealed record PluginPage(IReadOnlyList<StoredPlugin> Items, int Total);

/// <summary>
/// The plugin packages people have submitted, beside the presets in the same SQLite file.
/// </summary>
/// <remarks>
/// A package arrives unpublished and is seen by nobody until a reviewer publishes it.
/// Every read here takes <c>unpublished</c> for the reviewer, and leaves such packages
/// out without it.
/// </remarks>
internal sealed class PluginStore
{
    private const char Separator = '';

    private const string Columns = """
        p.id, p.assembly, p.name, p.version, p.author, p.description, p.adds, p.reaches, p.builds, p.contract,
        p.sha256, p.file_name, p.size, p.submitted_at, p.downloads, p.published
        """;

    private readonly string connection;

    public PluginStore(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder) Directory.CreateDirectory(folder);

        connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();

        using var db = Open();
        Run(db, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS plugins (
                id TEXT PRIMARY KEY,
                assembly TEXT NOT NULL,
                name TEXT NOT NULL,
                version TEXT NOT NULL,
                author TEXT NOT NULL,
                description TEXT NOT NULL,
                adds TEXT NOT NULL,
                reaches TEXT NOT NULL,
                builds TEXT NOT NULL,
                contract TEXT NOT NULL,
                sha256 TEXT NOT NULL UNIQUE,
                file_name TEXT NOT NULL,
                file BLOB NOT NULL,
                size INTEGER NOT NULL,
                submitted_at TEXT NOT NULL,
                downloads INTEGER NOT NULL DEFAULT 0,
                published INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS plugins_submitted ON plugins(submitted_at);
            """);
    }

    /// <summary>Stores the package unpublished, or returns null where the same package is already here.</summary>
    public StoredPlugin? Add(PluginSubmission submission, DateTimeOffset at)
    {
        var id = Guid.CreateVersion7().ToString("N");

        using var db = Open();
        using var insert = db.CreateCommand();
        insert.CommandText = """
            INSERT OR IGNORE INTO plugins
                (id, assembly, name, version, author, description, adds, reaches, builds, contract, sha256, file_name, file, size, submitted_at)
            VALUES ($id, $assembly, $name, $version, $author, $description, $adds, $reaches, $builds, $contract, $sha256, $file_name, $file, $size, $at)
            """;
        insert.Parameters.AddWithValue("$id", id);
        insert.Parameters.AddWithValue("$assembly", submission.Assembly);
        insert.Parameters.AddWithValue("$name", submission.Name);
        insert.Parameters.AddWithValue("$version", submission.Version);
        insert.Parameters.AddWithValue("$author", submission.Author);
        insert.Parameters.AddWithValue("$description", submission.Description);
        insert.Parameters.AddWithValue("$adds", Joined(submission.Adds));
        insert.Parameters.AddWithValue("$reaches", Joined(submission.Reaches));
        insert.Parameters.AddWithValue("$builds", Joined(submission.Builds));
        insert.Parameters.AddWithValue("$contract", Joined(submission.Contract.Select(c => c.Key + " " + c.Value)));
        insert.Parameters.AddWithValue("$sha256", submission.Sha256);
        insert.Parameters.AddWithValue("$file_name", submission.FileName);
        insert.Parameters.AddWithValue("$file", submission.File);
        insert.Parameters.AddWithValue("$size", submission.File.LongLength);
        insert.Parameters.AddWithValue("$at", at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

        return insert.ExecuteNonQuery() == 0 ? null : Find(id, unpublished: true);
    }

    public StoredPlugin? Find(string id, bool unpublished = false)
    {
        using var db = Open();
        using var query = db.CreateCommand();
        query.CommandText = $"SELECT {Columns} FROM plugins p WHERE p.id = $id {Visible(unpublished)}";
        query.Parameters.AddWithValue("$id", id);

        using var reader = query.ExecuteReader();

        return reader.Read() ? Row(reader) : null;
    }

    /// <summary>
    /// Newest first, matching all of <paramref name="search"/>'s words, and only those
    /// with a build that would install on <paramref name="platform"/> where it is given.
    /// </summary>
    public PluginPage List(string? search, string? platform, int page, int size, bool unpublished = false)
    {
        var where = new List<string>();

        if (!unpublished) where.Add("p.published = 1");

        using var db = Open();
        using var query = db.CreateCommand();

        var words = (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < words.Length && i < 8; i++)
        {
            where.Add($"(p.name LIKE $w{i} ESCAPE '\\' OR p.author LIKE $w{i} ESCAPE '\\' OR p.description LIKE $w{i} ESCAPE '\\' OR p.assembly LIKE $w{i} ESCAPE '\\')");
            query.Parameters.AddWithValue($"$w{i}", "%" + Escaped(words[i]) + "%");
        }

        if (!string.IsNullOrEmpty(platform))
        {
            where.Add("(instr(char(31) || p.builds || char(31), char(31) || $platform || char(31)) > 0 OR instr(char(31) || p.builds || char(31), char(31) || 'any' || char(31)) > 0)");
            query.Parameters.AddWithValue("$platform", platform);
        }

        var filter = where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where);

        query.CommandText = $"SELECT count(*) FROM plugins p {filter}";
        var total = Convert.ToInt32(query.ExecuteScalar(), CultureInfo.InvariantCulture);

        query.CommandText = $"SELECT {Columns} FROM plugins p {filter} ORDER BY p.submitted_at DESC, p.id DESC LIMIT $size OFFSET $skip";
        query.Parameters.AddWithValue("$size", size);
        query.Parameters.AddWithValue("$skip", Math.Max(0, page - 1) * size);

        var items = new List<StoredPlugin>();

        using (var reader = query.ExecuteReader())
            while (reader.Read()) items.Add(Row(reader));

        return new PluginPage(items, total);
    }

    /// <summary>The package, counted as a download where <paramref name="counted"/>.</summary>
    public (StoredPlugin Plugin, byte[] File)? Download(string id, bool counted, bool unpublished = false)
    {
        using var db = Open();
        using var query = db.CreateCommand();
        query.CommandText = $"SELECT {Columns}, p.file FROM plugins p WHERE p.id = $id {Visible(unpublished)}";
        query.Parameters.AddWithValue("$id", id);

        (StoredPlugin Plugin, byte[] File) found;

        using (var reader = query.ExecuteReader())
        {
            if (!reader.Read()) return null;

            found = (Row(reader), (byte[])reader.GetValue(16));
        }

        if (counted)
        {
            using var count = db.CreateCommand();
            count.CommandText = "UPDATE plugins SET downloads = downloads + 1 WHERE id = $id";
            count.Parameters.AddWithValue("$id", id);
            count.ExecuteNonQuery();
        }

        return found;
    }

    /// <summary>Shows or hides the plugin, false where there is no such plugin.</summary>
    public bool Publish(string id, bool published) =>
        Change("UPDATE plugins SET published = $value WHERE id = $id", id, published ? 1 : 0);

    /// <summary>Deletes the plugin, false where there is no such plugin.</summary>
    public bool Delete(string id) =>
        Change("DELETE FROM plugins WHERE id = $id", id, null);

    private bool Change(string sql, string id, object? value)
    {
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        if (value is not null) command.Parameters.AddWithValue("$value", value);

        return command.ExecuteNonQuery() > 0;
    }

    private static string Visible(bool unpublished) => unpublished ? string.Empty : "AND p.published = 1";

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connection);
        db.Open();

        return db;
    }

    private static void Run(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Joined(IEnumerable<string> values) => string.Join(Separator, values);

    private static string[] Split(string joined) =>
        joined.Length == 0 ? [] : joined.Split(Separator);

    private static string Escaped(string word) =>
        word.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static StoredPlugin Row(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        Split(reader.GetString(6)),
        Split(reader.GetString(7)),
        Split(reader.GetString(8)),
        Split(reader.GetString(9)).Select(c => c.Split(' ', 2)).ToDictionary(c => c[0], c => c.Length > 1 ? c[1] : "", StringComparer.Ordinal),
        reader.GetString(10),
        reader.GetString(11),
        reader.GetInt64(12),
        DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
        reader.GetInt64(14),
        reader.GetInt64(15) != 0);
}
