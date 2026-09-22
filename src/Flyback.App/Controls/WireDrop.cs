using Avalonia;
using Flyback.Core.Graph;

namespace Flyback.App.Controls;

/// <summary>
/// A wire let go over empty canvas: where it landed, and which socket is still
/// holding the other end.
/// </summary>
/// <param name="At">Where it was dropped, in graph space — where the new module goes.</param>
/// <param name="Node">The module the wire is still attached to.</param>
/// <param name="Port">Which of that module's sockets.</param>
/// <param name="FromOutput">
/// True when the loose end is looking for an input, because the end still held
/// is an output. The whole of which direction the new wire runs.
/// </param>
/// <param name="Kind">What flows down it, which is a hint about where it belongs on the far end.</param>
public readonly record struct WireDrop(Point At, Guid Node, int Port, bool FromOutput, PortKind Kind)
{
    /// <summary>
    /// Which socket of a new <paramref name="def"/> the wire belongs on, or null
    /// where the module has none of that kind at all.
    /// </summary>
    /// <remarks>
    /// The port a module is about comes first: <see cref="PortSpec.Domain"/> and
    /// <see cref="PortSpec.Swept"/> are the socket the module exists to have
    /// something in, which is what the compiler already warns about. Then an exact
    /// match of kind, which tells a Scan's <c>view</c> from its <c>out</c>. Then the
    /// first socket, which is where this would land anyway — the catalog is
    /// written with the principal one first.
    /// </remarks>
    public int? SocketOn(NodeDef def)
    {
        var sockets = FromOutput ? def.Inputs : def.Outputs;

        if (sockets.Count == 0) return null;

        for (var i = 0; i < sockets.Count; i++)
            if (sockets[i].Domain || sockets[i].Swept)
                return i;

        for (var i = 0; i < sockets.Count; i++)
            if (sockets[i].Kind == Kind)
                return i;

        return 0;
    }
}
