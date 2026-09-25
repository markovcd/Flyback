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
/// Both are answered by position rather than by name, which is what lets them
/// work on the text people write: the module in
/// <c>atan2(a: 1.5) |&gt; out.left</c> is called nothing at all, and a scheme
/// needing a name would have to write one into somebody's file. The positions
/// come from whoever made the text — <see cref="Binder"/> or
/// <see cref="PatchPrinter"/> — and what is done here is turning a line and a
/// column into an offset and a length. Conservative wherever the text is not
/// shaped for it, since being wrong here means editing somebody else's line.
/// </remarks>
public sealed class SourceMap
{
    /// <summary>A map of no text, which points at nothing and rewrites nothing.</summary>
    public static readonly SourceMap Empty = new(
        string.Empty,
        [],
        new Dictionary<Guid, Site>(),
        new Dictionary<(Guid, string), Site?>(),
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
    /// Where the value of each named thing stands — a socket's knob, or one of a
    /// plugin's declared fields, which the language spells the same way.
    /// </summary>
    /// <remarks>
    /// Null for one the text does say and this cannot rewrite: <c>1/12</c> is a
    /// knob worked out from two numbers, and there is no single figure in the
    /// file to put another in place of.
    /// </remarks>
    private readonly Dictionary<(Guid Node, string Name), Site?> written;

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

    /// <summary>
    /// Each group's block: its header up to the brace that opens it, and the
    /// brace that closes it.
    /// </summary>
    private readonly List<(int From, int Opened, int Closed, int To, Guid Group)> blocks = [];

    /// <param name="boxes">Where each <c>group</c> block begins, and the group it is.</param>
    internal SourceMap(
        string source,
        IReadOnlyList<(Site Where, Guid Node)> mentions,
        IReadOnlyDictionary<Guid, Site> calls,
        IReadOnlyDictionary<(Guid Node, string Name), Site?> written,
        IReadOnlyDictionary<Guid, string> named,
        IReadOnlySet<Guid> bound,
        IReadOnlyList<(Site Where, Guid Group)>? boxes = null)
    {
        this.source = source;
        this.calls = new Dictionary<Guid, Site>(calls);
        this.named = new Dictionary<Guid, string>(named);

        // Keyed on the word rather than on a socket's position, because a
        // plugin's field has no position — and folded, because the language
        // reads a socket's name without minding its case.
        this.written = [];

        foreach (var (key, site) in written) this.written[Folded(key.Node, key.Name)] = site;

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
        Blocks(boxes ?? []);
    }

    /// <summary>
    /// The group the text at <paramref name="offset"/> is about: on a block's
    /// header or its closing brace, and anywhere inside it that is about no
    /// module. Null elsewhere, and on a module inside a block, which is about
    /// that module.
    /// </summary>
    public Guid? GroupAt(int offset)
    {
        foreach (var (from, opened, closed, to, group) in blocks.OrderBy(block => block.To - block.From))
        {
            if (offset < from || offset > to) continue;

            if (offset <= opened || offset >= closed) return group;

            return At(offset) is null ? group : null;
        }

        return null;
    }

    private void Blocks(IReadOnlyList<(Site Where, Guid Group)> boxes)
    {
        foreach (var (where, group) in boxes)
        {
            var from = Offset(where);

            if (!beginning.TryGetValue(from, out var i)) continue;

            var open = i;

            while (open < tokens.Count && tokens[open].Kind != TokenKind.OpenBrace) open++;

            if (open >= tokens.Count) continue;

            var depth = 0;
            var close = open;

            for (; close < tokens.Count; close++)
            {
                if (tokens[close].Kind == TokenKind.OpenBrace) depth++;
                else if (tokens[close].Kind == TokenKind.CloseBrace && --depth == 0) break;
            }

            var opened = Offset(tokens[open]) + 1;
            var closed = close < tokens.Count ? Offset(tokens[close]) : source.Length;

            blocks.Add((from, opened, closed, Math.Min(closed + 1, source.Length), group));
        }
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
    /// <param name="node">The module the value is on.</param>
    /// <param name="name">
    /// The socket or the field's key, as the language spells it. One namespace,
    /// because the language has one: a call's named arguments are its sockets
    /// and its fields together, and the binder looks for a socket first.
    /// </param>
    /// <param name="value">Already written the way the language writes one.</param>
    public Change? Knob(Guid node, string name, string value)
    {
        if (shared.Contains(node)) return null;

        if (written.TryGetValue(Folded(node, name), out var said))
        {
            // Null is for a value this cannot write, and only for that — one the
            // text already says comes back as the edit that would say it, so
            // that a caller can tell "nothing to do" from "nowhere to put it".
            return said is { } site && Value(site) is { } span
                ? new Change(span.From, span.Length, value)
                : null;
        }

        if (calls.TryGetValue(node, out var call) && Parentheses(call) is { } brackets)
            return Argument(brackets, name + ": " + value, first: false);

        // The Output is the module no call ever places — every patch already has
        // one, so the language names it rather than writing it. Its knobs are
        // said by the statement the language has for saying one, at the end
        // because that is the one place a module is certain to exist already.
        if (!named.TryGetValue(node, out var word)) return null;

        var end = source.TrimEnd('\n', '\r').Length;

        return new Change(end, source.Length - end, $"\n{word}.{name} = {value}\n");
    }

    /// <summary>
    /// The edit that makes the text carry <paramref name="block"/> — a tune or a
    /// scale — for a module, or null where the text has nowhere to put one.
    /// </summary>
    /// <remarks>
    /// A block goes after the call rather than inside it, and there is at most
    /// one, so it needs no name and this needs nothing recorded about it: the
    /// token after the brackets either is one or is not.
    /// </remarks>
    /// <param name="block">Including its own brackets, and <c>[ ]</c> for nothing.</param>
    public Change? Carried(Guid node, string block)
    {
        if (shared.Contains(node)) return null;
        if (!calls.TryGetValue(node, out var call) || Parentheses(call) is not { } brackets) return null;

        return Block(brackets.Close) is { } span
            ? new Change(span.From, span.To - span.From, block)
            : new Change(brackets.Close + 1, 0, " " + block);
    }

    /// <summary>
    /// The edit that makes the text name <paramref name="path"/> as a module's
    /// file, or null where it has nowhere to put one.
    /// </summary>
    /// <remarks>
    /// First among the arguments, which is where a printing puts it and where it
    /// reads: it is not a socket, so it goes in without a name and the binder
    /// takes the one string a call has as the file it names (ADR-0052).
    /// </remarks>
    public Change? File(Guid node, string path)
    {
        if (shared.Contains(node) || path.Contains('"')) return null;
        if (!calls.TryGetValue(node, out var call) || Parentheses(call) is not { } brackets) return null;

        var quoted = $"\"{path}\"";

        return Text(brackets) is { } span
            ? new Change(span.From, span.Length, quoted)
            : Argument(brackets, quoted, first: true);
    }

    /// <summary>
    /// The edit that makes the text lay the computer keyboard out as
    /// <paramref name="line"/> says, or null where the text already does.
    /// </summary>
    /// <remarks>
    /// Found from the tokens rather than recorded by the binder, because the line
    /// is about no module and so nothing else here would know where it is. The
    /// first one standing at the start of a statement is the one the binder
    /// obeys. With no line to put there — back to a piano — it is taken out, and
    /// with none to replace a new one goes at the top, where a printing puts it.
    /// </remarks>
    /// <param name="line">What <see cref="PatchPrinter.Keyboard"/> writes, and null for a piano.</param>
    public Change? Keyboard(string? line) => PatchLine("keyboard", TokenKind.Identifier, line);

    /// <summary>
    /// The edit that makes the text describe the patch as <paramref name="line"/>
    /// says, or null where the text already does.
    /// </summary>
    /// <param name="line">What <see cref="PatchPrinter.Description"/> writes, and null for none.</param>
    public Change? Description(string? line) => PatchLine("description", TokenKind.Text, line);

    /// <summary>
    /// The edit that makes the text credit the patch as <paramref name="line"/>
    /// says, or null where the text already does. A new one goes under the description.
    /// </summary>
    /// <param name="line">What <see cref="PatchPrinter.Author"/> writes, and null for none.</param>
    public Change? Author(string? line) => PatchLine("author", TokenKind.Text, line, "description");

    /// <summary>
    /// The edit that makes the text tag the patch as <paramref name="line"/>
    /// says, or null where the text already does. A new one goes under the
    /// author, or under the description where nobody is credited.
    /// </summary>
    /// <param name="line">What <see cref="PatchPrinter.Tags"/> writes, and null for none.</param>
    public Change? Tags(string? line) => PatchLine("tags", TokenKind.Text, line, "author", "description");

    /// <summary>
    /// The edit that makes the text give the patch the length <paramref name="line"/>
    /// says, or null where the text already does. A new one goes under the tags,
    /// the author or the description, whichever comes last.
    /// </summary>
    /// <param name="line">What <see cref="PatchPrinter.Length"/> writes, and null for none.</param>
    public Change? Length(string? line) => PatchLine("length", TokenKind.Number, line, "tags", "author", "description");

    /// <summary>
    /// The edit that puts <paramref name="line"/> where the text says the thing
    /// <paramref name="word"/> opens, or takes that out for a null line. Where the
    /// text says nothing, the line goes under the first of <paramref name="under"/>
    /// it does say, and at the top where it says none of them.
    /// </summary>
    /// <param name="next">What follows the word in a statement of this kind.</param>
    private Change? PatchLine(string word, TokenKind next, string? line, params string[] under)
    {
        if (Opened(word, next) is { } span)
        {
            if (line is not null)
                return source[span.From..span.To] == line ? null : new Change(span.From, span.To - span.From, line);

            // The line and the break after it, so taking it out leaves no gap.
            var end = span.To;
            while (end < source.Length && source[end] is ' ' or '\t') end++;
            if (end < source.Length && source[end] == '\r') end++;
            if (end < source.Length && source[end] == '\n') end++;

            return new Change(span.From, end - span.From, string.Empty);
        }

        if (line is null) return null;

        foreach (var above in under)
        {
            if (Opened(above, TokenKind.Text) is { } before) return new Change(before.To, 0, "\n" + line);
        }

        return new Change(0, 0, line + "\n\n");
    }

    /// <summary>
    /// Where the first statement <paramref name="word"/> opens stands, and null
    /// where the text has none.
    /// </summary>
    private (int From, int To)? Opened(string word, TokenKind next)
    {
        (int From, int To)? found = null;
        var start = true;

        for (var i = 0; i < tokens.Count && found is null; i++)
        {
            var token = tokens[i];

            if (start
                && token.Kind == TokenKind.Identifier
                && token.Text == word
                && i + 1 < tokens.Count
                && tokens[i + 1].Kind == next)
            {
                var after = tokens[i + 1];

                // A string's token holds what is between its quotes.
                var to = Offset(after) + after.Text.Length + (next == TokenKind.Text ? 2 : 0);

                if (i + 2 < tokens.Count && tokens[i + 2].Kind == TokenKind.Block) to = Closed(Offset(tokens[i + 2]));

                // A length in minutes runs on over its colon and its seconds.
                if (next == TokenKind.Number && i + 3 < tokens.Count && tokens[i + 2].Kind == TokenKind.Colon && tokens[i + 3].Kind == TokenKind.Number)
                    to = Offset(tokens[i + 3]) + tokens[i + 3].Text.Length;

                // A description runs on over the strings on the lines below it, and
                // tags are a string each.
                for (var j = i + 2; next == TokenKind.Text && j < tokens.Count; j++)
                {
                    if (tokens[j].Kind == TokenKind.NewLine) continue;
                    if (tokens[j].Kind != TokenKind.Text) break;

                    to = Offset(tokens[j]) + tokens[j].Text.Length + 2;
                }

                found = (Offset(token), to);
            }

            start = token.Kind is TokenKind.NewLine or TokenKind.OpenBrace;
        }

        return found;
    }

    /// <summary>Each panel knob the text declares, by the id the binder gives it, and the word it is called by.</summary>
    public IReadOnlyDictionary<Guid, string> PanelWords =>
        Panels().ToDictionary(panel => Binder.PanelId(panel.Word), panel => panel.Word);

    /// <summary>
    /// The edit that makes the text's panel knobs the ones <paramref name="lines"/>
    /// says, in that order, or null where they already are.
    /// </summary>
    /// <remarks>
    /// Each statement is rewritten where it stands, and whatever is between two of
    /// them stays, so a comment or a group between knobs is kept. One line fewer
    /// takes out the last statement and its line break; one more goes after the
    /// last, or at the top where there is none.
    /// </remarks>
    /// <param name="lines">What <see cref="PatchPrinter.PanelLine"/> writes, one per knob.</param>
    public Change? Panel(IReadOnlyList<string> lines)
    {
        var found = Panels();

        if (found.Count == 0)
        {
            if (lines.Count == 0) return null;

            var joined = string.Join('\n', lines);

            return Statements().FirstOrDefault(statement => statement.Word == "requires") is { Word: not null } requires
                ? new Change(requires.To, 0, "\n\n" + joined)
                : new Change(0, 0, joined + "\n\n");
        }

        var text = new System.Text.StringBuilder();

        for (var i = 0; i < found.Count; i++)
        {
            var gap = i == 0 ? string.Empty : source[found[i - 1].To..found[i].From];

            if (i < lines.Count) text.Append(gap).Append(lines[i]);

            // Gone, with the break that led to it — or, with nothing kept before
            // it, the break that followed the one before.
            else if (lines.Count > 0) text.Append(gap.TrimEnd(' ', '\t').TrimEnd('\n').TrimEnd('\r'));
            else text.Append(Unbroken(gap));
        }

        for (var i = found.Count; i < lines.Count; i++) text.Append('\n').Append(lines[i]);

        var from = found[0].From;
        var to = found[^1].To;

        if (lines.Count == 0) to = source.Length - Unbroken(source[to..]).Length;

        var said = text.ToString();

        return source[from..to] == said ? null : new Change(from, to - from, said);
    }

    /// <summary>Text without the line break it opens with.</summary>
    private static string Unbroken(string text)
    {
        var at = 0;

        while (at < text.Length && text[at] is ' ' or '\t') at++;
        if (at < text.Length && text[at] == '\r') at++;
        if (at < text.Length && text[at] == '\n') at++;

        return text[at..];
    }

    /// <summary>Every <c>panel</c> statement, with the word it declares and where it stands.</summary>
    private List<(string Word, int From, int To)> Panels() =>
    [
        .. Statements()
            .Where(statement => statement.Word == "panel" && statement.Declares is not null)
            .Select(statement => (statement.Declares!, statement.From, statement.To)),
    ];

    /// <summary>
    /// Every statement: the word it opens with, the name an <c>=</c> after that
    /// declares where there is one, and where it stands.
    /// </summary>
    private List<(string? Word, string? Declares, int From, int To)> Statements()
    {
        var found = new List<(string? Word, string? Declares, int From, int To)>();
        var statements = Lexer.Statements(tokens);

        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i].Kind is TokenKind.NewLine or TokenKind.OpenBrace or TokenKind.CloseBrace) continue;

            var first = i;

            while (i + 1 < statements.Count && statements[i + 1].Kind is not (TokenKind.NewLine or TokenKind.OpenBrace or TokenKind.CloseBrace)) i++;

            var end = statements[i];
            var word = statements[first].Kind == TokenKind.Identifier ? statements[first].Text : null;

            var declares = first + 2 <= i
                && statements[first + 1].Kind == TokenKind.Identifier
                && statements[first + 2].Kind == TokenKind.Assign
                    ? statements[first + 1].Text
                    : null;

            found.Add((word, declares, Offset(statements[first]), Offset(end) + end.Text.Length + (end.Kind == TokenKind.Text ? 2 : 0)));
        }

