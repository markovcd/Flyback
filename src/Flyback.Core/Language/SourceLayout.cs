using System.Text;

namespace Flyback.Core.Language;

/// <summary>
/// Breaks long statements across lines, so a patch written out as text reads as
/// text rather than as one line per pipeline.
/// </summary>
/// <remarks>
/// Every break it makes is one <see cref="Lexer.Statements"/> joins back up — a
/// line ending in a comma or an open bracket cannot be a whole statement, and one
/// beginning with a pipe or a close bracket carries on the line above — so a
/// printing put through this builds to the same program.
/// <para>
/// A pipeline is long because it has stages and breaks before each <c>|&gt;</c>;
/// a call is long because it has arguments and breaks after each comma. The
/// second is reached only where the first was not enough.
/// </para>
/// </remarks>
public static class SourceLayout
{
    /// <summary>
    /// How wide a line may be before it is worth breaking: wide enough for the
    /// ordinary two-stage pipeline, narrow enough to sit beside the preview. A
    /// number rather than a measurement of the panel, or a patch's diff would
    /// move when somebody dragged a splitter.
    /// </summary>
    public const int Width = 88;

    /// <summary>How far a continuation is indented past the line it continues.</summary>
    private const int Step = 2;

    /// <summary>
    /// How many brackets deep this will break arguments before leaving a line
    /// long. Three: past that the indent costs more in width than it says about
    /// the shape.
    /// </summary>
    private const int Deepest = 3;

    /// <summary>The same source, with its long statements broken across lines.</summary>
    /// <param name="source">Anything the language reads. Comments and short lines come back untouched.</param>
    /// <param name="width">How wide a line may be, for a caller with a reason to differ.</param>
    public static string Wrap(string source, int width = Width)
    {
        if (source.Length == 0) return source;

        // Line by line, because a printing puts one statement on each. What this
        // must not do is join anything back up: somebody's own line breaks are
        // theirs.
        //
        // Joined rather than appended to, so a source ending in a newline ends in
        // exactly one.
        var folded = source
            .ReplaceLineEndings("\n")
            .Split('\n')
            .SelectMany(line => Fold(line, width, depth: 0));

        return string.Join('\n', folded);
    }

    /// <summary>One line, as however many it should be.</summary>
    private static IEnumerable<string> Fold(string line, int width, int depth)
    {
        if (line.Length <= width || Commented(line)) return [line];

        var stages = Staged(line);

        // A pipeline with stages breaks between them, and each stage is then
        // asked the same question again — a single stage may still be a call
        // with twenty arguments in it.
        if (stages.Count > 0)
        {
            // The first stage stays with what it is reading, which is how every
            // pipeline in the handbook is written: `x |> sine(freq: 1.5)` and
            // then one stage a line under it. A source with a bare `x` on the
            // first line would be a break made where nothing needed breaking.
            // Only where there is a second stage to break at, though — with one
            // pipe and a line still too long, that pipe is the only break there
            // is.
            var from = stages.Count > 1 ? 1 : 0;

            var indent = new string(' ', Indent(line) + Step);
            var folded = new List<string>(Fold(line[..stages[from]].TrimEnd(), width, depth));

            for (var i = from; i < stages.Count; i++)
            {
                var to = i + 1 < stages.Count ? stages[i + 1] : line.Length;

                folded.AddRange(Fold(indent + line[stages[i]..to].Trim(), width, depth));
            }

            return folded;
        }

        // A tune before a call's arguments, because a line carrying one is a
        // sequencer's and the tune is what is long about it.
        return Steps(line, width) ?? Arguments(line, width, depth);
    }

    /// <summary>
    /// One call, as one line per argument, or the line as it stands where there
    /// is nothing to gain by it.
    /// </summary>
    private static IEnumerable<string> Arguments(string line, int width, int depth)
    {
        if (depth >= Deepest) return [line];
        if (Opening(line, '(') is not { } open) return [line];
        if (Closing(line, open, ')') is not { } close) return [line];

        var inner = Split(line, open + 1, close);

        // One argument is still worth breaking out, because the thing making the
        // line long is usually inside it — a single `b:` carrying a pipeline of
        // its own. A call with none is a call there is nothing to break.
        if (inner.Count == 0) return [line];

        var indent = new string(' ', Indent(line) + Step * 2);
        var folded = new List<string> { line[..(open + 1)] };

        for (var i = 0; i < inner.Count; i++)
        {
            var (from, to) = inner[i];
            var last = i == inner.Count - 1;
            var argument = line[from..to].Trim();

            // The comma goes at the end of the line it belongs to, which is what
            // makes the lexer read the next one as a continuation. The last
            // argument carries whatever followed the call instead.
            folded.AddRange(Fold(indent + argument + (last ? line[close..] : ","), width, depth + 1));
        }

        return folded;
    }

