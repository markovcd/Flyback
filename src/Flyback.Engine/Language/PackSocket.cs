namespace Flyback.Engine.Language;

/// <summary>One socket of a pack's module: a parameter written with a number for its default.</summary>
/// <param name="Default">What it holds with no wire.</param>
/// <param name="Min">The low end of its range, from <c>in lo..hi</c>, or null where none was written.</param>
/// <param name="Max">The high end of its range, or null where none was written.</param>
public sealed record PackSocket(string Name, float Default, float? Min = null, float? Max = null);
