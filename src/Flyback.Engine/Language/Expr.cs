namespace Flyback.Core.Language;

/// <summary>Anything that stands for a signal or a number.</summary>
public abstract record Expr(int Line, int Column);