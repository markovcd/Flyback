namespace Flyback.Core.Language;

/// <summary>
/// One output taken off what an expression places:
/// <c>tempo(bpm: 104).beats</c>. The selection <see cref="NameExpr"/> makes on a
/// binding, for a module that has no name to make it on.
/// </summary>
/// <param name="Line">Where the output's name is, which is what a complaint is about.</param>
/// <param name="Column">Where the output's name is, which is what a complaint is about.</param>
public sealed record SelectExpr(Expr Source, string Port, int Line, int Column) : Expr(Line, Column);