namespace Flyback.Cli.Models;

/// <summary>What a module carries that is neither a socket nor a knob.</summary>
/// <param name="Help">What each of its fields is for, or the extra itself where it has none.</param>
internal sealed record Carried(string Key, string Says, IReadOnlyDictionary<string, string> Help);
