using Flyback.Core.Graph;

namespace Flyback.Engine.Language;

/// <summary>
/// The text before one place in a patch, read as tokens and the names in sight
/// there, with the questions an editor asks of it: which call is open, what a
/// dotted name before a bracket means, which module a name is.
/// </summary>
/// <remarks>
/// Tokens rather than the syntax tree, because text being typed is never finished
/// and a half-written call does not parse.
/// </remarks>
internal sealed class Caret
{
    private Caret(IReadOnlyList<Token> tokens, ModuleCatalog modules)
    {
        Tokens = tokens;
        Modules = modules;
        Names = VisibleNames.Before(tokens);
        Written = ModuleNames.For(modules);
    }

    /// <summary>The statement tokens before the place, with no end token.</summary>
    public IReadOnlyList<Token> Tokens { get; }

    public IReadOnlyList<VisibleName> Names { get; }

    public ModuleCatalog Modules { get; }

    public ModuleNames Written { get; }

    /// <summary>The text before <paramref name="offset"/>, or null where it is inside a comment, a text or a step block.</summary>
    public static Caret? Before(string source, int offset, ModuleCatalog modules)
    {
        if (Quiet(source, offset)) return null;

        var issues = new List<LanguageIssue>();
        var scanned = Lexer.Scan(source[..offset], issues);

        if (issues.Any(i => i.Code == IssueCode.UnclosedBlock)) return null;

        return new Caret([.. StatementTokens.ForParsing(scanned).Where(t => t.Kind != TokenKind.End)], modules);
    }

    /// <summary>Where line and column fall in the text, clamped to it.</summary>
    public static int Offset(string source, int line, int column)
    {
        var at = 0;

        for (var l = 1; l < line; l++)
        {
            var next = source.IndexOf('\n', at);
            if (next < 0) return source.Length;
            at = next + 1;
        }

        var end = source.IndexOf('\n', at) is var newline and >= 0 ? newline : source.Length;

        return Math.Clamp(at + column - 1, at, end);
    }

    public static bool IsWordCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    /// <summary>The first sentence of <paramref name="text"/>, or <paramref name="otherwise"/> where it says nothing.</summary>
    public static string FirstSentence(string text, string otherwise)
    {
        if (string.IsNullOrWhiteSpace(text)) return otherwise;

        var stop = text.IndexOf(". ", StringComparison.Ordinal);

        return (stop >= 0 ? text[..(stop + 1)] : text).ReplaceLineEndings(" ").Trim();
    }

    /// <summary>What an input takes and sits at unwired, as <c>flyback-cli modules</c> lists it, or what an output gives where that is not a number.</summary>
    public static string Reads(PortSpec port, bool input) => input
        ? string.Join(", ", new[] { port.Span, port.NeedsAWire ? port.Unwired : $"at {port.Unwired}" }.Where(s => s.Length > 0))
        : port.Kind == PortKind.Color ? "color" : string.Empty;

    /// <summary>The call whose brackets the place is inside, by its callee and the index of its bracket.</summary>
    public (string Callee, int Open)? Call()
    {
        var depth = 0;

        for (var i = Tokens.Count - 1; i >= 0; i--)
        {
            switch (Tokens[i].Kind)
            {
                case TokenKind.CloseParen:
                    depth++;
                    break;
                case TokenKind.OpenParen when depth > 0:
                    depth--;
                    break;
                case TokenKind.OpenParen:
                    return CalleeBefore(i) is { } callee ? (callee, i) : null;
                case TokenKind.OpenBrace or TokenKind.CloseBrace or TokenKind.NewLine when depth == 0:
                    return null;
            }
        }

        return null;
    }

    /// <summary>The arguments a call holds so far, each the name it gives or null where it gives none, the last being the one the place is in.</summary>
    public IReadOnlyList<string?> Arguments(int open)
    {
        var arguments = new List<string?>();
        var depth = 0;
        var start = open + 1;

        for (var i = open; i <= Tokens.Count; i++)
        {
            var kind = i < Tokens.Count ? Tokens[i].Kind : TokenKind.End;

            if (kind == TokenKind.OpenParen) depth++;
            else if (kind == TokenKind.CloseParen) depth--;

            if ((depth == 1 && kind == TokenKind.Comma) || kind == TokenKind.End)
            {
                arguments.Add(start + 1 < Tokens.Count
                    && Tokens[start].Kind == TokenKind.Identifier
                    && Tokens[start + 1].Kind == TokenKind.Colon
                        ? Tokens[start].Text
                        : null);
                start = i + 1;
            }
        }

        return arguments;
    }

