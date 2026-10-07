namespace Flyback.Engine.Language.Values;

/// <summary>A number, which becomes a knob rather than a module.</summary>
/// <param name="Where">
/// Where the file writes it, so the knob can be changed where the text
/// already says it. Null for a number no single figure stands for:
/// <c>1/12</c> is one knob and two numbers.
/// </param>
internal sealed record Figure(double Amount, NumberStyle Style, Site? Where = null) : Value;
