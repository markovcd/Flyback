using Flyback.Core.Language.Ast.Expressions;
using Flyback.Core.Language.Ast;

namespace Flyback.Core.Language.Ast.Statements;

/// <summary><c>let (a, b, c) = call</c>, which takes a def's several results apart.</summary>
public sealed record LetTupleStatement(
    IReadOnlyList<string> Names,
    Expr Value,
    int Line,
    int Column) : Statement(Line, Column);