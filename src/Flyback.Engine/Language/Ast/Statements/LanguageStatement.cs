namespace Flyback.Engine.Language.Ast.Statements;

/// <summary><c>language 1</c>: the version of the text a file was written for.</summary>
public sealed record LanguageStatement(double Version, int Line, int Column) : Statement(Line, Column)
{
    public override string Naming => "language";
}
