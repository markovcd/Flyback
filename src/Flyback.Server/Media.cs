namespace Flyback.Server;

/// <summary>What <c>flyback-cli render-presets</c> has made of a preset so far.</summary>
internal sealed record Media(string? Still, string? Loop, string? Audio, IReadOnlyList<double>? Peaks, string State);