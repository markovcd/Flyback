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
    /// <summary>What was read, kept so a complaint can show the line it is about.</summary>
    public string Source { get; init; } = string.Empty;

    public bool Ok => Issues.Count == 0;

    /// <summary>
    /// Every complaint, each above the line it is about with the column marked.
    /// </summary>
    /// <remarks>
    /// The line is quoted rather than only numbered, and it earns the space. One
    /// mistake stops a statement being read, so every name that statement was
    /// going to make is then missing too — a single stray comma comes back as
    /// four complaints, three of them about names that were never the problem.
    /// Whoever is reading this, person or model, has to be able to see which one
    /// is the cause, and a line number alone does not show that.
    /// </remarks>
    public string Report
    {
        get
        {
            if (Issues.Count == 0) return string.Empty;
            if (Source.Length == 0) return string.Join(Environment.NewLine, Issues);

            var lines = Source.ReplaceLineEndings("\n").Split('\n');
            var text = new System.Text.StringBuilder();

            foreach (var issue in Issues)
            {
                text.Append(issue.Line).Append(':').Append(issue.Column).Append(": ")
                    .AppendLine(issue.Message);

                if (issue.Line < 1 || issue.Line > lines.Length) continue;

                var line = lines[issue.Line - 1];

                text.Append("    ").AppendLine(line);
                text.Append("    ").Append(new string(' ', Math.Clamp(issue.Column - 1, 0, line.Length)))
                    .AppendLine("^");
            }

            return text.ToString().TrimEnd();
        }
    }
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

        return new LanguageLoad(patch, [.. issues.OrderBy(i => i.Line).ThenBy(i => i.Column)])
        {
            Source = source,
        };
    }
}
