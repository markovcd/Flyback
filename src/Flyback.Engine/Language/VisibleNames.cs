namespace Flyback.Engine.Language;

/// <summary>
/// The names the text has bound that are in sight where its tokens stop, read
/// from tokens alone so that a half-written line still has an answer.
/// </summary>
/// <remarks>
/// Follows ADR-0218: every block keeps its defs, a group's lets are in sight
/// after it closes, a def's or a loop's are not, and parameters and loop names
/// are in sight only inside their body.
/// </remarks>
internal static class VisibleNames
{
    /// <param name="tokens">Statement tokens (<see cref="StatementTokens.ForParsing"/>), cut where the cursor is.</param>
    public static IReadOnlyList<VisibleName> Before(IReadOnlyList<Token> tokens)
    {
        var frames = new Stack<Frame>();
        frames.Push(new Frame(false, []));

        List<VisibleName>? waiting = null;
        List<VisibleName>? lineParameters = null;
        var lineDepth = -1;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            switch (token.Kind)
            {
                case TokenKind.Identifier when token.Text == "let":
                    if (At(tokens, i + 1, TokenKind.Identifier) is { } bound)
                        frames.Peek().Names.Add(new VisibleName(bound.Text, CompletionKind.Name, bound.Line, Callee(tokens, i + 2), []));
                    else if (At(tokens, i + 1, TokenKind.OpenParen) is not null)
                        frames.Peek().Names.AddRange(Listed(tokens, i + 1, CompletionKind.Name));
                    break;

                case TokenKind.Identifier when token.Text == "def":
                    if (At(tokens, i + 1, TokenKind.Identifier) is not { } named) break;

                    var bracketed = At(tokens, i + 2, TokenKind.OpenParen) is not null;
                    var parameters = bracketed ? Listed(tokens, i + 2, CompletionKind.Parameter) : [];

                    frames.Peek().Names.Add(new VisibleName(
                        named.Text, CompletionKind.Definition, named.Line, null, [.. parameters.Select(p => p.Name)]));

                    var body = bracketed ? Closing(tokens, i + 2) + 1 : i + 2;

                    if (At(tokens, body, TokenKind.Assign) is null) break;

                    if (At(tokens, body + 1, TokenKind.OpenBrace) is not null)
                    {
                        waiting = parameters;
                    }
                    else
                    {
                        lineParameters = parameters;
                        lineDepth = frames.Count;
                    }

                    break;

                case TokenKind.Identifier when token.Text == "for":
                    if (At(tokens, i + 1, TokenKind.Identifier) is { } item)
                        waiting = [new VisibleName(item.Text, CompletionKind.Parameter, item.Line, null, [])];
                    else if (At(tokens, i + 1, TokenKind.OpenParen) is not null)
                        waiting = Listed(tokens, i + 1, CompletionKind.Parameter);
                    break;

                case TokenKind.OpenBrace:
                    var group = (i >= 1 && tokens[i - 1] is { Kind: TokenKind.Identifier, Text: "group" })
                        || (i >= 2 && tokens[i - 1].Kind == TokenKind.Text && tokens[i - 2] is { Kind: TokenKind.Identifier, Text: "group" });

                    frames.Push(new Frame(group, waiting ?? []));
                    waiting = null;
                    break;

                case TokenKind.CloseBrace when frames.Count > 1:
                    var closed = frames.Pop();

                    if (closed.Group)
                        frames.Peek().Names.AddRange(closed.Names.Where(n => n.Kind == CompletionKind.Name));
                    break;

                case TokenKind.NewLine when lineParameters is not null && frames.Count == lineDepth:
                    lineParameters = null;
                    break;
            }
        }

        return [.. frames.SelectMany(f => f.Names), .. lineParameters ?? []];
    }

    private static Token? At(IReadOnlyList<Token> tokens, int index, TokenKind kind) =>
        index < tokens.Count && tokens[index].Kind == kind ? tokens[index] : null;

    /// <summary>The names in a bracketed list that starts at <paramref name="open"/>: a def's parameters or a tuple's parts, defaults skipped.</summary>
    private static List<VisibleName> Listed(IReadOnlyList<Token> tokens, int open, CompletionKind kind)
    {
        var names = new List<VisibleName>();
        var depth = 0;

        for (var i = open; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Kind == TokenKind.OpenParen) depth++;
            else if (token.Kind == TokenKind.CloseParen && --depth == 0) break;
            else if (depth == 1 && token.Kind == TokenKind.Identifier
                && tokens[i - 1].Kind is TokenKind.OpenParen or TokenKind.Comma)
                names.Add(new VisibleName(token.Text, kind, token.Line, null, []));
        }

        return names;
    }

    /// <summary>The index of the bracket that closes the one at <paramref name="open"/>, or the last token where none does.</summary>
    internal static int Closing(IReadOnlyList<Token> tokens, int open)
    {
        var depth = 0;

        for (var i = open; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.OpenParen) depth++;
            else if (tokens[i].Kind == TokenKind.CloseParen && --depth == 0) return i;
        }

        return tokens.Count - 1;
    }

    /// <summary>
    /// The module or def a <c>let</c> places, where its value is one call at the
    /// end of its pipeline with no output chosen after it.
    /// </summary>
    private static string? Callee(IReadOnlyList<Token> tokens, int assign)
    {
        if (At(tokens, assign, TokenKind.Assign) is null) return null;

        var stage = assign + 1;
        var depth = 0;
        var end = tokens.Count;

        for (var i = assign + 1; i < tokens.Count; i++)
        {
            var kind = tokens[i].Kind;

            if (kind is TokenKind.OpenParen or TokenKind.OpenBrace) depth++;
            else if (kind is TokenKind.CloseParen or TokenKind.CloseBrace) depth--;
            else if (depth == 0 && kind == TokenKind.Pipe) stage = i + 1;
            else if (depth == 0 && kind == TokenKind.NewLine) { end = i; break; }

            if (depth < 0) { end = i; break; }
        }

        var chain = Chain(tokens, stage, out var open);

        if (chain is null || At(tokens, open, TokenKind.OpenParen) is null) return null;

        var after = Closing(tokens, open) + 1;

        if (after < end && tokens[after].Kind == TokenKind.Block) after++;

        return after == end ? chain : null;
    }

    /// <summary>A dotted name starting at <paramref name="start"/>, and the index after it.</summary>
    private static string? Chain(IReadOnlyList<Token> tokens, int start, out int after)
    {
        after = start;

        if (At(tokens, start, TokenKind.Identifier) is not { } first) return null;

        var name = first.Text;
        after = start + 1;

        while (At(tokens, after, TokenKind.Dot) is not null && At(tokens, after + 1, TokenKind.Identifier) is { } next)
        {
            name += "." + next.Text;
            after += 2;
        }

        return name;
    }

    /// <summary>One block's names, and whether it is a group, whose lets outlive it.</summary>
    private sealed record Frame(bool Group, List<VisibleName> Names);
}
