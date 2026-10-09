namespace Flyback.Site.Reading;

/// <summary>A submitted preset file, with what it says about itself.</summary>
internal sealed record Submission(
    string Name,
    string? Author,
    string? Description,
    IReadOnlyList<string> Tags,
    string FileName,
    byte[] File);
