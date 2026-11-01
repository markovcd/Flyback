using Flyback.Core.Graph;

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

    /// <summary>A call's arguments as the text would write them, each named for its parameter.</summary>
    public IReadOnlyList<LevelArgument> Arguments { get; init; } = [];

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

    /// <summary>
    /// The levels as the layout takes them, each labeled as its shelf chip reads. A level
    /// that placed nothing itself and holds one level shares that level's band and chip.
    /// </summary>
    internal static IReadOnlyList<LayoutBand> Bands(IReadOnlyList<Level> levels, Level? loop = null) =>
        [.. levels.Select(level =>
        {
            var inner = Bands(level.Inside, level.Kind == LevelKind.Loop ? level : null);

            return level.Members.Count == 0 && inner is [var only]
                ? only with { Label = $"{level.Chip(loop)} › {only.Label}", Above = [level.Chip(loop), .. only.Above] }
                : new LayoutBand(level.Chip(loop), level.Members, inner);
        })];

    /// <summary>What a level's shelf chip reads: its label, or for a pass its place and what it took, <c>1/3 pitch = 220</c>.</summary>
    internal string Chip(Level? loop) =>
        Kind == LevelKind.Pass
            ? $"{Pass}/{Passes}" + (Item is null || loop?.Name is not { } names ? "" : $" {names["for ".Length..]} = {Item}")
            : Label;

    /// <summary>The wires from a module in this level to one outside it.</summary>
    public IEnumerable<Connection> Leaving(Patch patch)
    {
        var inside = All.ToHashSet();

        return patch.Connections.Where(wire => inside.Contains(wire.SourceNode) && !inside.Contains(wire.TargetNode));
    }

    /// <summary>Every level, each before the levels inside it.</summary>
    public static IEnumerable<Level> Every(IReadOnlyList<Level> levels) =>
        levels.SelectMany(level => Every(level.Inside).Prepend(level));

    /// <summary>The levels from the outermost down to <paramref name="target"/> itself, or null where it is not among them.</summary>
    public static IReadOnlyList<Level>? PathTo(IReadOnlyList<Level> levels, Level target)
    {
        foreach (var level in levels)
        {
            if (ReferenceEquals(level, target)) return [level];
            if (PathTo(level.Inside, target) is { } deeper) return [level, .. deeper];
        }

        return null;
    }

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