    /// <summary>The socket or parameter names a call already gives, from its bracket to the place.</summary>
    public HashSet<string> Named(int open) =>
        new(Arguments(open).OfType<string>(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a pipe runs into the call that opens at <paramref name="open"/>.</summary>
    public bool Piped(int open) =>
        ChainBefore(open, out var begins) is not null && begins > 0 && Tokens[begins - 1].Kind == TokenKind.Pipe;

    /// <summary>The index of the bracket that opens the one closing at <paramref name="close"/>, or -1.</summary>
    public int Opening(int close)
    {
        var depth = 0;

        for (var i = close; i >= 0; i--)
        {
            if (Tokens[i].Kind == TokenKind.CloseParen) depth++;
            else if (Tokens[i].Kind == TokenKind.OpenParen && --depth == 0) return i;
        }

        return -1;
    }

    /// <summary>The dotted name written straight before a call's bracket, where nothing is selected ahead of it.</summary>
    public string? CalleeBefore(int open) =>
        ChainBefore(open, out var begins) is { } chain && (begins == 0 || Tokens[begins - 1].Kind != TokenKind.Dot) ? chain : null;

    /// <summary>The dotted name that ends just before <paramref name="end"/>, and the index it begins at.</summary>
    public string? ChainBefore(int end, out int begins)
    {
        begins = end;

        if (end < 1 || Tokens[end - 1].Kind != TokenKind.Identifier) return null;

        var parts = new List<string> { Tokens[end - 1].Text };
        begins = end - 1;

        while (begins >= 2 && Tokens[begins - 1].Kind == TokenKind.Dot && Tokens[begins - 2].Kind == TokenKind.Identifier)
        {
            parts.Insert(0, Tokens[begins - 2].Text);
            begins -= 2;
        }

        return string.Join('.', parts);
    }

    /// <summary>The module a dot at the end of the tokens follows: a call's, or the one a name in sight was bound to.</summary>
    public NodeDef? Selected(out string? path, out bool startsStatement)
    {
        var dot = Tokens.Count - 1;
        path = null;
        startsStatement = false;

        if (dot >= 1 && Tokens[dot - 1].Kind == TokenKind.CloseParen)
        {
            var open = Opening(dot - 1);
            return open >= 0 && CalleeBefore(open) is { } callee ? Module(callee) : null;
        }

        if (ChainBefore(dot, out var begins) is not { } chain) return null;

        path = chain;
        startsStatement = begins == 0 || Tokens[begins - 1].Kind is TokenKind.NewLine or TokenKind.OpenBrace;

        if (chain == "out") return Modules.Get(NodeCatalog.OutputTypeId);

        return Bound(chain) is { Callee: { } bound } ? Module(bound) : null;
    }

    /// <summary>The let in sight by this name.</summary>
    public VisibleName? Bound(string name) =>
        Names.LastOrDefault(n => n.Name == name && n.Kind == CompletionKind.Name);

    /// <summary>The def in sight by this name.</summary>
    public VisibleName? Def(string name) =>
        Names.LastOrDefault(n => n.Name == name && n.Kind == CompletionKind.Definition);

    /// <summary>The module a name means, where no def in sight takes the name first.</summary>
    public NodeDef? Module(string name) =>
        Def(name) is null ? Written.Find(name, out _, out _) : null;

    /// <summary>Whether <paramref name="at"/> is inside a comment or a text on its line.</summary>
    private static bool Quiet(string source, int at)
    {
        var inText = false;

        for (var i = source.LastIndexOf('\n', Math.Max(at - 1, 0)) + 1; i < at; i++)
        {
            var c = source[i];

            if (c is '"' or '“' or '”') inText = !inText;
            else if (c == '#' && !inText) return true;
        }

        return inText;
    }
}
