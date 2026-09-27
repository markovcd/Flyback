namespace Flyback.Cli.Models;

/// <summary>Where one sink of two patches first parted, or null where it never did.</summary>
/// <param name="Seconds">When they parted.</param>
/// <param name="Most">The largest difference: a sample's value, or a byte of a pixel.</param>
/// <param name="Count">How many samples or frames differ.</param>
/// <param name="Channel">Which channel the sound parted in.</param>
/// <param name="Frame">Which frame the picture parted in.</param>
internal sealed record Parting(double Seconds, double Most, long Count, string? Channel = null, int? Frame = null);