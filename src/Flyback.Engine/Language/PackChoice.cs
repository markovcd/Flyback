namespace Flyback.Engine.Language;

/// <summary>
/// One setting of a pack's module: a parameter written <c>in [a, b, c]</c>, which a patch picks
/// once rather than wires, since it decides which modules the body is built from.
/// </summary>
/// <param name="Options">Each choice as the text writes it, without quotes.</param>
/// <param name="Default">The choice a module placed fresh holds.</param>
/// <param name="Texts">Whether the choices are texts, quoted when the body is built, rather than numbers.</param>
public sealed record PackChoice(string Name, IReadOnlyList<string> Options, string Default, bool Texts);
