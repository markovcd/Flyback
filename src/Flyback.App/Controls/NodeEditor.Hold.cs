using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// Muting a module for as long as the left button is down on it: a way to hear
/// what it contributes by taking it away, with nothing to undo afterward.
/// </summary>
/// <remarks>
/// Not an edit. The patch is sounded without the module and put back exactly as it
/// was, so nothing reaches the history and a module that was already off stays off.
/// </remarks>
public sealed partial class NodeEditor
{
    /// <summary>The modules the press switched off, and so the ones the release switches on.</summary>
    private readonly List<Guid> held = [];

    private void HoldOff(IEnumerable<Guid> ids)
    {
        if (Locked) return;

        foreach (var id in ids)
        {
            if (patch.Find(id) is not { Off: false } node || NodeCatalog.IsSink(node.TypeId)) continue;

            node.Off = true;
            held.Add(id);
        }

        if (held.Count > 0) Sounded();
    }

    private void ReleaseHeld()
    {
        if (held.Count == 0) return;

        foreach (var id in held)
            if (patch.Find(id) is { } node)
                node.Off = false;

        held.Clear();
        Sounded();
    }

    private void Sounded()
    {
        InvalidateVisual();
        PatchChanged?.Invoke(this, EventArgs.Empty);
    }
}
