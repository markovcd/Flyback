using System.Globalization;
using System.Text;

namespace Flyback.Core.Language;

/// <summary>
/// Source text to tokens. Hand-written, because ADR-0019 leaves the engine no
/// parser library to reach for.
/// </summary>
/// <remarks>
/// A bracketed block is captured as one token holding its raw text: what is
/// inside one is a different language — <c>~</c>, <c>@</c>, <c>!</c>, <c>%</c>
/// and <c>&lt;&gt;</c> all mean something there they do not mean outside — so
/// keeping it whole means neither half knows about the other.
/// <para>
/// Newlines survive lexing and are thinned in <see cref="Statements"/>: a
/// statement ends at a line break but a pipeline may be written across several,
/// and that is a question about the tokens either side.
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

            // A space pasted from a document, a non-breaking one or a thin one, is still a space.
            if (c is ' ' or '\t' || (c > '\u007f' && char.IsWhiteSpace(c))) { i++; continue; }

            // To the end of the line, and the newline itself is left to be read
            // as the statement break it is.
            if (c == '#')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            // Read to the end of the line as the comment it was meant to be, so
            // what it says is not taken apart into complaints of its own.
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                issues.Add(new LanguageIssue(line, column, IssueCode.SlashComment,
                    "a comment starts with '#', not '//'. To head a part of the patch, put it in a group: group \"Clock\" { ... }."));

                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }

            if (c == '"' || CurlyQuote(c))
            {
                var text = new StringBuilder();
                var at = i + 1;

                // No escapes. A sample's path is the only string the language
                // has, and on Windows one is full of backslashes that mean
                // themselves — treating them as escapes would break every path
                // to buy a quote nobody puts in a filename.
                while (at < source.Length && source[at] != '"' && !CurlyQuote(source[at]) && source[at] != '\n') text.Append(source[at++]);

                if (at >= source.Length || !(source[at] == '"' || CurlyQuote(source[at])))
                {
                    issues.Add(new LanguageIssue(line, column, IssueCode.UnclosedText, "this text is never closed."));
                    i = at;
                    continue;
                }

                // Read as the text it was meant to be, so the one complaint is about the quotes.
                if (CurlyQuote(c) || CurlyQuote(source[at]))
                {
                    var curly = CurlyQuote(c) ? c : source[at];

                    issues.Add(new LanguageIssue(line, CurlyQuote(c) ? column : at - lineStart + 1, IssueCode.LookalikeCharacter,
                        $"'{curly}' is a curly quote. Text is written between straight quotes: \"like this\"."));
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

                issues.Add(new LanguageIssue(line, column, IssueCode.UnclosedBlock, "this block is never closed."));
                i = source.Length;
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                i = Number(source, i, line, column, tokens, issues);
                continue;
            }

            // A number with its nought left off. A range's two points are read as the range.
            if (c == '.' && i + 1 < source.Length && char.IsAsciiDigit(source[i + 1]))
            {
                i = Malformed(source, i, line, column, tokens, issues);
                continue;
            }

            if (Misspelled(source, i, line, column, issues) is var (stand, length))
            {
                if (stand is { } token) tokens.Add(token);
                i += length;
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                i = Word(source, i, line, column, tokens, issues);
                continue;
            }

            if (Punctuation(source, i, line, column) is { } punctuation)
            {
                tokens.Add(punctuation);
                i += punctuation.Text.Length;
                continue;
            }

            issues.Add(Stray(c, line, column));
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

    /// <summary>
    /// Whether a line starting on this token is carrying on the one above. A
    /// string among them because no statement opens on one, and a description
    /// too long for one line goes on as a string on the next; a brace, so a
    /// group's may stand on a line of its own, and a dot, so an output may.
    /// </summary>
    private static bool Continues(TokenKind kind) => kind
        is TokenKind.Pipe or TokenKind.Plus or TokenKind.Minus or TokenKind.Star
        or TokenKind.Slash or TokenKind.Percent or TokenKind.CloseParen or TokenKind.Block
        or TokenKind.Text or TokenKind.OpenBrace or TokenKind.Dot;

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
    private static int Number(string source, int start, int line, int column, List<Token> tokens, List<LanguageIssue> issues)
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

        // Digits that run on into a letter or a second point are one mistake, said
        // whole, rather than a number and then whatever the rest reads as.
        if (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_' || (source[i] == '.' && !source.AsSpan(i).StartsWith(".."))))
            return Malformed(source, start, line, column, tokens, issues);

        tokens.Add(new Token(TokenKind.Number, text, line, column, value));
        return i;
    }

    /// <summary>
    /// Something that starts like a number and is not one, said as the whole of what
    /// was written and read as nought, so the rest of the line still reads.
    /// </summary>
    private static int Malformed(string source, int start, int line, int column, List<Token> tokens, List<LanguageIssue> issues)
    {
        var i = start;

        while (i < source.Length
               && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_' || (source[i] == '.' && !source.AsSpan(i).StartsWith(".."))))
            i++;

        var text = source[start..i];

        issues.Add(new LanguageIssue(line, column, IssueCode.MalformedNumber, text[0] == '.'
            ? $"'{text}' is not a number. Write 0{text}."
            : $"'{text}' is not a number. A number is written 220 or 0.5, a note C4 and a length of time 20ms."));

        tokens.Add(new Token(TokenKind.Number, text, line, column));
        return i;
    }

    private static bool CurlyQuote(char c) => c is '\u201c' or '\u201d' or '\u201e' or '\u201f';

    /// <summary>
    /// A character somebody wrote meaning one the language has: a pipe drawn as an
    /// arrow or a bar, a minus from a word processor, a semicolon. Said, and read as
    /// what it stood for where there is one, so that is the only complaint.
    /// </summary>
    /// <returns>The token it stands for, if any, and how many characters it took; null where it is none of these.</returns>
    private static (Token? Stand, int Length)? Misspelled(string source, int i, int line, int column, List<LanguageIssue> issues)
    {
        var rest = source.AsSpan(i);

        (Token?, int) Pipe(string written)
        {
            issues.Add(new LanguageIssue(line, column, IssueCode.NotAPipe, $"'{written}' is not a pipe. A pipe is written '|>'."));
            return (new Token(TokenKind.Pipe, written, line, column), written.Length);
        }

        (Token?, int) Lookalike(TokenKind kind, char plain)
        {
            issues.Add(new LanguageIssue(line, column, IssueCode.LookalikeCharacter,
                $"'{source[i]}' looks like '{plain}' but is not one. Write '{plain}'."));

            return (new Token(kind, plain.ToString(), line, column), 1);
        }

        if (rest.StartsWith("->", StringComparison.Ordinal)) return Pipe("->");
        if (rest.StartsWith("=>", StringComparison.Ordinal)) return Pipe("=>");
        if (rest[0] == '|' && !rest.StartsWith("|>", StringComparison.Ordinal)) return Pipe("|");

        switch (rest[0])
        {
            case '\u2192' or '\u21d2' or '\u25b6' or '\u00bb' or '\u27a4':
                return Pipe(rest[0].ToString());

            case '\u2212' or '\u2013' or '\u2014' or '\u2010' or '\u2011':
                return Lookalike(TokenKind.Minus, '-');

            case '\u00d7':
                return Lookalike(TokenKind.Star, '*');

            case '\u00f7':
                return Lookalike(TokenKind.Slash, '/');

            case ';':
                issues.Add(new LanguageIssue(line, column, IssueCode.Semicolon,
                    "';' is not needed: a statement ends with its line."));
                return (null, 1);

            case ']':
                issues.Add(new LanguageIssue(line, column, IssueCode.UnmatchedCloser,
                    "']' closes nothing: no '[' is open before it."));
                return (null, 1);

            default:
                return null;
        }
    }

    /// <summary>A character the language has no use for, named so it can be found even where it cannot be seen.</summary>
    private static LanguageIssue Stray(char c, int line, int column)
    {
        if (c is '\u2018' or '\u2019')
        {
            return new LanguageIssue(line, column, IssueCode.LookalikeCharacter,
                $"'{c}' is a curly quote. Text is written between straight double quotes: \"like this\".");
        }

        if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
        {
            return new LanguageIssue(line, column, IssueCode.StrayCharacter,
                string.Create(CultureInfo.InvariantCulture, $"an invisible character, U+{(int)c:X4}, means nothing here. Delete it."));
        }

        return new LanguageIssue(line, column, IssueCode.StrayCharacter, c == '>'
            ? "'>' means nothing here: nothing is compared, and a pipe is written '|>'."
            : $"'{c}' means nothing here.");
    }

    /// <summary>A name, or a note written as one.</summary>
    /// <remarks>
    /// The note is tried first, and it has to be: a sharp is spelled with the
    /// same character a comment starts with, so reading <c>C#4</c> as a name
    /// would leave <c>#4</c> to swallow the rest of the line. Nothing else in
    /// the language puts a <c>#</c> inside a word, so trying the narrow shape
    /// before the wide one costs nothing and settles it.
    /// </remarks>
    private static int Word(string source, int start, int line, int column, List<Token> tokens, List<LanguageIssue> issues)
    {
        if (Spelled(source, start) is var (note, after))
        {
            // Spelled like a note, and further from middle C than any note is named.
            if (note is null)
            {
                issues.Add(new LanguageIssue(line, column, IssueCode.MalformedNumber,
                    $"'{source[start..after]}' is too far from middle C to be a note. Write it as the number it is."));

                tokens.Add(new Token(TokenKind.Number, source[start..after], line, column));
                return after;
            }

            tokens.Add(new Token(TokenKind.Number, source[start..after], line, column, note.Value, NumberStyle.Note));
            return after;
        }

        var i = start;
        while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] == '_')) i++;

        tokens.Add(new Token(TokenKind.Identifier, source[start..i], line, column));
        return i;
    }

    /// <summary>
    /// The note beginning at <paramref name="start"/> and where it ends, or null
    /// where a name begins there instead. The note itself is null where the word
    /// has a note's shape and an octave no note is named in.
    /// </summary>
    private static (double? Note, int After)? Spelled(string source, int start)
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

        return (Note(source[start..i]), i);
    }

    /// <summary>
    /// The note a word spells, or null where it spells a name instead.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow: a capital A to G, an optional sharp or flat, then an
    /// octave — the octave being the part no ordinary name has. Both spellings
    /// are read although only sharps are written back
    /// (<see cref="Graph.Pitch.ClassName"/>), since refusing <c>Bb2</c> would be
    /// refusing a note over which of its two names it was given.
    /// </remarks>
    public static double? Note(string word)
    {
        const int MaxOctave = 1_000 / (int)Graph.Pitch.Semitones;

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

            // Past this Pitch.Name writes the number rather than a name, and the
            // digits would soon overflow.
            if (octave > MaxOctave) return null;
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
