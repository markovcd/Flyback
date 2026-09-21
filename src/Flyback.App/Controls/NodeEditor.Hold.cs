using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Flipping a module for as long as the right button is down on it: off if it is
/// on, on if it is off. A way to hear what it contributes by taking it away or
/// bringing it in, with nothing to undo afterward.
/// </summary>
/// <remarks>
/// Not an edit. The patch is sounded with the module flipped and put back exactly
/// as it was, so nothing reaches the history. A box flips together: it goes on
/// only where every module in it is off, the way <see cref="SelectionIsOff"/> reads.
/// </remarks>
public sealed partial class NodeEditor
{
    /// <summary>The modules the press flipped, and whether each was off before.</summary>
    private readonly List<(Guid Id, bool WasOff)> held = [];

    private void Hold(IEnumerable<Guid> ids)
    {
        if (Locked) return;

        var nodes = ids
            .Select(patch.Find)
            .OfType<NodeInstance>()
            .Where(node => !NodeCatalog.IsSink(node.TypeId))
            .ToArray();

        var muting = nodes.Any(node => !node.Off);

        foreach (var node in nodes)
        {
            if (node.Off == muting) continue;

            held.Add((node.Id, node.Off));
            node.Off = muting;
        }

        if (held.Count > 0) Sounded();
    }

    private void ReleaseHeld()
    {
        if (held.Count == 0) return;

        foreach (var (id, wasOff) in held)
            if (patch.Find(id) is { } node)
                node.Off = wasOff;

        held.Clear();
        Sounded();
    }

    private void Sounded()
    {
        InvalidateVisual();
        PatchChanged?.Invoke(this, EventArgs.Empty);
    }
}
