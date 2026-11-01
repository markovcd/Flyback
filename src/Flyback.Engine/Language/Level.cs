namespace Flyback.Engine.Language;

/// <summary>
/// A group, def call, loop or loop pass the text opened, the modules it placed itself,
/// and the levels opened inside it.
/// </summary>
/// <param name="Name">A group's name, null for one with none; a call's def; a loop's names, as <c>for pitch</c>.</param>
/// <param name="Members">The modules placed in this level and in none inside it.</param>
public sealed record Level(
    LevelKind Kind,
    string? Name,
    int Line,
    int Column,
    IReadOnlyList<Guid> Members,
    IReadOnlyList<Level> Inside)
{
    /// <summary>The <c>let</c> that holds what a call or a loop hands back, where one does.</summary>
    public string? Binding { get; init; }

    /// <summary>A pass's place in its loop, counted from one.</summary>
    public int? Pass { get; init; }

    /// <summary>How many passes a loop or a pass's loop made.</summary>
    public int? Passes { get; init; }

    /// <summary>What a pass's names were bound to, as the text would write it.</summary>
    public string? Item { get; init; }

    /// <summary>Every module in this level and in the levels inside it.</summary>
    public IEnumerable<Guid> All => Members.Concat(Inside.SelectMany(level => level.All));

    /// <summary>How the level reads in a list: <c>Bass</c>, <c>bass = voice</c>, <c>pads = for pitch ×3</c>, <c>pass 2/3</c>.</summary>
    public string Label => Kind switch
    {
        LevelKind.Group => Name ?? "group",
        LevelKind.Call or LevelKind.Loop when Binding is not null => $"{Binding} = {Named}",
        LevelKind.Pass => $"pass {Pass}/{Passes}",
        _ => Named,
    };

    private string Named => Kind == LevelKind.Loop ? $"{Name} ×{Passes}" : Name ?? "";

    /// <summary>The levels from the outermost down to the one that placed <paramref name="module"/>, or null where none did.</summary>
    public static IReadOnlyList<Level>? PathTo(IReadOnlyList<Level> levels, Guid module)
    {
        foreach (var level in levels)
        {
            if (level.Members.Contains(module)) return [level];
            if (PathTo(level.Inside, module) is { } deeper) return [level, .. deeper];
        }

        return null;
    }
}
