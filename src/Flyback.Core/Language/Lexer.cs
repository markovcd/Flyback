using System.Globalization;
using System.Text;

namespace Flyback.Core.Language;

/// <summary>What one piece of source text is.</summary>
public enum TokenKind
{
    Identifier,

    /// <summary>A plain number, and what a note or a duration has already become.</summary>
    Number,

    /// <summary>The text between two quotes, with nothing done to what is inside.</summary>
    Text,

    /// <summary>
    /// The raw contents of a bracketed block, captured whole rather than
    /// tokenised — see <see cref="StepNotation"/> for why.
    /// </summary>
    Block,

    Pipe,
    BackWire,
    Range,
    Assign,
    Comma,
    Colon,
    Dot,
    OpenParen,
    CloseParen,
    OpenBrace,
    CloseBrace,

    Plus,
    Minus,
    Star,
    Slash,
    Percent,

    /// <summary>The end of a statement, once the continuation rules have had their say.</summary>
    NewLine,

    End,
}

/// <summary>
/// One token, and where it came from so that a complaint can point at it.
/// </summary>
/// <param name="Value">
/// What a <see cref="TokenKind.Number"/> is worth. A note and a duration are
/// numbers by the time they reach here — the scale a socket reads them on is
/// decided in the lexer, because that is where the spelling still exists.
/// </param>
/// <param name="Scaled">
/// Whether this number was written as a note or a duration rather than as a
/// bare figure. The binder checks it against the port's
/// <see cref="Graph.PortDisplay"/>, so that <c>20ms</c> on a plain socket is a
/// complaint rather than a silent -1.699.
/// </param>
public readonly record struct Token(
    TokenKind Kind,
    string Text,
    int Line,
    int Column,
    double Value = 0d,
    NumberStyle Scaled = NumberStyle.Plain);

/// <summary>How a number was written, which decides what sockets will take it.</summary>
public enum NumberStyle
{
    Plain,

    /// <summary>Written as a note name, so it belongs on a <see cref="Graph.PortDisplay.Note"/> socket.</summary>
    Note,

    /// <summary>Written with a unit of time, so it belongs on a <see cref="Graph.PortDisplay.Duration"/> socket.</summary>
    Duration,
}

/// <summary>
/// Source text to tokens. Hand-written, because
/// [0019](../../../docs/adr/0019-no-third-party-dependencies-in-the-engine.md)
/// leaves the engine no parser library to reach for.
/// </summary>
/// <remarks>
/// Two things here are not the ordinary shape of a lexer, and both are there so
/// that the parser can stay simple.
/// <para>
/// A bracketed block is captured as one token holding its raw text. Brackets
/// start a step block and nothing else, and what is inside one is a different
/// language — <c>~</c>, <c>@</c>, <c>!</c>, <c>%</c> and <c>&lt;&gt;</c> all
/// mean something there that they do not mean outside. Keeping it whole means
/// neither half has to know about the other.
/// </para>
/// <para>
/// Newlines survive lexing and are thinned afterwards, in <see cref="Statements"/>.
/// A statement ends at a line break, but a pipeline may be written across
/// several, and which is which is a question about the tokens either side — far
/// easier to answer over a finished list than one character at a time.
/// </para>
/// </remarks>
public static class Lexer
{
    /// <summary>The units a duration may be written in, longest first so that "ms" wins over "s".</summary>
    private static readonly (string Suffix, double Seconds)[] Durations =
        [("us", 1e-6d), ("ms", 1e-3d), ("s", 1d)];

