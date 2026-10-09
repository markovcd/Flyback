using Flyback.Core.Graph;
using Flyback.Engine.Graph;

namespace Flyback.Editor.Canvas;

/// <summary>
/// One kept group, as it was read back off the disk.
/// </summary>
/// <remarks>
/// The whole of what was read, trouble included: a fragment naming a module this
/// build has not got is still listed, because the entry is a real thing somebody
/// saved. What it cannot do is arrive quietly full of holes — see
/// <see cref="IsComplete"/>, the same check a paste makes.
/// </remarks>
/// <param name="Name">
/// What the palette calls it: the name on the group inside, falling back to the
/// file's own name for a patch dropped into the folder by hand.
/// </param>
/// <param name="Path">The file it came from, which is also how it is removed.</param>
public sealed record SavedGroup(string Name, string Path, PatchLoad Load)
{
    public bool IsComplete => Load.IsComplete;

    /// <summary>What to add to a patch — an ordinary fragment, box and all.</summary>
    public Patch Fragment => Load.Patch;

    /// <summary>
    /// How many modules arrive with it, which is what a saved group is worth
    /// knowing before it is added. The sink is left out because pasting drops it.
    /// </summary>
    public int Modules => Fragment.Nodes.Count(node => !NodeCatalog.IsSink(node.TypeId));
}
