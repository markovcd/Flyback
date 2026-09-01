using Flyback.Core.Graph;

namespace Flyback.Core.Language;

/// <summary>
/// A patch read from text, and everything wrong with the text it came from.
/// </summary>
/// <remarks>
/// Both halves are handed back, in the shape <see cref="PatchLoad"/> already
/// uses: a file with a mistake in it still describes most of a patch, and which
/// of those two facts matters is the caller's to decide. A batch job refuses;
/// an editor shows the complaints beside what it managed to build.
/// </remarks>
public sealed record LanguageLoad(Patch Patch, IReadOnlyList<LanguageIssue> Issues)
{
    public bool Ok => Issues.Count == 0;

    /// <summary>Every complaint on its own line, for a console or a panel.</summary>
    public string Report => string.Join(Environment.NewLine, Issues);
}

/// <summary>
/// The text language, which parses to a patch and to nothing else — see
/// [0065](../../../docs/adr/0065-a-text-language-that-parses-to-a-patch.md) and
/// the reference beside it.
/// </summary>
/// <remarks>
/// There is no interpreter here and no second engine. What comes out is the
/// same <see cref="Patch"/> the editor builds and <see cref="PatchIO"/> writes,
/// so everything downstream — the compiler, both sinks, the GLSL backend, the
/// bundler — is reached without knowing this exists.
/// </remarks>
public static class PatchLanguage
{
    /// <summary>The extension a source file takes, beside .fbk for the patch it builds into.</summary>
    public const string FileExtension = "fbks";

    /// <summary>
    /// The patch <paramref name="source"/> describes, against
    /// <paramref name="against"/> or the installed catalogue.
    /// </summary>
    /// <remarks>
    /// Never throws. Every way a source file can be wrong is a
    /// <see cref="LanguageIssue"/> with a line and a column on it, because the
    /// thing reading this is usually an editor and a stack trace is no use to
    /// one.
    /// </remarks>
    public static LanguageLoad Build(string source, ModuleCatalog? against = null)
    {
        var modules = against ?? NodeCatalog.Current;
        var issues = new List<LanguageIssue>();

        var tokens = Lexer.Statements(Lexer.Scan(source, issues));
        var statements = new Parser(tokens, issues).Parse();
        var patch = new Binder(modules, issues).Build(statements);

        return new LanguageLoad(patch, [.. issues.OrderBy(i => i.Line).ThenBy(i => i.Column)]);
    }
}
