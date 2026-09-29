namespace Flyback.App;

/// <summary>
/// What a restart opens when it comes back up: a patch on disk, or a preset the site
/// shared, which has no file of its own to name.
/// </summary>
public sealed record Reopen(string? Path = null, string? Shared = null);