namespace Flyback.Server;

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
    long Downloads,
    bool Published);