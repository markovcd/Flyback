using System.Text.RegularExpressions;
using Flyback.Engine.Language.Ast.Statements;

namespace Flyback.Engine.Language;

/// <summary>
/// The lines that open a module pack, <c>pack</c> and <c>language</c>, each said once
/// and before anything else.
/// </summary>
internal sealed partial class PackLines(Issues issues)
{
    private PackStatement? pack;
    private LanguageStatement? language;

    /// <summary>The pack the file says it is, or null where it says none or said it wrongly.</summary>
    public PackHeader? Header => pack is null ? null : new PackHeader(pack.Id, pack.Version, (int)(language?.Version ?? PatchLanguage.Version));

    /// <summary>Reads a header line from the top of the file.</summary>
    public void Read(PackStatement statement)
    {
        if (pack is not null)
        {
            issues.Complain(IssueCode.SaidTwice, statement.Line, statement.Column,
                "the pack is already named further up. A file is one pack, so it says so once.");
            return;
        }

        if (!Id().IsMatch(statement.Id))
        {
            issues.Complain(IssueCode.BadPackId, statement.Line, statement.Column,
                $"'{statement.Id}' is not a pack's id. It is two or more lower-case words joined by points, as in 'pack ada.echoes 1.0.0'.");
            return;
        }

        pack = statement;
    }

    /// <inheritdoc cref="Read(PackStatement)"/>
    public void Read(LanguageStatement statement)
    {
        if (language is not null)
        {
            issues.Complain(IssueCode.SaidTwice, statement.Line, statement.Column,
                "the text's version is already said further up, so it is said once.");
            return;
        }

        if (statement.Version < 1 || statement.Version != Math.Floor(statement.Version))
        {
            issues.Complain(IssueCode.BadLanguage, statement.Line, statement.Column,
                $"'{statement.Version}' is not a version of the text. It is a whole number, as in 'language {PatchLanguage.Version}'.");
            return;
        }

        if (statement.Version > PatchLanguage.Version)
        {
            issues.Complain(IssueCode.NewerLanguage, statement.Line, statement.Column,
                $"this was written for version {statement.Version} of the text, and this Flyback reads up to {PatchLanguage.Version}. Update Flyback to open it.");
            return;
        }

        language = statement;
    }

    /// <summary>A header line anywhere but the top of the file, refused.</summary>
    public void Misplaced(Ast.Statement statement) =>
        issues.Complain(IssueCode.HeaderNotFirst, statement.Line, statement.Column,
            $"'{statement.Naming}' opens the file: it comes before every other line, outside every group, def, loop and if.");

    [GeneratedRegex("^[a-z][a-z0-9]*(\\.[a-z][a-z0-9]*)+$")]
    private static partial Regex Id();
}
