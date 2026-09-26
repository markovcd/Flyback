namespace Flyback.Core.Language;

/// <summary>
/// A signal flowing into a module. The whole of the language's shape, and the
/// only place <see cref="Binder"/> applies the pipe rule.
/// </summary>
public sealed record PipeExpr(Expr Source, Expr Stage, int Line, int Column) : Expr(Line, Column);