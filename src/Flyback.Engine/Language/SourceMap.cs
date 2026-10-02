using Flyback.Engine.Graph;

namespace Flyback.Engine.Language;

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

    private readonly SourceMapText text;

    /// <summary>The call that placed each module, where the text has one.</summary>
    private readonly Dictionary<Guid, Site> calls;

    private readonly SourceMapEdits edits;

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

    /// <summary>The name each module is bound to with <c>let</c>, where it has one.</summary>
    internal IReadOnlyDictionary<Guid, string> Named { get; }

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
        text = new SourceMapText(source);
        this.calls = new Dictionary<Guid, Site>(calls);
        Named = new Dictionary<Guid, string>(named);
        edits = new SourceMapEdits(text, this.calls, written, named, shared);

        var counted = new Dictionary<Site, Guid>();

        foreach (var (where, node) in mentions)
        {
            if (text.Extent(where) is not { } extent) continue;

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
            var from = text.Offset(where);

            if (!text.TryBeginning(from, out var i)) continue;

            var open = i;

            while (open < text.Tokens.Count && text.Tokens[open].Kind != TokenKind.OpenBrace) open++;

            if (open >= text.Tokens.Count) continue;

            var depth = 0;
            var close = open;

            for (; close < text.Tokens.Count; close++)
            {
                if (text.Tokens[close].Kind == TokenKind.OpenBrace) depth++;
                else if (text.Tokens[close].Kind == TokenKind.CloseBrace && --depth == 0) break;
            }

            var opened = text.Offset(text.Tokens[open]) + 1;
            var closed = close < text.Tokens.Count ? text.Offset(text.Tokens[close]) : text.Source.Length;

            blocks.Add((from, opened, closed, Math.Min(closed + 1, text.Source.Length), group));
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
        => edits.Knob(node, name, value);

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
        => edits.Carried(node, block);

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
        => edits.File(node, path);

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
    public Change? Keyboard(string? line) => edits.Keyboard(line);

    /// <summary>
    /// The edit that makes the text describe the patch as <paramref name="line"/>
    /// says, or null where the text already does.
    /// </summary>
    /// <param name="line">What <see cref="PatchPrinter.Description"/> writes, and null for none.</param>
    public Change? Description(string? line) => edits.Description(line);

    /// <summary>
    /// The edit that makes the text credit the patch as <paramref name="line"/>
    /// says, or null where the text already does. A new one goes under the description.
    /// </summary>
    /// <param name="line">What <see cref="PatchPrinter.Author"/> writes, and null for none.</param>
    public Change? Author(string? line) => edits.Author(line);

    /// <summary>
    /// The edit that makes the text tag the patch as <paramref name="line"/>
    /// says, or null where the text already does. A new one goes under the
    /// author, or under the description where nobody is credited.
    /// </summary>
    /// <param name="line">What <see cref="PatchPrinter.Tags"/> writes, and null for none.</param>
    public Change? Tags(string? line) => edits.Tags(line);

    /// <summary>
    /// The edit that makes the text give the patch the length <paramref name="line"/>
    /// says, or null where the text already does. A new one goes under the tags,
    /// the author or the description, whichever comes last.
    /// </summary>
    /// <param name="line">What <see cref="PatchPrinter.Length"/> writes, and null for none.</param>
    public Change? Length(string? line) => edits.Length(line);

    /// <summary>Each panel knob the text declares, by the id the binder gives it, and the word it is called by.</summary>
    public IReadOnlyDictionary<Guid, string> PanelWords => edits.PanelWords;

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
    public Change? Panel(IReadOnlyList<string> lines) => edits.Panel(lines);

    /// <summary>
    /// The statement each binding owns, worked out from the text rather than
    /// recorded with it, so that where a statement ends is one rule and the
    /// lexer's.
    /// </summary>
    private void Bindings(IReadOnlySet<Guid> bound)
    {
        if (bound.Count == 0 || text.Tokens.Count == 0) return;

        var statements = new List<(int From, int To)>();
        var from = -1;

        foreach (var token in StatementTokens.ForParsing(text.Tokens))
        {
            if (token.Kind is TokenKind.NewLine or TokenKind.End)
            {
                if (from >= 0) statements.Add((from, text.Offset(token)));
                from = -1;
                continue;
            }

            if (from < 0) from = text.Offset(token);
        }

        foreach (var node in bound)
        {
            if (!calls.TryGetValue(node, out var site)) continue;

            var at = text.Offset(site);

            foreach (var (start, end) in statements)
            {
                if (at < start || at > end) continue;

                bindings.Add((start, end, node));
                break;
            }
        }
    }

}
