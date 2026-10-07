namespace Flyback.Engine.Language;

internal sealed class SourceMapEdits
{
    private readonly SourceMapText text;
    private readonly Dictionary<Guid, Site> calls;
    private readonly Dictionary<(Guid Node, string Name), Site?> written;
    private readonly Dictionary<Guid, string> named;
    private readonly IReadOnlySet<Guid> shared;

    internal SourceMapEdits(
        SourceMapText text,
        IReadOnlyDictionary<Guid, Site> calls,
        IReadOnlyDictionary<(Guid Node, string Name), Site?> written,
        IReadOnlyDictionary<Guid, string> named,
        IReadOnlySet<Guid> shared)
    {
        this.text = text;
        this.calls = new Dictionary<Guid, Site>(calls);
        this.named = new Dictionary<Guid, string>(named);
        this.shared = shared;
        this.written = [];

        foreach (var (key, site) in written) this.written[Folded(key.Node, key.Name)] = site;
    }

    internal Change? Knob(Guid node, string name, string value)
    {
        if (shared.Contains(node)) return null;

        if (written.TryGetValue(Folded(node, name), out var said))
        {
            return said is { } site && text.Value(site) is { } span
                ? new Change(span.From, span.Length, value)
                : null;
        }

        if (calls.TryGetValue(node, out var call) && text.Parentheses(call) is { } brackets)
            return Argument(brackets, name + ": " + value, first: false);

        if (!named.TryGetValue(node, out var word)) return null;

        var end = text.Source.TrimEnd('\n', '\r').Length;

        return new Change(end, text.Source.Length - end, $"\n{word}.{name} = {value}\n");
    }

    internal Change? Carried(Guid node, string block)
    {
        if (shared.Contains(node)) return null;
        if (!calls.TryGetValue(node, out var call) || text.Parentheses(call) is not { } brackets) return null;

        return text.Block(brackets.Close) is { } span
            ? new Change(span.From, span.To - span.From, block)
            : new Change(brackets.Close + 1, 0, " " + block);
    }

    internal Change? File(Guid node, string path)
    {
        if (shared.Contains(node) || path.Contains('"')) return null;
        if (!calls.TryGetValue(node, out var call) || text.Parentheses(call) is not { } brackets) return null;

        var quoted = $"\"{path}\"";

        return text.Text(brackets) is { } span
            ? new Change(span.From, span.Length, quoted)
            : Argument(brackets, quoted, first: true);
    }

    internal Change? Keyboard(string? line) => PatchLine("keyboard", TokenKind.Identifier, line);

    internal Change? Description(string? line) => PatchLine("description", TokenKind.Text, line);

    internal Change? Author(string? line) => PatchLine("author", TokenKind.Text, line, "description");

    internal Change? Tags(string? line) => PatchLine("tags", TokenKind.Text, line, "author", "description");

    internal Change? Length(string? line) => PatchLine("length", TokenKind.Number, line, "tags", "author", "description");

    internal IReadOnlyDictionary<Guid, string> PanelWords =>
        Panels().ToDictionary(panel => NodeIdentity.PanelId(panel.Word), panel => panel.Word);

    internal Change? Panel(IReadOnlyList<string> lines)
    {
        var found = Panels();

        if (found.Count == 0)
        {
            if (lines.Count == 0) return null;

            var joined = string.Join('\n', lines);

            return text.Statements().FirstOrDefault(statement => statement.Word == "requires") is { Word: not null } requires
                ? new Change(requires.To, 0, "\n\n" + joined)
                : new Change(0, 0, joined + "\n\n");
        }

        var output = new System.Text.StringBuilder();

        for (var i = 0; i < found.Count; i++)
        {
            var gap = i == 0 ? string.Empty : text.Source[found[i - 1].To..found[i].From];

            if (i < lines.Count) output.Append(gap).Append(lines[i]);
            else if (lines.Count > 0) output.Append(gap.TrimEnd(' ', '\t').TrimEnd('\n').TrimEnd('\r'));
            else output.Append(Unbroken(gap));
        }

        for (var i = found.Count; i < lines.Count; i++) output.Append('\n').Append(lines[i]);

        var from = found[0].From;
        var to = found[^1].To;

        if (lines.Count == 0) to = text.Source.Length - Unbroken(text.Source[to..]).Length;

        var said = output.ToString();

        return text.Source[from..to] == said ? null : new Change(from, to - from, said);
    }

    private Change? PatchLine(string word, TokenKind next, string? line, params string[] under)
    {
        if (text.Opened(word, next) is { } span)
        {
            if (line is not null)
                return text.Source[span.From..span.To] == line ? null : new Change(span.From, span.To - span.From, line);

            var end = span.To;
            while (end < text.Source.Length && text.Source[end] is ' ' or '\t') end++;
            if (end < text.Source.Length && text.Source[end] == '\r') end++;
            if (end < text.Source.Length && text.Source[end] == '\n') end++;

            return new Change(span.From, end - span.From, string.Empty);
        }

        if (line is null) return null;

        foreach (var above in under)
        {
            if (text.Opened(above, TokenKind.Text) is { } before) return new Change(before.To, 0, "\n" + line);
        }

        return new Change(0, 0, line + "\n\n");
    }

    private List<(string Word, int From, int To)> Panels() =>
    [
        .. text.Statements()
            .Where(statement => statement.Word == "panel" && statement.Declares is not null)
            .Select(statement => (statement.Declares!, statement.From, statement.To)),
    ];

    private Change Argument((int Open, int Close) brackets, string value, bool first)
    {
        var inside = text.Source.AsSpan(brackets.Open + 1, brackets.Close - brackets.Open - 1).Trim().Length > 0;

        if (!inside) return new Change(brackets.Close, 0, value);

        return first
            ? new Change(brackets.Open + 1, 0, value + ", ")
            : new Change(brackets.Close, 0, ", " + value);
    }

    private static string Unbroken(string value)
    {
        var at = 0;

        while (at < value.Length && value[at] is ' ' or '\t') at++;
        if (at < value.Length && value[at] == '\r') at++;
        if (at < value.Length && value[at] == '\n') at++;

        return value[at..];
    }

    private static (Guid, string) Folded(Guid node, string name) => (node, name.ToLowerInvariant());
}
