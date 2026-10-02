namespace Flyback.Engine.Language.Ast;

/// <summary>One parameter of a <c>def</c>, with what a call that leaves it out gets.</summary>
/// <param name="Default">A number, a note, a duration or a text, or null where every call must say.</param>
public sealed record Parameter(string Name, Expr? Default, int Line, int Column);
