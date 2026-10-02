using Flyback.Core.Graph;
using Flyback.Engine.Graph;

namespace Flyback.Engine.Language;

/// <summary>Token positions and source spans shared by source mapping and edits.</summary>
internal sealed class SourceMapText
{
    private readonly int[] starts;
    private readonly Dictionary<int, int> beginning = [];

    internal SourceMapText(string source)
    {
        Source = source;
        starts = Starts(source);
        Tokens = source.Length == 0 ? [] : Lexer.Scan(source, []);

        for (var i = 0; i < Tokens.Count; i++) beginning.TryAdd(Offset(Tokens[i]), i);
    }

    internal string Source { get; }

    internal IReadOnlyList<Token> Tokens { get; }

    /// <summary>Maps a one-based line and column to a clamped source offset.</summary>
    internal int Offset(Site site)
    {
        var line = Math.Clamp(site.Line, 1, starts.Length);

        return Math.Clamp(starts[line - 1] + site.Column - 1, 0, Source.Length);
    }

    internal int Offset(Token token) => Offset(new Site(token.Line, token.Column));

    internal bool TryBeginning(int offset, out int index) => beginning.TryGetValue(offset, out index);

    internal int Closed(int open)
    {
        var depth = 0;

        for (var i = open; i < Source.Length; i++)
        {
            if (Source[i] == '[') depth++;
            else if (Source[i] == ']' && --depth == 0) return i + 1;
        }

        return Source.Length;
    }

