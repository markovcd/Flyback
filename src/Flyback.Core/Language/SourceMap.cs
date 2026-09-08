namespace Flyback.Core.Language;

/// <summary>Where something stands in a source file, counting from one as an editor does.</summary>
public readonly record struct Site(int Line, int Column);

/// <summary>One stretch of a source file, and what should stand there instead.</summary>
/// <remarks>
/// A span rather than a whole new file, so that applying it leaves the caret
/// where it was and is one thing to take back.
/// </remarks>
public readonly record struct Change(int Offset, int Length, string Text);

/// <summary>
/// Which module each piece of a source file is about, and where the file writes
/// its knobs.
/// </summary>
/// <remarks>
/// <para>
/// The link between a caret and a module, and between a knob and the number the
/// text already has for it. Both are answered by position rather than by name,
/// which is what lets them work on the text people write rather than on a
/// dialect of it: the module in <c>atan2(a: 1.5) |&gt; out.left</c> is called
/// nothing at all, and a scheme that needed a name would have to invent one and
/// write it into somebody's file.
/// </para>
/// <para>
/// The positions come from whoever made the text — <see cref="Binder"/> for a
/// file that was built, <see cref="PatchPrinter"/> for one that was written out
/// — because only they know which module ended up where. What is done here is
/// the half neither of them has: turning a line and a column into an offset and
/// a length, which takes the text again.
/// </para>
/// <para>
/// Conservative wherever the text is not shaped for it, and it hands back
/// nothing rather than guessing. Being wrong here means editing somebody else's
/// line.
/// </para>
/// </remarks>
public sealed class SourceMap
{
    /// <summary>A map of no text, which points at nothing and rewrites nothing.</summary>
    public static readonly SourceMap Empty = new(
        string.Empty,
        [],
        new Dictionary<Guid, Site>(),
        new Dictionary<(Guid, int), Site?>(),
        new Dictionary<Guid, string>(),
        new HashSet<Guid>());

    private readonly string source;

    /// <summary>Where each line begins, so that a line and a column make an offset.</summary>
    private readonly int[] starts;

    private readonly IReadOnlyList<Token> tokens;

    /// <summary>Which token begins at an offset, for the walks below.</summary>
    private readonly Dictionary<int, int> beginning = [];

    /// <summary>The call that placed each module, where the text has one.</summary>
    private readonly Dictionary<Guid, Site> calls;

    /// <summary>
    /// Where each knob's number stands. Null for one the text does say and this
    /// cannot rewrite — <c>1/12</c> is a knob worked out from two numbers, and
    /// there is no single figure in the file to put another in place of.
    /// </summary>
    private readonly Dictionary<(Guid Node, int Port), Site?> knobs;

    /// <summary>What the text calls each module it has a word for.</summary>
    private readonly Dictionary<Guid, string> named;

    /// <summary>Every place a module is named, and how much of the text that covers.</summary>
    private readonly List<(int From, int To, Guid Node)> spans = [];

    /// <summary>The statement each binding owns, which is what its name points at.</summary>
    private readonly List<(int From, int To, Guid Node)> bindings = [];

    /// <summary>
    /// Modules written in one place and made in several, which is what a
    /// <c>def</c> stamped out twice gives. They can be pointed at and not
    /// written to: the one line means every one of them.
    /// </summary>
    private readonly HashSet<Guid> shared = [];

    internal SourceMap(
        string source,
        IReadOnlyList<(Site Where, Guid Node)> mentions,
        IReadOnlyDictionary<Guid, Site> calls,
        IReadOnlyDictionary<(Guid Node, int Port), Site?> knobs,
        IReadOnlyDictionary<Guid, string> named,
        IReadOnlySet<Guid> bound)
    {
        this.source = source;
        this.calls = new Dictionary<Guid, Site>(calls);
        this.knobs = new Dictionary<(Guid, int), Site?>(knobs);
        this.named = new Dictionary<Guid, string>(named);

        starts = Starts(source);
        tokens = source.Length == 0 ? [] : Lexer.Scan(source, []);

        for (var i = 0; i < tokens.Count; i++) beginning.TryAdd(Offset(tokens[i]), i);

        var counted = new Dictionary<Site, Guid>();

        foreach (var (where, node) in mentions)
        {
            if (Extent(where) is not { } extent) continue;

            spans.Add((extent.From, extent.To, node));

            // One call standing for several modules is a def expanded more than
            // once. Pointing at it is honest; changing it is not, since the
            // number in that line is every one of them.
            if (counted.TryGetValue(where, out var first) && first != node)
            {
                shared.Add(first);
                shared.Add(node);
            }

            counted.TryAdd(where, node);
        }

        Bindings(bound);
    }

