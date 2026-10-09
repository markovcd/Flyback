namespace Flyback.Core.Graph;

/// <summary>
/// A color a plugin hands over, Core knowing no toolkit to name one with.
/// </summary>
public readonly record struct Swatch(byte Red, byte Green, byte Blue);
