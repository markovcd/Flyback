using Flyback.Engine.Language.Ast.Expressions;

namespace Flyback.Engine.Language.Ast.Statements;

/// <summary><c>name.port = value</c>, which turns a knob.</summary>
public sealed record KnobStatement(NameExpr Target, Expr Value, int Line, int Column)
    : Statement(Line, Column);