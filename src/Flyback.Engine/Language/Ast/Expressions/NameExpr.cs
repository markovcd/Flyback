namespace Flyback.Engine.Language.Ast.Expressions;

/// <summary>
/// A name, and optionally one of its outputs: <c>riff</c>, <c>riff.gate</c>,
/// <c>out.color</c>.
/// </summary>
public sealed record NameExpr(string Name, string? Port, int Line, int Column) : Expr(Line, Column)
{
    /// <summary>The name as the text writes it, with its socket where it has one.</summary>
    public string Written => Port is null ? Name : Name + "." + Port;

    public override bool Placeholder => Name == "_" && Port is null;
}