    /// <summary>
    /// The module the text at <paramref name="offset"/> is about, or null where
    /// it is about none.
    /// </summary>
    /// <remarks>
    /// The innermost, because a call inside a call is what an argument is:
    /// <c>mix(a: sine(), b: saw())</c> is three modules, and the caret in the
    /// middle of <c>sine()</c> means the Sine. Failing that the statement, which
    /// is what makes clicking the name in <c>let hum = t |&gt; gain(0.5)</c>
    /// select the Gain the name is for.
    /// </remarks>
    public Guid? At(int offset)
    {
        Guid? found = null;
        var narrowest = int.MaxValue;

        foreach (var (from, to, node) in spans)
        {
            if (offset < from || offset > to || to - from >= narrowest) continue;

            found = node;
            narrowest = to - from;
        }

        if (found is not null) return found;

        foreach (var (from, to, node) in bindings)
            if (offset >= from && offset <= to) return node;

        return null;
    }

    /// <summary>Where a module is named, for pointing the other way.</summary>
    public (int From, int To)? Where(Guid node)
    {
        foreach (var (from, to, found) in spans)
            if (found == node) return (from, to);

        return null;
    }

    /// <summary>
    /// The edit that makes the text say <paramref name="value"/> for a knob, or
    /// null where the text cannot be made to say it safely.
    /// </summary>
    /// <remarks>
    /// The number is changed where it already stands. A knob sitting at its
    /// default is written nowhere, so it is added to the call that placed the
    /// module — rather than said a second time further down, which would leave
    /// the file asserting two different values for one socket with the older one
    /// still written a few lines up.
    /// </remarks>
    /// <param name="node">The module the knob is on.</param>
    /// <param name="port">The socket's index, which is how a knob is keyed.</param>
    /// <param name="name">The socket, as the language spells it.</param>
    /// <param name="value">The number, already written the way the language writes one.</param>
    public Change? Knob(Guid node, int port, string name, string value)
    {
        if (shared.Contains(node)) return null;

        if (knobs.TryGetValue((node, port), out var written))
        {
            // Null is for a knob this cannot write, and only for that — a value
            // the text already says comes back as the edit that would say it, so
            // that a caller can tell "nothing to do" from "nowhere to put it".
            return written is { } site && Figure(site) is { } span
                ? new Change(span.From, span.Length, value)
                : null;
        }

        if (calls.TryGetValue(node, out var call) && Parentheses(call) is { } brackets)
        {
            var inside = source.AsSpan(brackets.Open + 1, brackets.Close - brackets.Open - 1).Trim().Length > 0;

            return new Change(brackets.Close, 0, (inside ? ", " : string.Empty) + name + ": " + value);
        }

        // The Output is the module no call ever places — every patch already has
        // one, so the language names it rather than writing it. Its knobs are
        // said by the statement the language has for saying one, at the end
        // because that is the one place a module is certain to exist already.
        if (!named.TryGetValue(node, out var word)) return null;

        var end = source.TrimEnd('\n', '\r').Length;

        return new Change(end, source.Length - end, $"\n{word}.{name} = {value}\n");
    }

    /// <summary>The offset a line and a column name, clamped to the text.</summary>
    private int Offset(Site site)
    {
        var line = Math.Clamp(site.Line, 1, starts.Length);

        return Math.Clamp(starts[line - 1] + site.Column - 1, 0, source.Length);
    }

    private int Offset(Token token) => Offset(new Site(token.Line, token.Column));

