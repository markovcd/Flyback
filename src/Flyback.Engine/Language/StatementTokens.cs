namespace Flyback.Core.Language;

internal static class StatementTokens
{
    /// <summary>
    /// Removes the newlines that continue a statement instead of ending it.
    /// </summary>
    internal static IReadOnlyList<Token> ForParsing(IReadOnlyList<Token> tokens)
    {
        var kept = new List<Token>(tokens.Count);

        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != TokenKind.NewLine)
            {
                kept.Add(tokens[i]);
                continue;
            }

            // Blank lines are one break, and leading or trailing breaks are not statements.
            if (kept.Count == 0 || kept[^1].Kind == TokenKind.NewLine) continue;
            if (Unfinished(kept[^1].Kind)) continue;

            var next = i + 1;
            while (next < tokens.Count && tokens[next].Kind == TokenKind.NewLine) next++;

            if (next < tokens.Count && Continues(tokens[next].Kind)) continue;

            kept.Add(tokens[i]);
        }

        return kept;
    }

    private static bool Unfinished(TokenKind kind) => kind
        is TokenKind.Pipe or TokenKind.BackWire or TokenKind.Assign or TokenKind.Comma
        or TokenKind.Colon or TokenKind.Dot or TokenKind.Range or TokenKind.OpenParen
        or TokenKind.OpenBrace or TokenKind.Plus or TokenKind.Minus or TokenKind.Star
        or TokenKind.Slash or TokenKind.Percent;

    /// <summary>
    /// A string may continue metadata such as a description; braces may start
    /// a group on the next line, and a dot may start a selected output.
    /// </summary>
    private static bool Continues(TokenKind kind) => kind
        is TokenKind.Pipe or TokenKind.Plus or TokenKind.Minus or TokenKind.Star
        or TokenKind.Slash or TokenKind.Percent or TokenKind.CloseParen or TokenKind.Block
        or TokenKind.Text or TokenKind.OpenBrace or TokenKind.Dot;
}
