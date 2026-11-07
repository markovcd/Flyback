using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Editor.Sequence;

/// <summary>The Song's parts as the Sequence pane edits them: a name and a row of levels each.</summary>
internal sealed class SongParts
{
    private readonly List<string> names = [];
    private readonly List<List<PartLevel>> levels = [];

    public SongParts(IEnumerable<SongPart> parts)
    {
        foreach (var part in SongExtra.Tidy(parts))
        {
            names.Add(part.Name);
            levels.Add([.. part.Levels]);
        }
    }

    public int Count => names.Count;

    /// <summary>Sections in the longest part, or 0 with none.</summary>
    public int Longest => levels.Count == 0 ? 0 : levels.Max(part => part.Count);

    public string Name(int part) => names[part];

    public IReadOnlyList<PartLevel> Levels(int part) => levels[part];

    /// <summary>The parts as the Song stores them.</summary>
    public IReadOnlyList<SongPart> Parts => SongExtra.Tidy(names.Select((name, p) => new SongPart(name, levels[p])));

    /// <summary>
    /// A level that is on goes to nought; one at nought comes back at the part's
    /// strongest level, or at one where the whole part is nought.
    /// </summary>
    public void Toggle(int part, int section)
    {
        var row = levels[part];
        var strongest = row.MaxBy(level => Math.Abs(level.Value)).Value;

        row[section] = row[section].Value != 0f
            ? new PartLevel(0f)
            : new PartLevel(strongest != 0f ? strongest : 1f);
    }

    /// <summary>Puts <paramref name="before"/> back and makes it glide, or hold where it glided.</summary>
    public void Glide(int part, int section, PartLevel before) => levels[part][section] = before with { Glides = !before.Glides };

    /// <summary>Turns one level, keeping whether it glides.</summary>
    public void Set(int part, int section, float value) => levels[part][section] = levels[part][section] with { Value = value };

    /// <summary>Gives every section from <paramref name="from"/> to <paramref name="to"/> the level <paramref name="level"/>.</summary>
    public void Paint(int part, int from, int to, PartLevel level)
    {
        var row = levels[part];
        for (var s = Math.Max(0, Math.Min(from, to)); s <= Math.Min(row.Count - 1, Math.Max(from, to)); s++) row[s] = level;
    }

    /// <summary>Makes a part loop over <paramref name="sections"/>: new sections start at nought.</summary>
    public void Resize(int part, int sections)
    {
        var row = levels[part];
        sections = Math.Clamp(sections, 1, SongModule.MaxSections);

        if (sections < row.Count) row.RemoveRange(sections, row.Count - sections);
        while (row.Count < sections) row.Add(new PartLevel(0f));
    }

    /// <summary>Renames a part, and says whether the name could be taken.</summary>
    public bool Rename(int part, string name)
    {
        name = name.Trim();

        if (!SongExtra.Writable(name)) return false;
        if (names.Where((_, p) => p != part).Any(other => other.Equals(name, StringComparison.OrdinalIgnoreCase))) return false;

        names[part] = name;
        return true;
    }

    /// <summary>Moves a part up or down the list, which moves the output it is played from.</summary>
    public void Move(int part, int by)
    {
        var to = Math.Clamp(part + by, 0, Count - 1);
        if (to == part) return;

        var (name, row) = (names[part], levels[part]);
        names.RemoveAt(part);
        levels.RemoveAt(part);
        names.Insert(to, name);
        levels.Insert(to, row);
    }

    /// <summary>Adds a copy of a part under it, where there is room for one.</summary>
    public void Duplicate(int part)
    {
        if (Count >= NodeCatalog.MaxParts) return;

        names.Insert(part + 1, names[part]);
        levels.Insert(part + 1, [.. levels[part]]);
        Retidy();
    }

    public void Remove(int part)
    {
        names.RemoveAt(part);
        levels.RemoveAt(part);
    }

    /// <summary>Adds a part at nought, as long as the longest, where there is room for one.</summary>
    public void Add()
    {
        if (Count >= NodeCatalog.MaxParts) return;

        names.Add($"part{Count + 1}");
        levels.Add([.. Enumerable.Repeat(new PartLevel(0f), Math.Max(1, Longest))]);
        Retidy();
    }

    /// <summary>Gives every part the name the Song would store it under.</summary>
    private void Retidy()
    {
        var tidy = SongExtra.Tidy(names.Select((name, p) => new SongPart(name, levels[p])));
        for (var p = 0; p < tidy.Count; p++) names[p] = tidy[p].Name;
    }
}
