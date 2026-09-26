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