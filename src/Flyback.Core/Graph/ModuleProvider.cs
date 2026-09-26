namespace Flyback.Core.Graph;

/// <summary>
/// Who a set of modules came from. Written into saved patches, so a file names
/// what it needs by both id and title — the title matters because a missing
/// plugin cannot be looked up to find out what it was called.
/// </summary>
public sealed record ModuleProvider(string Id, string Name);