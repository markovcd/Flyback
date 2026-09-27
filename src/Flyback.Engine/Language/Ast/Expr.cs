namespace Flyback.Core.Language.Ast;

/// <summary>Anything that stands for a signal or a number.</summary>
public abstract record Expr(int Line, int Column);