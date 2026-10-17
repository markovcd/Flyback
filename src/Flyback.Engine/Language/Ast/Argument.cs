namespace Flyback.Engine.Language.Ast;

/// <summary>One argument to a call, named or not.</summary>
/// <param name="Name">The socket this is for, or null to take the next free one.</param>
/// <param name="Spread">Whether it was written <c>...list</c>, so each item takes a socket of its own.</param>
public sealed record Argument(string? Name, Expr Value, int Line, int Column, bool Spread = false);