    /// <summary>
    /// Where a pipeline may be broken: every <c>|&gt;</c> that is not inside
    /// something, except one the line already begins with.
    /// </summary>
    /// <remarks>
    /// Depth counts brackets of both kinds and text is skipped whole, since a pipe
    /// inside a file name is not a pipe. Skipping the leading one is load-bearing:
    /// a stage broken out of a pipeline begins with the pipe that broke it, and
    /// breaking there again would recur for ever.
    /// </remarks>
    private static IReadOnlyList<int> Staged(string line)
    {
        var starts = new List<int>();
        var first = Indent(line);

        Walk(line, 0, line.Length, (at, depth) =>
        {
            if (at == first) return;
            if (depth != 0 || at + 1 >= line.Length) return;
            if (line[at] != '|' || line[at + 1] != '>') return;

            starts.Add(at);
        });

        return starts;
    }

    /// <summary>The first bracket of its kind the line opens, or null where it opens none.</summary>
    private static int? Opening(string line, char bracket)
    {
        int? found = null;

        Walk(line, 0, line.Length, (at, depth) =>
        {
            if (found is null && depth == 0 && line[at] == bracket) found = at;
        });

        return found;
    }

    /// <summary>Where the bracket opened at <paramref name="open"/> closes.</summary>
    private static int? Closing(string line, int open, char bracket)
    {
        int? found = null;

        Walk(line, open, line.Length, (at, depth) =>
        {
            if (found is null && depth == 1 && line[at] == bracket) found = at;
        });

        return found;
    }

    /// <summary>
    /// A tune, filled across as many lines as it takes — or null where the line
    /// carries no block, or one with nothing in it.
    /// </summary>
    /// <remarks>
    /// The one break here that is not the lexer joining statements up: a block is
    /// a single token scanned to its closing bracket, and its steps are separated
    /// by whitespace of any kind. <see cref="Lexer"/> counts the newlines it
    /// swallows, which keeps a complaint after a long tune on the right line.
    /// Filled rather than one step per line, because a tune is read as a run.
    /// </remarks>
    private static IReadOnlyList<string>? Steps(string line, int width)
    {
        if (Opening(line, '[') is not { } open) return null;
        if (Closing(line, open, ']') is not { } close) return null;

        var steps = line[(open + 1)..close].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (steps.Length < 2) return null;

        var indent = new string(' ', Indent(line) + Step * 2);
        var folded = new List<string> { line[..(open + 1)].TrimEnd() };
        var row = new StringBuilder(indent);

        foreach (var step in steps)
        {
            if (row.Length > indent.Length && row.Length + 1 + step.Length > width)
            {
                folded.Add(row.ToString());
                row.Clear().Append(indent);
            }

            if (row.Length > indent.Length) row.Append(' ');

            row.Append(step);
        }

        // Whatever followed the block goes with the last step, which is the
        // closing bracket and nothing else in anything the printer writes.
        folded.Add(row.Append(' ').Append(line[close..]).ToString());

        return folded;
    }

    /// <summary>Each argument between two brackets, as a span of the line.</summary>
    private static IReadOnlyList<(int From, int To)> Split(string line, int from, int to)
    {
        var parts = new List<(int, int)>();
        var start = from;

        Walk(line, from, to, (at, depth) =>
        {
            if (depth != 0 || line[at] != ',') return;

            parts.Add((start, at));
            start = at + 1;
        });

        if (start < to) parts.Add((start, to));

        return parts;
    }

    /// <summary>
    /// Walks a stretch of a line, handing each character its bracket depth and
    /// skipping over anything quoted. The depth is the one before the character,
    /// so an opening bracket is seen at the depth it opens from. Both kinds count:
    /// a step block holds commas that are the block's own.
    /// </summary>
    private static void Walk(string line, int from, int to, Action<int, int> visit)
    {
        var depth = 0;
        var quoted = false;

        for (var at = from; at < to; at++)
        {
            var c = line[at];

            if (quoted)
            {
                if (c == '"') quoted = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    continue;

                // The rest of the line is a comment, which has no structure to
                // find and must never be broken — unless the hash is a sharp.
                // A note is spelled `C#4`, and the lexer reads that as one word
                // because it begins on a letter; a hash that begins a comment is
                // one nothing runs into. Being wrong the other way would leave a
                // line long, which is the safe direction to be wrong in.
                case '#' when at == from || !char.IsAsciiLetterOrDigit(line[at - 1]):
                    return;

                case '(' or '[':
                    visit(at, depth);
                    depth++;
                    continue;

                case ')' or ']':
                    depth--;
                    visit(at, depth + 1);
                    continue;

                default:
                    visit(at, depth);
                    continue;
            }
        }
    }

    /// <summary>Whether a line is nothing but a comment, which cannot be broken anywhere.</summary>
    private static bool Commented(string line) => line.TrimStart().StartsWith('#');

    private static int Indent(string line) => line.Length - line.TrimStart().Length;
}
