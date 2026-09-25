using Flyback.Core.Graph;

namespace Flyback.App.Canvas;

/// <summary>
/// Flipping a module for as long as the right button is down on it: off if it is on,
/// on if it is off. A way to hear what it contributes, with nothing to undo afterward.
/// </summary>
/// <remarks>
/// Not an edit: the patch is sounded with the module flipped and put back exactly as it
/// was, so nothing reaches the history. A box flips together, going on only where every
/// module in it is off.
/// </remarks>
internal sealed class HeldModules(CanvasHistory history, Repaint repaint)
{
    /// <summary>The modules the press flipped, and whether each was off before.</summary>
    private readonly List<(Guid Id, bool WasOff)> held = [];

    /// <summary>Whether a press is holding anything flipped.</summary>
    public bool Holding => held.Count > 0;

    public void Hold(IEnumerable<Guid> ids)
    {
        if (history.Locked) return;

        var nodes = ids
            .Select(history.Patch.Find)
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

    public void Release()
    {
        if (held.Count == 0) return;

        foreach (var (id, wasOff) in held)
            if (history.Patch.Find(id) is { } node)
                node.Off = wasOff;

        held.Clear();
        Sounded();
    }

    private void Sounded()
    {
        repaint.Request();
        history.Sounded();
    }
}
