namespace Flyback.Server;

/// <summary>A letter somebody wrote to the author from inside Flyback, and what their copy was.</summary>
/// <param name="Mood">Whether it is praise, a problem, an idea or something else.</param>
/// <param name="Contact">How they may be written back to, or null where they left it blank.</param>
/// <param name="Version">The build it was written from, as the About window names it.</param>
/// <param name="Platform">The operating system it was written on.</param>
/// <param name="Plugins">Which plugins loaded and which sound backend opened.</param>
internal sealed record StoredLetter(
    string Id,
    string Mood,
    string Message,
    string? Contact,
    string? Version,
    string? Platform,
    string? Plugins,
    DateTimeOffset Submitted);