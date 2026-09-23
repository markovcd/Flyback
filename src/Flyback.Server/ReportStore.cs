using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Flyback.Server;

/// <summary>A report that somebody made about a shared preset or plugin, and the name it had then.</summary>
/// <param name="Kind">Whether it is about a preset or a plugin.</param>
/// <param name="Subject">The id of the preset or plugin it is about.</param>
/// <param name="Name">What the preset or plugin is called now, or null where it is gone.</param>
internal sealed record StoredReport(
    string Id,
    string Kind,
    string Subject,
    string? Name,
    string Reason,
    string? Details,
    DateTimeOffset Submitted);

/// <summary>What people have reported about shared presets and plugins, beside them in the one SQLite file.</summary>
internal sealed class ReportStore
{
    public const string Preset = "preset";
    public const string Plugin = "plugin";

    /// <summary>Why a report may be made, as the site and the editor offer them.</summary>
    public static readonly IReadOnlySet<string> Reasons = new HashSet<string>(StringComparer.Ordinal)
    {
        "broken", "harmful", "offensive", "stolen", "other",
    };

    public const int DetailsLimit = 1000;

    private readonly string connection;

    public ReportStore(string path)
    {
        connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();

        using var db = Open();
        using var create = db.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS reports (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                subject_id TEXT NOT NULL,
                reason TEXT NOT NULL,
                details TEXT,
                submitted_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS reports_subject ON reports(kind, subject_id);
            """;
        create.ExecuteNonQuery();
    }

    public void Add(string kind, string subject, string reason, string? details, DateTimeOffset at)
    {
        using var db = Open();
        using var insert = db.CreateCommand();
        insert.CommandText = """
            INSERT INTO reports (id, kind, subject_id, reason, details, submitted_at)
            VALUES ($id, $kind, $subject, $reason, $details, $at)
            """;
        insert.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString("N"));
        insert.Parameters.AddWithValue("$kind", kind);
        insert.Parameters.AddWithValue("$subject", subject);
        insert.Parameters.AddWithValue("$reason", reason);
        insert.Parameters.AddWithValue("$details", (object?)details ?? DBNull.Value);
        insert.Parameters.AddWithValue("$at", at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        insert.ExecuteNonQuery();
    }

    /// <summary>Every report, newest first, with the name of what it is about.</summary>
    public IReadOnlyList<StoredReport> List()
    {
        using var db = Open();
        using var query = db.CreateCommand();
        query.CommandText = $"""
            SELECT r.id, r.kind, r.subject_id,
                CASE r.kind
                    WHEN '{Preset}' THEN (SELECT p.name FROM presets p WHERE p.id = r.subject_id)
                    ELSE (SELECT p.name FROM plugins p WHERE p.id = r.subject_id)
                END,
                r.reason, r.details, r.submitted_at
            FROM reports r ORDER BY r.submitted_at DESC, r.id DESC
            """;

        var reports = new List<StoredReport>();

        using var reader = query.ExecuteReader();

        while (reader.Read())
            reports.Add(new StoredReport(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)));

        return reports;
    }

    /// <summary>Removes the report, false where there is no such report.</summary>
    public bool Dismiss(string id)
    {
        using var db = Open();
        using var delete = db.CreateCommand();
        delete.CommandText = "DELETE FROM reports WHERE id = $id";
        delete.Parameters.AddWithValue("$id", id);

        return delete.ExecuteNonQuery() > 0;
    }

    /// <summary>Removes every report about the preset or plugin, once it is deleted.</summary>
    public void Forget(string kind, string subject)
    {
        using var db = Open();
        using var delete = db.CreateCommand();
        delete.CommandText = "DELETE FROM reports WHERE kind = $kind AND subject_id = $subject";
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

/// <summary>What a report says, as it is posted.</summary>
internal sealed record ReportForm(string? Reason, string? Details);

/// <summary>The report endpoints: anyone may report, only the admin reads and dismisses.</summary>
internal static class ReportApi
{
    /// <summary>Takes a report about what <paramref name="exists"/> says is there to see.</summary>
    public static RouteHandlerBuilder MapReport(this RouteGroupBuilder api, string route, string kind, ReportStore reports, Func<string, bool> exists) =>
        api.MapPost(route, (string id, ReportForm form) =>
        {
            if (form.Reason is not { } reason || !ReportStore.Reasons.Contains(reason))
                return Results.BadRequest(new { Error = $"A reason is one of {string.Join(", ", ReportStore.Reasons)}." });

            var details = form.Details?.Trim();

            if (details?.Length > ReportStore.DetailsLimit)
                return Results.BadRequest(new { Error = $"Say it in {ReportStore.DetailsLimit} characters or fewer." });

            if (!exists(id)) return Results.NotFound();

            reports.Add(kind, id, reason, string.IsNullOrEmpty(details) ? null : details, DateTimeOffset.UtcNow);

            return Results.NoContent();
        })
        .DisableAntiforgery()
        .RequireRateLimiting("report");

    public static void MapReports(this RouteGroupBuilder api, ReportStore reports, Func<HttpContext, bool> reviewing)
    {
        api.MapGet("/reports", (HttpContext http) =>
            reviewing(http) ? Results.Ok(reports.List()) : Results.Unauthorized());

        api.MapDelete("/reports/{id}", (HttpContext http, string id) =>
            !reviewing(http) ? Results.Unauthorized()
            : reports.Dismiss(id) ? Results.NoContent()
            : Results.NotFound());
    }
}
