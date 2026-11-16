namespace Flyback.Engine.Language.Ast;

/// <summary>
/// A fold's <c>from seed with names</c>: what the first pass starts with, and the name, or
/// bracketed names, each pass reads what the pass before ended with by.
/// </summary>
public sealed record Carry(IReadOnlyList<string> Names, bool Tupled, Expr Seed);