        return found;
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

        if (!beginning.TryGetValue(from, out var i)) return null;

        // A sum is placed at the operator that joins it, and the operator is what
        // stands for it: what is either side is its operands, each somewhere to
        // click of its own.
        if (tokens[i].Kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
            return (from, from + tokens[i].Text.Length);

        if (tokens[i].Kind != TokenKind.Identifier) return null;

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

    /// <summary>The value written at <paramref name="site"/>, with its sign or its quotes.</summary>
    /// <remarks>
    /// The minus is part of a number. Putting 0.5 where the digits of
    /// <c>-0.5</c> are would leave the file still saying <c>-0.5</c>, which is
    /// the one wrong answer this could give. The quotes are part of a string for
    /// the same reason, and because what replaces one carries its own.
    /// </remarks>
    private (int From, int Length)? Value(Site site)
    {
        var from = Offset(site);

        if (!beginning.TryGetValue(from, out var i)) return null;

        if (tokens[i].Kind == TokenKind.Text) return (from, tokens[i].Text.Length + 2);

        while (i < tokens.Count && tokens[i].Kind == TokenKind.Minus) i++;

        if (i >= tokens.Count || tokens[i].Kind != TokenKind.Number) return null;

        return (from, Offset(tokens[i]) + tokens[i].Text.Length - from);
    }

    /// <summary>The block standing after a call's brackets, where there is one.</summary>
    private (int From, int To)? Block(int close)
    {
        if (!beginning.TryGetValue(close, out var i) || i + 1 >= tokens.Count) return null;
        if (tokens[i + 1].Kind != TokenKind.Block) return null;

        var from = Offset(tokens[i + 1]);

        return (from, Closed(from));
    }

    /// <summary>
    /// The one string a call carries without a name, which is the file it names.
    /// </summary>
    /// <remarks>
    /// Without a name, because a plugin's choice of device is a string too and
    /// is an argument like any other. What has no name in front of it is the
    /// file, and there is at most one.
    /// </remarks>
    private (int From, int Length)? Text((int Open, int Close) brackets)
    {
        if (!beginning.TryGetValue(brackets.Open, out var i)) return null;

        var depth = 0;

        for (; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.OpenParen) depth++;
            else if (tokens[i].Kind == TokenKind.CloseParen && --depth == 0) return null;
            else if (depth == 1
                && tokens[i].Kind == TokenKind.Text
                && !(i >= 2 && tokens[i - 1].Kind == TokenKind.Colon))
            {
                return (Offset(tokens[i]), tokens[i].Text.Length + 2);
            }
        }

        return null;
    }

    /// <summary>
    /// Puts one more argument into a call, at whichever end it belongs.
    /// </summary>
    /// <remarks>
    /// Adding a named argument for a socket the call does not fill leaves every
    /// positional one where it was: what a bare argument lands on is the next
    /// socket still free, and naming one that was already free does not change
    /// the order of the rest.
    /// </remarks>
    private Change Argument((int Open, int Close) brackets, string written, bool first)
    {
        var inside = source.AsSpan(brackets.Open + 1, brackets.Close - brackets.Open - 1).Trim().Length > 0;

        if (!inside) return new Change(brackets.Close, 0, written);

        return first
            ? new Change(brackets.Open + 1, 0, written + ", ")
            : new Change(brackets.Close, 0, ", " + written);
    }

    /// <summary>One key for a module and a word, however the word was spelled.</summary>
    private static (Guid, string) Folded(Guid node, string name) => (node, name.ToLowerInvariant());

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
