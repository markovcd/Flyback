namespace Flyback.Engine.Language;

/// <summary>One output of a pack's module: its socket's name and what it gives.</summary>
/// <param name="Help">Its help line, or empty where the pack wrote none.</param>
public sealed record PackOutput(string Name, string Help = "");
