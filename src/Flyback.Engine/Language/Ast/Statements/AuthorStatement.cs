namespace Flyback.Engine.Language.Ast.Statements;

/// <summary><c>author "..."</c>: who made the patch.</summary>
public sealed record AuthorStatement(string Text, int Line, int Column) : Statement(Line, Column)
{
    public override string Naming => "author";
}
