using Flyback.Core.Graph;

namespace Flyback.Engine.Language;

/// <summary>
/// Each group the text opens, and the box drawn round it once every block
/// has been read: a group may be opened more than once, and its first block
/// may hold fewer modules than a box can be.
/// </summary>
internal sealed class Boxes(Patch patch, Issues issues, SourceSites sites)
{
    /// <summary>Each group and the modules its blocks placed, a name's blocks gathered into one.</summary>
    private readonly List<(string? Name, List<Guid> Members)> boxes = [];

    /// <summary>The boxes a block of which had a mistake in it, and so no say about its size.</summary>
    private readonly HashSet<int> troubled = [];

    /// <summary>Where each group block begins, and which of <see cref="boxes"/> it is.</summary>
    private readonly List<(Site Where, int Box)> opened = [];

    /// <summary>A block of a group has been read, having placed <paramref name="made"/>.</summary>
    /// <param name="troubled">Whether something in the block was refused, which leaves the box no say about its size.</param>
    public void Block(string? name, Site where, IEnumerable<Guid> made, bool troubled)
    {
        // A name opened again is the same group, which is how a printing says one
        // whose modules do not come out next to each other.
        var index = name is null ? -1 : boxes.FindIndex(box => box.Name == name);

        if (index < 0)
        {
            index = boxes.Count;
            boxes.Add((name, []));
        }

        boxes[index].Members.AddRange(made);

        if (troubled) this.troubled.Add(index);
        opened.Add((where, index));
    }

    /// <summary>Draws each box. Named from the text, so the same text builds the same boxes as it builds the same modules.</summary>
    public void Draw()
    {
        for (var i = 0; i < boxes.Count; i++)
        {
            var (name, members) = boxes[i];

            if (patch.Group(members) is not { } made)
            {
                if (troubled.Contains(i)) continue;

                var (line, column) = opened.First(open => open.Box == i).Where;

                // A warning, since the modules are built all the same and only the box is missing.
                issues.Warn(IssueCode.GroupTooSmall, line, column,
                    $"a group is drawn round {NodeGroup.Fewest} modules or more, and this one has {members.Count}, "
                    + "so it is left out. Put a lone module outside any group, or in the group it belongs to.");
                continue;
            }

            var group = made.Clone(NodeIdentity.FromName(name is null ? $"group #{i}" : "group " + name));

            group.Rename(name);
            patch.Groups![patch.Groups.IndexOf(made)] = group;

            foreach (var (where, box) in opened)
                if (box == i) sites.Open(where, group.Id);
        }
    }
}
