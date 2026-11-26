using Flyback.Core.Graph;
using Flyback.Engine.Graph;

namespace Flyback.Engine.Language;

/// <summary>
/// The text language, which parses to a patch and to nothing else (ADR-0065).
/// </summary>
/// <remarks>
/// There is no interpreter here and no second engine: what comes out is the same
/// <see cref="Patch"/> the editor builds and <see cref="PatchIO"/> writes, so
/// everything downstream is reached without knowing this exists.
/// </remarks>
public static class PatchLanguage
{
    /// <summary>The extension a source file takes, beside .fbk for the patch it builds into.</summary>
    public const string FileExtension = "fbks";

    /// <summary>The latest version of the text this build reads, as a <c>language</c> line names it.</summary>
    public const int Version = 1;

    /// <summary>
    /// The patch <paramref name="source"/> describes, against
    /// <paramref name="against"/> or the installed catalog. Never throws: every way
    /// a source file can be wrong is a <see cref="LanguageIssue"/> with a line and a
    /// column, because the thing reading it is usually an editor.
    /// </summary>
    public static LanguageLoad Build(string source, ModuleCatalog? against = null)
    {
        var modules = against ?? NodeCatalog.Current;
        var issues = new List<LanguageIssue>();

        var tokens = StatementTokens.ForParsing(Lexer.Scan(source, issues));
        var statements = new Parser(tokens, issues).Parse();
        var binder = new Binder(modules, issues);
        var patch = binder.Build(statements);

        return new LanguageLoad(patch, [.. issues.OrderBy(i => i.Line).ThenBy(i => i.Column)])
        {
            Source = source,
            Map = binder.Map(source),
            Levels = binder.Levels,
            Pack = binder.Pack,
            Modules = Offered(binder, source),
        };
    }

    /// <summary>The modules a pack offers, each with the card written above it.</summary>
    private static IReadOnlyList<PackModule> Offered(Binder binder, string source)
    {
        if (binder.Pack is not { } pack || binder.Offered.Count == 0) return [];

        var lines = source.ReplaceLineEndings("\n").Split('\n');

        return [.. binder.Offered.Select(module => new PackModule(
            pack.Id + "." + module.Name,
            module.Name,
            [.. module.Parameters.Select(p => p.Name)],
            ModuleCard.Above(lines, module.Line),
            module.Line))];
    }
}