    /// <summary>
    /// Every token in <paramref name="source"/>, ending with
    /// <see cref="TokenKind.End"/>. Anything it cannot read is reported rather
    /// than thrown, so one bad character does not cost the rest of the file.
    /// </summary>
    public static IReadOnlyList<Token> Scan(string source, List<LanguageIssue> issues)
    {
        var tokens = new List<Token>();
        var line = 1;
        var lineStart = 0;

        for (var i = 0; i < source.Length;)
        {
            var c = source[i];
            var column = i - lineStart + 1;

            if (c == '\r') { i++; continue; }

            if (c == '\n')
            {
                tokens.Add(new Token(TokenKind.NewLine, "\n", line, column));
                i++;
                line++;
                lineStart = i;
                continue;
            }

            if (c is ' ' or '\t') { i++; continue; }

            // To the end of the line, and the newline itself is left to be read
            // as the statement break it is.
            if (c == '#')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            if (c == '"')
            {
                var text = new StringBuilder();
                var at = i + 1;

                // No escapes. A sample's path is the only string the language
                // has, and on Windows one is full of backslashes that mean
                // themselves — treating them as escapes would break every path
                // to buy a quote nobody puts in a filename.
                while (at < source.Length && source[at] != '"' && source[at] != '\n') text.Append(source[at++]);

                if (at >= source.Length || source[at] != '"')
                {
                    issues.Add(new LanguageIssue(line, column, "this text is never closed."));
                    i = at;
                    continue;
                }

                tokens.Add(new Token(TokenKind.Text, text.ToString(), line, column));
                i = at + 1;
                continue;
            }

            if (c == '[')
            {
                if (Block(source, i, out var inner, out var after))
                {
                    tokens.Add(new Token(TokenKind.Block, inner, line, column));

                    for (var scan = i; scan < after; scan++)
                        if (source[scan] == '\n') { line++; lineStart = scan + 1; }

                    i = after;
                    continue;
                }

                issues.Add(new LanguageIssue(line, column, "this block is never closed."));
                i = source.Length;
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                i = Number(source, i, line, column, tokens);
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                i = Word(source, i, line, column, tokens);
                continue;
            }

            if (Punctuation(source, i, line, column) is { } punctuation)
            {
                tokens.Add(punctuation);
                i += punctuation.Text.Length;
                continue;
            }

            issues.Add(new LanguageIssue(line, column, $"'{c}' means nothing here."));
            i++;
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, line, source.Length - lineStart + 1));
        return tokens;
    }

    /// <summary>
    /// The tokens with the newlines that are not statement breaks taken out.
    /// </summary>
    /// <remarks>
    /// A line break ends a statement unless the line is obviously unfinished or
    /// the next one is obviously a continuation. Both halves are needed: the
    /// first covers a pipeline broken after its <c>|&gt;</c>, and the second the
    /// far commoner shape where the operator leads the next line instead.
    /// </remarks>
    public static IReadOnlyList<Token> Statements(IReadOnlyList<Token> tokens)
    {
        var kept = new List<Token>(tokens.Count);

        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != TokenKind.NewLine)
            {
                kept.Add(tokens[i]);
                continue;
            }

            // Runs of blank lines are one break, and a break before the first
            // token or after the last is no break at all.
            if (kept.Count == 0 || kept[^1].Kind == TokenKind.NewLine) continue;
            if (Unfinished(kept[^1].Kind)) continue;

            var next = i + 1;
            while (next < tokens.Count && tokens[next].Kind == TokenKind.NewLine) next++;

            if (next < tokens.Count && Continues(tokens[next].Kind)) continue;

            kept.Add(tokens[i]);
        }

        return kept;
    }

    /// <summary>Whether a line ending on this token cannot be a whole statement.</summary>
    private static bool Unfinished(TokenKind kind) => kind
        is TokenKind.Pipe or TokenKind.BackWire or TokenKind.Assign or TokenKind.Comma
        or TokenKind.Colon or TokenKind.Dot or TokenKind.Range or TokenKind.OpenParen
        or TokenKind.OpenBrace or TokenKind.Plus or TokenKind.Minus or TokenKind.Star
        or TokenKind.Slash or TokenKind.Percent;

    /// <summary>Whether a line starting on this token is carrying on the one above.</summary>
    private static bool Continues(TokenKind kind) => kind
        is TokenKind.Pipe or TokenKind.Plus or TokenKind.Minus or TokenKind.Star
        or TokenKind.Slash or TokenKind.Percent or TokenKind.CloseParen or TokenKind.Block;

    /// <summary>
    /// The text inside a bracketed block, counting nesting so that a subdivided
    /// step keeps its own brackets.
    /// </summary>
    private static bool Block(string source, int open, out string inner, out int after)
    {
        var depth = 0;

        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '[') depth++;
            else if (source[i] == ']' && --depth == 0)
            {
                inner = source[(open + 1)..i];
                after = i + 1;
                return true;
            }
        }

        inner = string.Empty;
        after = source.Length;
        return false;
    }

    /// <summary>
    /// A number, and the unit of time after it where there is one.
    /// </summary>
    /// <remarks>
    /// The decimal point is only taken when a digit follows it, which is what
    /// keeps <c>-2..2</c> a range of two whole numbers rather than a number with
    /// a second point in it.
    /// </remarks>
    private static int Number(string source, int start, int line, int column, List<Token> tokens)
    {
        var i = start;
        while (i < source.Length && char.IsAsciiDigit(source[i])) i++;

        if (i + 1 < source.Length && source[i] == '.' && char.IsAsciiDigit(source[i + 1]))
        {
            i++;
            while (i < source.Length && char.IsAsciiDigit(source[i])) i++;
        }

        var text = source[start..i];
        var value = double.Parse(text, CultureInfo.InvariantCulture);

        foreach (var (suffix, seconds) in Durations)
        {
            if (!source.AsSpan(i).StartsWith(suffix, StringComparison.Ordinal)) continue;

            // A unit is only a unit when the word stops there; "12same" is not
            // twelve seconds followed by a name.
            var end = i + suffix.Length;
            if (end < source.Length && (char.IsAsciiLetterOrDigit(source[end]) || source[end] == '_')) continue;

            // A Duration socket holds the power of ten, not the seconds — see
            // PortDisplay.Duration. Nought seconds has no logarithm, and a
            // socket cannot hold one either, so it is refused at the port.
            var decades = value * seconds <= 0d ? double.NegativeInfinity : Math.Log10(value * seconds);

            tokens.Add(new Token(TokenKind.Number, text + suffix, line, column, decades, NumberStyle.Duration));
            return end;
        }

        tokens.Add(new Token(TokenKind.Number, text, line, column, value));
        return i;
    }

    /// <summary>A name, or a note written as one.</summary>
    /// <remarks>
    /// The note is tried first, and it has to be: a sharp is spelled with the
    /// same character a comment starts with, so reading <c>C#4</c> as a name
    /// would leave <c>#4</c> to swallow the rest of the line. Nothing else in
    /// the language puts a <c>#</c> inside a word, so trying the narrow shape
    /// before the wide one costs nothing and settles it.
    /// </remarks>
    private static int Word(string source, int start, int line, int column, List<Token> tokens)
    {
        if (Spelled(source, start) is var (note, after))
        {
            tokens.Add(new Token(TokenKind.Number, source[start..after], line, column, note, NumberStyle.Note));
            return after;
        }

        var i = start;
        while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_')) i++;

        tokens.Add(new Token(TokenKind.Identifier, source[start..i], line, column));
        return i;
    }

    /// <summary>
    /// The note beginning at <paramref name="start"/> and where it ends, or null
    /// where a name begins there instead.
    /// </summary>
    private static (double Note, int After)? Spelled(string source, int start)
    {
        var i = start;

        if (i >= source.Length || source[i] is < 'A' or > 'G') return null;
        i++;

        if (i < source.Length && (source[i] == '#' || source[i] == 'b')) i++;
        if (i < source.Length && source[i] == '-') i++;

        var digits = i;
        while (i < source.Length && char.IsAsciiDigit(source[i])) i++;

        if (i == digits) return null;

        // A name may not follow: "C4x" is a name, all of it, and reading two
        // characters of it as a note would leave a stray "x" behind.
        if (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_')) return null;

        return Note(source[start..i]) is { } note ? (note, i) : null;
    }

    /// <summary>
    /// The note a word spells, or null where it spells a name instead.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow: a capital A to G, an optional sharp or flat, then an
    /// octave. Anything else is a name, so only a binding called something like
    /// <c>A3</c> could be shadowed by this — and the octave is what makes that
    /// unlikely enough to accept, since it is the part no ordinary name has.
    /// <para>
    /// Both spellings are read although only sharps are written back
    /// (<see cref="Graph.Pitch.ClassName"/>): reading is where somebody else's
    /// spelling arrives, and refusing <c>Bb2</c> would be refusing the name of a
    /// note over which of its two names it was given.
    /// </para>
    /// </remarks>
    public static double? Note(string word)
    {
        if (word.Length < 2) return null;
        if (word[0] is < 'A' or > 'G') return null;

        // C is 0, and the gaps are where the black keys are.
        var natural = word[0] switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, _ => 11,
        };

        var at = 1;
        if (word[at] == '#') { natural++; at++; }
        else if (word[at] == 'b') { natural--; at++; }

        if (at >= word.Length) return null;

        var negative = word[at] == '-';
        if (negative) at++;

        if (at >= word.Length) return null;

        var octave = 0;
        for (; at < word.Length; at++)
        {
            if (!char.IsAsciiDigit(word[at])) return null;
            octave = octave * 10 + (word[at] - '0');
        }

        if (negative) octave = -octave;

        // Scientific octaves, where middle C is C4 and 60 — the same numbering
        // Pitch.Name writes back.
        return (octave + 1) * (int)Graph.Pitch.Semitones + natural;
    }

    /// <summary>The token some punctuation makes, or null where it makes none.</summary>
    private static Token? Punctuation(string source, int i, int line, int column)
    {
        var rest = source.AsSpan(i);

        Token Two(TokenKind kind, string text) => new(kind, text, line, column);

        if (rest.StartsWith("|>", StringComparison.Ordinal)) return Two(TokenKind.Pipe, "|>");
        if (rest.StartsWith("<-", StringComparison.Ordinal)) return Two(TokenKind.BackWire, "<-");
        if (rest.StartsWith("..", StringComparison.Ordinal)) return Two(TokenKind.Range, "..");

        return source[i] switch
        {
            '=' => Two(TokenKind.Assign, "="),
            ',' => Two(TokenKind.Comma, ","),
            ':' => Two(TokenKind.Colon, ":"),
            '.' => Two(TokenKind.Dot, "."),
            '(' => Two(TokenKind.OpenParen, "("),
            ')' => Two(TokenKind.CloseParen, ")"),
            '{' => Two(TokenKind.OpenBrace, "{"),
            '}' => Two(TokenKind.CloseBrace, "}"),
            '+' => Two(TokenKind.Plus, "+"),
            '-' => Two(TokenKind.Minus, "-"),
            '*' => Two(TokenKind.Star, "*"),
            '/' => Two(TokenKind.Slash, "/"),
            '%' => Two(TokenKind.Percent, "%"),
            _ => null,
        };
    }
}
