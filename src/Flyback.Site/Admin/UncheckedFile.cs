namespace Flyback.Site;

/// <summary>A submission still to be checked: its id, the name it was sent under for a preset, and its file's name.</summary>
internal sealed record UncheckedFile(string Id, string? Name, string FileName);
