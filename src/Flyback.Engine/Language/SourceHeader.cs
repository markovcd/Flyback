using Flyback.Core.Graph;
using Flyback.Engine.Language.Ast.Statements;

namespace Flyback.Engine.Language;

/// <summary>
/// What a source file says of itself, read without a catalog: its description and
/// its tags, tidied as the patch it builds would hold them.
/// </summary>
internal sealed record SourceHeader(string Description, IReadOnlyList<string>? Tags)
{
    /// <summary>The header of <paramref name="source"/>; empty where it says nothing.</summary>
    public static SourceHeader Read(string source)
    {
        var issues = new List<LanguageIssue>();
        var statements = new Parser(StatementTokens.ForParsing(Lexer.Scan(source, issues)), issues).Parse();

        var description = statements.OfType<DescriptionStatement>().FirstOrDefault()?.Text;
        var tags = statements.OfType<TagsStatement>().FirstOrDefault()?.Tags;

        return new SourceHeader(Patch.Tidied(description) ?? string.Empty, Patch.TidiedTags(tags));
    }
}
