namespace Flyback.Engine.Language.Ast;

/// <summary>One way an <c>if</c> can go: statements, then what it is worth.</summary>
/// <param name="Result">What the branch is worth, or null where it ends in nothing.</param>
/// <param name="Results">What the branch is worth where it ends in several things.</param>
public sealed record Branch(IReadOnlyList<Statement> Body, Expr? Result, IReadOnlyList<Expr>? Results);
