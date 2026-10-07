namespace Flyback.Engine.Language;

/// <summary>
/// Where the text says each thing the binder built, gathered while it binds and
/// handed over as a <see cref="SourceMap"/>.
/// </summary>
internal sealed class SourceSites
{
    /// <summary>Every place the text names a module, in the order it names them.</summary>
    private readonly List<(Site Where, Guid Node)> mentions = [];

    /// <summary>The call that placed each module, which is where a knob is added.</summary>
    private readonly Dictionary<Guid, Site> calls = [];

    /// <summary>
    /// Where the file writes each named value it sets — a socket's knob, or one
    /// of a plugin's declared fields.
    /// </summary>
    private readonly Dictionary<(Guid Node, string Name), Site?> written = [];

    /// <summary>How often each number the file writes is read through a name.</summary>
    private readonly Dictionary<Site, int> readThrough = [];

    /// <summary>Modules a statement is about, rather than mentions inside one.</summary>
    private readonly HashSet<Guid> bound = [];

    /// <summary>What the text calls each module it has a word for.</summary>
    private readonly Dictionary<Guid, string> named = [];

    /// <summary>Where each group block begins, and the group it built.</summary>
    private readonly List<(Site Where, Guid Group)> blocks = [];

    /// <summary>A word in the text names <paramref name="node"/>.</summary>
    public void Mention(Site where, Guid node) => mentions.Add((where, node));

    /// <summary>A number the text writes is read through a name.</summary>
    public void ReadThrough(Site where) => readThrough[where] = readThrough.GetValueOrDefault(where) + 1;

    /// <summary>The call at <paramref name="where"/> placed <paramref name="node"/>.</summary>
    public void Call(Guid node, Site where) => calls[node] = where;

    /// <summary>The text sets <paramref name="name"/> on <paramref name="node"/> at <paramref name="where"/>, or at no one figure.</summary>
    public void Write(Guid node, string name, Site? where) => written[(node, name)] = where;

    /// <summary>A statement is about <paramref name="node"/>.</summary>
    public void Own(Guid node) => bound.Add(node);

    /// <summary>Calls <paramref name="node"/> <paramref name="name"/>, unless the text already has a word for it.</summary>
    public void Name(Guid node, string name) => named.TryAdd(node, name);

    /// <summary>What the text calls <paramref name="node"/>, where it has a word for it.</summary>
    public string? NameOf(Guid node) => named.GetValueOrDefault(node);

    /// <summary>A group block begins at <paramref name="where"/> and built <paramref name="group"/>.</summary>
    public void Open(Site where, Guid group) => blocks.Add((where, group));

    /// <summary>
    /// What the text says about modules the folding took away or remade: a word
    /// that named one names the Expression it went into, and a knob or a call
    /// written for one points nowhere, since its brackets take no formula.
    /// </summary>
    public void Folded(Dictionary<Guid, Guid> into)
    {
        if (into.Count == 0) return;

        for (var i = 0; i < mentions.Count; i++)
            if (into.TryGetValue(mentions[i].Node, out var root))
                mentions[i] = (mentions[i].Where, root);

        foreach (var id in into.Keys) calls.Remove(id);

        foreach (var key in written.Keys.Where(key => into.ContainsKey(key.Node)).ToList()) written.Remove(key);

        foreach (var (id, root) in into)
            if (bound.Remove(id)) bound.Add(root);
    }

    public SourceMap Map(string source) => new(source, mentions, calls, Writable(), named, bound, blocks);

    /// <summary>
    /// <see cref="written"/>, less the numbers read through a name more than once:
    /// rewriting one of those would turn every knob and sum that reads it.
    /// </summary>
    private Dictionary<(Guid Node, string Name), Site?> Writable() => written.ToDictionary(
        pair => pair.Key,
        pair => pair.Value is { } site && readThrough.GetValueOrDefault(site) > 1 ? null : pair.Value);
}