    /// <summary>
    /// How much of the text a module's name covers: the name itself, its
    /// brackets and whatever is between them, and the block after them where
    /// there is one.
    /// </summary>
    private (int From, int To)? Extent(Site site)
    {
        var from = Offset(site);

        if (!beginning.TryGetValue(from, out var i) || tokens[i].Kind != TokenKind.Identifier) return null;

        var to = from + tokens[i].Text.Length;
        i++;

        // 'space.rotate' and 'out.left' are each a name in two words, and the
        // second word is as much the thing clicked as the first.
        while (i + 1 < tokens.Count
            && tokens[i].Kind == TokenKind.Dot
            && tokens[i + 1].Kind == TokenKind.Identifier)
        {
            to = Offset(tokens[i + 1]) + tokens[i + 1].Text.Length;
            i += 2;
        }

        if (i >= tokens.Count || tokens[i].Kind != TokenKind.OpenParen) return (from, to);

        var depth = 0;

        for (; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.OpenParen)
            {
                depth++;
            }
            else if (tokens[i].Kind == TokenKind.CloseParen && --depth == 0)
            {
                to = Offset(tokens[i]) + 1;
                i++;
                break;
            }
        }

        if (i < tokens.Count && tokens[i].Kind == TokenKind.Block) to = Closed(Offset(tokens[i]));

        return (from, to);
    }

    /// <summary>The brackets of the call standing at <paramref name="site"/>.</summary>
    private (int Open, int Close)? Parentheses(Site site)
    {
        if (!beginning.TryGetValue(Offset(site), out var i)) return null;

        var open = -1;
        var depth = 0;

        for (; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.OpenParen)
            {
                if (depth++ == 0) open = Offset(tokens[i]);
            }
            else if (tokens[i].Kind == TokenKind.CloseParen && --depth == 0)
            {
                return (open, Offset(tokens[i]));
            }
            else if (depth == 0 && tokens[i].Kind is not (TokenKind.Identifier or TokenKind.Dot))
            {
                // A name with nothing after it is a module written without
                // brackets, which has nowhere to put an argument.
                return null;
            }
        }

        return null;
    }

    /// <summary>The number written at <paramref name="site"/>, with its sign.</summary>
    /// <remarks>
    /// The minus is part of it. Putting 0.5 where the digits of <c>-0.5</c> are
    /// would leave the file still saying <c>-0.5</c>, which is the one wrong
    /// answer this could give.
    /// </remarks>
    private (int From, int Length)? Figure(Site site)
    {
        var from = Offset(site);

        if (!beginning.TryGetValue(from, out var i)) return null;

        while (i < tokens.Count && tokens[i].Kind == TokenKind.Minus) i++;

        if (i >= tokens.Count || tokens[i].Kind != TokenKind.Number) return null;

        return (from, Offset(tokens[i]) + tokens[i].Text.Length - from);
    }

    /// <summary>Where the block opening at <paramref name="open"/> ends.</summary>
    private int Closed(int open)
    {
        var depth = 0;

        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '[') depth++;
            else if (source[i] == ']' && --depth == 0) return i + 1;
        }

        return source.Length;
    }

    /// <summary>
    /// The statement each binding owns, worked out from the text rather than
    /// recorded with it, so that where a statement ends is one rule and the
    /// lexer's.
    /// </summary>
    private void Bindings(IReadOnlySet<Guid> bound)
    {
        if (bound.Count == 0 || tokens.Count == 0) return;

        var statements = new List<(int From, int To)>();
        var from = -1;

        foreach (var token in Lexer.Statements(tokens))
        {
            if (token.Kind is TokenKind.NewLine or TokenKind.End)
            {
                if (from >= 0) statements.Add((from, Offset(token)));
                from = -1;
                continue;
            }

            if (from < 0) from = Offset(token);
        }

        foreach (var node in bound)
        {
            if (!calls.TryGetValue(node, out var site)) continue;

            var at = Offset(site);

            foreach (var (start, end) in statements)
            {
                if (at < start || at > end) continue;

                bindings.Add((start, end, node));
                break;
            }
        }
    }

    private static int[] Starts(string source)
    {
        var found = new List<int> { 0 };

        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n') found.Add(i + 1);

        return [.. found];
    }
}
