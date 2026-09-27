using Flyback.Core.Language.Ast.Expressions;
using Flyback.Core.Language.Ast;

namespace Flyback.Core.Language.Ast.Statements;

/// <summary><c>name.port = value</c>, which turns a knob.</summary>
public sealed record KnobStatement(NameExpr Target, Expr Value, int Line, int Column)
    : Statement(Line, Column);