    /// <summary>The source span of the call or operator beginning at a site.</summary>
    internal (int From, int To)? Extent(Site site)
    {
        var from = Offset(site);

        if (!beginning.TryGetValue(from, out var i)) return null;

        if (Tokens[i].Kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
            return (from, from + Tokens[i].Text.Length);

        if (Tokens[i].Kind != TokenKind.Identifier) return null;

        var to = from + Tokens[i].Text.Length;
        i++;

        while (i + 1 < Tokens.Count
            && Tokens[i].Kind == TokenKind.Dot
            && Tokens[i + 1].Kind == TokenKind.Identifier)
        {
            to = Offset(Tokens[i + 1]) + Tokens[i + 1].Text.Length;
            i += 2;
        }

        if (i >= Tokens.Count || Tokens[i].Kind != TokenKind.OpenParen) return (from, to);

        var depth = 0;

        for (; i < Tokens.Count; i++)
        {
            if (Tokens[i].Kind == TokenKind.OpenParen)
            {
                depth++;
            }
            else if (Tokens[i].Kind == TokenKind.CloseParen && --depth == 0)
            {
                to = Offset(Tokens[i]) + 1;
                i++;
                break;
            }
        }

        if (i < Tokens.Count && Tokens[i].Kind == TokenKind.Block) to = Closed(Offset(Tokens[i]));

        return (from, to);
    }

    /// <summary>The outer call parentheses at a site, or null when it is not a call.</summary>
    internal (int Open, int Close)? Parentheses(Site site)
    {
        if (!beginning.TryGetValue(Offset(site), out var i)) return null;

        var open = -1;
        var depth = 0;

        for (; i < Tokens.Count; i++)
        {
            if (Tokens[i].Kind == TokenKind.OpenParen)
            {
                if (depth++ == 0) open = Offset(Tokens[i]);
            }
            else if (Tokens[i].Kind == TokenKind.CloseParen && --depth == 0)
            {
                return (open, Offset(Tokens[i]));
            }
            else if (depth == 0 && Tokens[i].Kind is not (TokenKind.Identifier or TokenKind.Dot))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>The complete literal span, including a sign or string quotes.</summary>
    internal (int From, int Length)? Value(Site site)
    {
        var from = Offset(site);

        if (!beginning.TryGetValue(from, out var i)) return null;

        if (Tokens[i].Kind == TokenKind.Text) return (from, Tokens[i].Text.Length + 2);

        while (i < Tokens.Count && Tokens[i].Kind == TokenKind.Minus) i++;

        if (i >= Tokens.Count || Tokens[i].Kind != TokenKind.Number) return null;

        return (from, Offset(Tokens[i]) + Tokens[i].Text.Length - from);
    }

    internal (int From, int To)? Block(int close)
    {
        if (!beginning.TryGetValue(close, out var i) || i + 1 >= Tokens.Count) return null;
        if (Tokens[i + 1].Kind != TokenKind.Block) return null;

        var from = Offset(Tokens[i + 1]);

        return (from, Closed(from));
    }

    /// <summary>The unkeyed string argument of a call, if present.</summary>
    internal (int From, int Length)? Text((int Open, int Close) brackets)
    {
        if (!beginning.TryGetValue(brackets.Open, out var i)) return null;

        var depth = 0;

        for (; i < Tokens.Count; i++)
        {
            if (Tokens[i].Kind == TokenKind.OpenParen) depth++;
            else if (Tokens[i].Kind == TokenKind.CloseParen && --depth == 0) return null;
            else if (depth == 1
                && Tokens[i].Kind == TokenKind.Text
                && !(i >= 2 && Tokens[i - 1].Kind == TokenKind.Colon))
            {
                return (Offset(Tokens[i]), Tokens[i].Text.Length + 2);
            }
        }

        return null;
    }

    /// <summary>The first matching metadata statement and its full source span.</summary>
    internal (int From, int To)? Opened(string word, TokenKind next)
    {
        (int From, int To)? found = null;
        var start = true;

        for (var i = 0; i < Tokens.Count && found is null; i++)
        {
            var token = Tokens[i];

            if (start
                && token.Kind == TokenKind.Identifier
                && token.Text == word
                && i + 1 < Tokens.Count
                && Tokens[i + 1].Kind == next)
            {
                var after = Tokens[i + 1];
                var to = Offset(after) + after.Text.Length + (next == TokenKind.Text ? 2 : 0);

                if (i + 2 < Tokens.Count && Tokens[i + 2].Kind == TokenKind.Block)
                    to = Closed(Offset(Tokens[i + 2]));

                if (next == TokenKind.Number && i + 3 < Tokens.Count
                    && Tokens[i + 2].Kind == TokenKind.Colon && Tokens[i + 3].Kind == TokenKind.Number)
                    to = Offset(Tokens[i + 3]) + Tokens[i + 3].Text.Length;

                // Description strings can continue on later lines; tags can repeat.
                for (var j = i + 2; next == TokenKind.Text && j < Tokens.Count; j++)
                {
                    if (Tokens[j].Kind == TokenKind.NewLine) continue;
                    if (Tokens[j].Kind != TokenKind.Text) break;

                    to = Offset(Tokens[j]) + Tokens[j].Text.Length + 2;
                }

                found = (Offset(token), to);
            }

            start = token.Kind is TokenKind.NewLine or TokenKind.OpenBrace;
        }

        return found;
    }

    /// <summary>Statement words, declared names, and source spans.</summary>
    internal List<(string? Word, string? Declares, int From, int To)> Statements()
    {
        var found = new List<(string? Word, string? Declares, int From, int To)>();
        var statements = StatementTokens.ForParsing(Tokens);

        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i].Kind is TokenKind.NewLine or TokenKind.OpenBrace or TokenKind.CloseBrace) continue;

            var first = i;

            while (i + 1 < statements.Count
                && statements[i + 1].Kind is not (TokenKind.NewLine or TokenKind.OpenBrace or TokenKind.CloseBrace)) i++;

            var end = statements[i];
            var word = statements[first].Kind == TokenKind.Identifier ? statements[first].Text : null;
            var declares = first + 2 <= i
                && statements[first + 1].Kind == TokenKind.Identifier
                && statements[first + 2].Kind == TokenKind.Assign
                    ? statements[first + 1].Text
                    : null;

            found.Add((word, declares, Offset(statements[first]),
                Offset(end) + end.Text.Length + (end.Kind == TokenKind.Text ? 2 : 0)));
        }

        return found;
    }

    private static int[] Starts(string source)
    {
        var found = new List<int> { 0 };

        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n') found.Add(i + 1);

        return [.. found];
    }
}
