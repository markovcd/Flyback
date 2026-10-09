using Flyback.Engine.Language.Ast.Expressions;

namespace Flyback.Engine.Language.Ast.Statements;

/// <summary>A pipeline standing on its own, which is the only statement with an effect.</summary>
public sealed record PipelineStatement(Expr Value, int Line, int Column) : Statement(Line, Column)
{
    /// <summary>
    /// The socket the pipeline ends at, where it ends at one: <c>out.color</c>
    /// and <c>out.left</c> stay two different statements however the lines
    /// around them are shuffled.
    /// </summary>
    public override string? Naming => Value is PipeExpr { Stage: NameExpr socket } ? socket.Written : null;
}
