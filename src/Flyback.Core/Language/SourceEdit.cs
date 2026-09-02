namespace Flyback.Core.Language;

/// <summary>One stretch of a source file, and what should stand there instead.</summary>
/// <remarks>
/// A span rather than a whole new file, so that whoever applies it can do so
/// without disturbing the caret, and so that taking it back is one thing to
/// undo rather than a document replaced.
/// </remarks>
public readonly record struct Change(int Offset, int Length, string Text);

/// <summary>
/// Small edits to a source file that leave it the file somebody wrote.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of printing, for the case printing cannot serve. A patch
/// written back out is a patch <em>rewritten</em> — the comments go, the
/// <c>def</c>s are expanded, the formatting is somebody else's — so a knob
/// turned in the panel cannot be answered by printing the patch again. It has to
/// be answered by changing the one number that moved.
/// </para>
/// <para>
/// Everything here is conservative and says so: where the text is not shaped the
/// way this expects, it hands back nothing rather than guessing, and the caller
/// falls back on a statement of its own. Being wrong here means editing somebody
/// else's line.
/// </para>
/// </remarks>
public static class SourceEdit
{
    /// <summary>
    /// Where a module's call already says what a knob is, or where the knob
    /// should be added to it — and null where neither can be found safely.
    /// </summary>
    /// <remarks>
    /// The call edited is the last one in the module's binding, because that is
    /// the one the binding names: <c>let hum = t |&gt; sine(...) |&gt; gain(...)</c>
    /// is a gain called hum, and the sine before it has no name at all.
    /// </remarks>
    /// <param name="module">The name the text binds it to.</param>
    /// <param name="port">The socket, as the language spells it.</param>
    /// <param name="value">The number, already written the way the language writes one.</param>
    /// <param name="source"></param>
    public static Change? Knob(string source, string module, string port, string value)
    {
        if (Statement(source, module) is not var (from, to)) return null;
        if (Call(source, from, to) is not var (open, close)) return null;

        // An argument with no name is filling a socket by position, and adding a
        // named one for the same socket would be saying it twice. Nothing here
        // knows which position is which port — that is the binder's — so a call
        // written that way is left alone.
        if (Positional(source, open, close)) return null;

        if (Argument(source, open, close, port) is var (at, length))
            return new Change(at, length, value);

        var inside = source[(open + 1)..close].Trim().Length > 0;

        return new Change(close, 0, (inside ? ", " : string.Empty) + port + ": " + value);
    }

    /// <summary>
    /// The statement binding <paramref name="module"/>, as a span of the source.
    /// </summary>
    /// <remarks>
    /// A statement may run over several lines, so its end is where the language
    /// says a statement ends: a line break that neither leaves something
    /// unfinished behind it nor has something carrying on in front of it. The
    /// two rules are the lexer's and are mirrored here against text rather than
    /// tokens — being wrong makes this find nothing, which is the safe way to be
    /// wrong.
    /// </remarks>
    private static (int From, int To)? Statement(string source, string module)
    {
        var lines = Lines(source);

        for (var i = 0; i < lines.Count; i++)
        {
            if (!Binds(source, lines[i], module)) continue;

            var to = lines[i].To;

            for (var j = i; j + 1 < lines.Count; j++)
            {
                var here = source[lines[j].From..lines[j].To].TrimEnd();
                var next = source[lines[j + 1].From..lines[j + 1].To].TrimStart();

                if (!Unfinished(here) && !Continues(next)) break;

                to = lines[j + 1].To;
            }

            return (lines[i].From, to);
        }

        return null;
    }

    /// <summary>Whether a line is <c>let NAME =</c> for this name and no other.</summary>
    private static bool Binds(string source, (int From, int To) line, string module)
    {
        var said = source[line.From..line.To].AsSpan().TrimStart();

        if (!said.StartsWith("let ")) return false;

        said = said[4..].TrimStart();

        if (!said.StartsWith(module)) return false;

        var rest = said[module.Length..].TrimStart();

        return rest.Length > 0 && rest[0] == '=';
    }

