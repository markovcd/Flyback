namespace Flyback.Engine.Language.Ast.Statements;

/// <summary><c>pack flyback.raymarch 1.0.0</c>: the module pack a file is, by its id and its version.</summary>
public sealed record PackStatement(string Id, string Version, int Line, int Column) : Statement(Line, Column)
{
    public override string Naming => "pack";
}
