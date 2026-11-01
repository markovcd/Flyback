using Flyback.Core.Graph;
using Flyback.Engine.Language.Values;

namespace Flyback.Engine.Language;

/// <summary>The levels the binder opens and closes as it walks the text, kept as a tree.</summary>
internal sealed class LevelTrail(Patch patch, Func<Guid, bool> shared)
{
    internal sealed class Opened(LevelKind kind, string? name, int line, int column, int from)
    {
        public readonly List<Level> Inside = [];
        public LevelKind Kind => kind;
        public string? Name => name;
        public int Line => line;
        public int Column => column;
        public int From => from;
        public int? Pass;
        public int? Passes;
        public string? Item;
    }

    private readonly List<Level> top = [];
    private readonly Stack<Opened> open = new();

    /// <summary>What each closed call or loop handed back, for a <c>let</c> to name it by.</summary>
    private readonly List<(Value Result, Level Level, List<Level> Siblings)> results = [];

    public IReadOnlyList<Level> Tree => top;

    public Opened Enter(LevelKind kind, string? name, int line, int column, int? pass = null, int? passes = null, string? item = null)
    {
        var level = new Opened(kind, name, line, column, patch.Nodes.Count) { Pass = pass, Passes = passes, Item = item };

        open.Push(level);
        return level;
    }

    /// <summary>Closes the level <see cref="Enter"/> opened, and remembers what it handed back.</summary>
    public void Leave(Opened level, Value? result = null, int? passes = null)
    {

        // A level left early by a mistake inside it closes whatever it still had open.
        while (open.Count > 0 && open.Peek() != level) Leave(open.Peek());
        if (open.Count == 0) return;

        open.Pop();

        var inner = level.Inside.SelectMany(child => child.All).ToHashSet();
        var members = patch.Nodes.Skip(level.From)
            .Select(node => node.Id)
            .Where(id => !shared(id) && !inner.Contains(id))
            .ToList();

        var closed = new Level(level.Kind, level.Name, level.Line, level.Column, members, level.Inside)
        {
            Pass = level.Pass,
            Passes = passes ?? level.Passes,
            Item = level.Item,
        };

        var siblings = open.Count > 0 ? open.Peek().Inside : top;

        siblings.Add(closed);
        if (result is not null) results.Add((result, closed, siblings));
    }

    /// <summary>Gives the call or loop that handed back <paramref name="value"/> the name a <c>let</c> bound it to.</summary>
    public void Name(Value value, string name)
    {
        var index = results.FindLastIndex(entry => ReferenceEquals(entry.Result, value) && entry.Level.Binding is null);

        if (index < 0) return;

        var (result, level, siblings) = results[index];
        var named = level with { Binding = name };

        siblings[siblings.IndexOf(level)] = named;
        results[index] = (result, named, siblings);
    }

    /// <summary>A loop's item as the text would write it: <c>A2</c>, <c>0.5</c>, <c>(A2, 0.5)</c>, a module's name.</summary>
    public string Written(Value value) => value switch
    {
        Figure { Style: NumberStyle.Note } note => Pitch.Name((float)note.Amount),
        Figure { Style: NumberStyle.Duration } time => PatchValueWriter.Value((float)time.Amount, PortDisplay.Duration),
        Figure figure => PatchValueWriter.Number((float)figure.Amount),
        Named named => $"\"{named.Path}\"",
        Several { Listed: true } list => "[" + string.Join(", ", list.Items.Select(Written)) + "]",
        Several tuple => "(" + string.Join(", ", tuple.Items.Select(Written)) + ")",
        Placed placed => Module(placed.Id, placed.Def),
        Socket socket => Module(socket.Id, socket.Def) + "." + socket.Def.Outputs[socket.Port].Name,
        Dial dial => dial.Word,
        _ => "?",
    };

    private string Module(Guid id, NodeDef def) => patch.Find(id)?.Name ?? def.Name;

    /// <summary>
    /// Moves members onto what the Expressions were folded into, each module kept in the
    /// deepest level that placed it, and drops what folding removed.
    /// </summary>
    public void Folded(Dictionary<Guid, Guid> into)
    {
        var claimed = new HashSet<Guid>();
        var kept = patch.Nodes.Select(node => node.Id).ToHashSet();

        for (var i = 0; i < top.Count; i++) top[i] = Fold(top[i], into, claimed, kept);
    }

    private static Level Fold(Level level, Dictionary<Guid, Guid> into, HashSet<Guid> claimed, HashSet<Guid> kept)
    {
        var inside = level.Inside.Select(child => Fold(child, into, claimed, kept)).ToList();
        var members = level.Members
            .Select(id => into.GetValueOrDefault(id, id))
            .Where(id => kept.Contains(id) && claimed.Add(id))
            .ToList();

        return level with { Members = members, Inside = inside };
    }
}