    /// <summary>
    /// The last call at the top level of a statement, which is the one the
    /// statement's name is bound to.
    /// </summary>
    private static (int Open, int Close)? Call(string source, int from, int to)
    {
        int? open = null;
        int? close = null;
        var depth = 0;

        for (var at = from; at < to; at++)
        {
            switch (source[at])
            {
                case '"':
                    while (++at < to && source[at] != '"') { }
                    continue;

                case '(' or '[':
                    if (depth == 0 && source[at] == '(') { open = at; close = null; }
                    depth++;
                    continue;

                case ')' or ']':
                    depth--;
                    if (depth == 0 && source[at] == ')' && close is null) close = at;
                    continue;
            }
        }

        return open is { } o && close is { } c && c > o ? (o, c) : null;
    }

    /// <summary>Whether any of a call's arguments fills its socket by position.</summary>
    private static bool Positional(string source, int open, int close)
    {
        foreach (var (from, to) in Split(source, open + 1, close))
        {
            var argument = source[from..to].AsSpan().Trim();

            if (argument.Length == 0) continue;

            // A name and then a colon, and nothing between them but the name. A
            // file path is the other thing a call may carry with no socket named
            // for it, and it is quoted.
            var end = 0;

            while (end < argument.Length
                && (char.IsAsciiLetterOrDigit(argument[end]) || argument[end] == '_')) end++;

            if (end == 0 || argument[end..].TrimStart() is not [':', ..]) return true;
        }

        return false;
    }

    /// <summary>Where a named argument's value stands, or nothing where it is not written.</summary>
    private static (int At, int Length)? Argument(string source, int open, int close, string port)
    {
        foreach (var (from, to) in Split(source, open + 1, close))
        {
            var said = source[from..to];
            var name = said.AsSpan().TrimStart();
            var lead = said.Length - name.Length;

            if (!name.StartsWith(port)) continue;

            var rest = name[port.Length..];
            var gap = rest.Length;

            rest = rest.TrimStart();

            if (rest is not [':', ..]) continue;

            var colon = from + lead + port.Length + (gap - rest.Length);
            var value = colon + 1;

            while (value < to && source[value] == ' ') value++;

            return (value, to - value);
        }

        return null;
    }

    /// <summary>Each argument between two brackets, as a span of the source.</summary>
    private static IEnumerable<(int From, int To)> Split(string source, int from, int to)
    {
        var depth = 0;
        var start = from;

        for (var at = from; at < to; at++)
        {
            switch (source[at])
            {
                case '"':
                    while (++at < to && source[at] != '"') { }
                    continue;

                case '(' or '[':
                    depth++;
                    continue;

                case ')' or ']':
                    depth--;
                    continue;

                case ',' when depth == 0:
                    yield return (start, at);
                    start = at + 1;
                    continue;
            }
        }

        if (start < to) yield return (start, to);
    }

    /// <summary>Every line of a source, as spans that leave the newlines out.</summary>
    private static IReadOnlyList<(int From, int To)> Lines(string source)
    {
        var lines = new List<(int, int)>();
        var from = 0;

        for (var at = 0; at <= source.Length; at++)
        {
            if (at < source.Length && source[at] != '\n') continue;

            var to = at > from && source[at - 1] == '\r' ? at - 1 : at;

            lines.Add((from, to));
            from = at + 1;
        }

        return lines;
    }

    /// <summary>Whether a line ending here cannot be a whole statement.</summary>
    /// <remarks>Mirrors <see cref="Lexer"/>'s rule, against text rather than tokens.</remarks>
    private static bool Unfinished(string line) =>
        line.Length > 0
        && (line.EndsWith("|>", StringComparison.Ordinal)
            || line.EndsWith("<-", StringComparison.Ordinal)
            || line[^1] is '=' or ',' or ':' or '.' or '(' or '{' or '+' or '-' or '*' or '/' or '%');

    /// <summary>Whether a line starting here is carrying on the one above.</summary>
    private static bool Continues(string line) =>
        line.StartsWith("|>", StringComparison.Ordinal)
        || (line.Length > 0
            && line[0] is ')' or ']' or '}' or '[' or '+' or '-' or '*' or '/' or '%');
}
