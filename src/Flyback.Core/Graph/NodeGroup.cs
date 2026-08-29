namespace Flyback.Core.Graph;

/// <summary>
/// Several modules drawn as one box, and nothing whatever besides.
/// </summary>
/// <remarks>
/// A group is a fact about the canvas and not about the patch. The modules stay
/// where they were, the wires between them stay exactly as they were drawn, and
/// the compiler is never told any of this happened — which is the whole of why
/// it costs so little. Collapsing hides some boxes and draws one in their place;
/// expanding stops doing that.
/// <para>
/// What it deliberately is not is a definition. There is one of these per set of
/// modules rather than one per shape, so two groups made the same way are two
/// groups and editing one does nothing to the other. A group that could be
/// instanced would need ports that outlive an edit, and a
/// <see cref="Connection"/> names a port by index — so the day the inside was
/// rearranged, every patch holding an instance would quietly rewire itself.
/// Nothing here writes a port index down, which is exactly what makes it safe.
/// </para>
/// </remarks>
public sealed class NodeGroup
{
    /// <inheritdoc cref="NodeInstance.NameLimit"/>
    public const int NameLimit = NodeInstance.NameLimit;

    /// <summary>
    /// The fewest modules a group may hold.
    /// </summary>
    /// <remarks>
    /// A box round one module is a box that says nothing. Every socket it could
    /// show is a socket that module already has, drawn in the same order, so it
    /// is the module again with a second name and one more thing to open. Held
    /// here rather than on the gesture that makes one, so it is true of a group
    /// however it arrived — see <see cref="Patch.Group"/>, and see
    /// <see cref="Patch.Remove"/>, which drops a group that deleting has worn
    /// down to one rather than leaving the picture this forbids.
    /// </remarks>
    public const int Fewest = 2;

    public required Guid Id { get; init; }

    /// <summary>
    /// What it has been called, and null where it has not been — in which case
    /// the box names itself after how many modules are in it.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Which modules are inside. Ids rather than instances, because this is
    /// serialised beside them rather than around them.
    /// </summary>
    public List<Guid> Members { get; set; } = [];

    /// <summary>Whether the box is drawn in place of its modules, or they are.</summary>
    public bool Collapsed { get; set; }

    /// <summary>
    /// Ports the box shows whether or not a wire is on them.
    /// </summary>
    /// <remarks>
    /// A group socket names a real port on one member. It stays visible while the
    /// wire is connected, and remains on the edge once the wire is removed.
    /// </remarks>
    public List<GroupSocket> Exposed { get; set; } = [];

    /// <summary>Puts a socket on the edge, if it is not already there.</summary>
    public bool Expose(GroupSocket socket)
    {
        if (Exposed.Contains(socket)) return false;

        Exposed.Add(socket);
        return true;
    }

    /// <summary>
    /// Takes a socket off the edge. What is still wired comes back the moment
    /// anything asks, since a crossing wire is a socket whatever this says.
    /// </summary>
    public bool Hide(GroupSocket socket) => Exposed.Remove(socket);

    /// <summary>What the header reads.</summary>
    public string Title() => string.IsNullOrWhiteSpace(Name) ? Counted : Name;

    /// <summary>
    /// What a group with no name of its own is called: how many modules are in
    /// it, which is the only thing that can be said about one without looking
    /// inside.
    /// </summary>
    public string Counted => Members.Count == 1 ? "1 module" : $"{Members.Count} modules";

    /// <summary>
    /// Calls it something else, or takes the name off again.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="NodeInstance.Rename"/> and the same rules:
    /// trimmed, held to a length a header can draw, and emptied back to null
    /// rather than kept as a blank — so a group nobody has named writes no name
    /// into the file and goes back to counting itself.
    /// </remarks>
    public void Rename(string? to)
    {
        var trimmed = to?.Trim();

        if (trimmed is { Length: > NameLimit }) trimmed = trimmed[..NameLimit].TrimEnd();

        Name = string.IsNullOrEmpty(trimmed) || trimmed == Counted ? null : trimmed;
    }

    public NodeGroup Clone(Guid? id = null) => new()
    {
        Id = id ?? Id,
        Name = Name,
        Members = [.. Members],
        Collapsed = Collapsed,
        Exposed = [.. Exposed],
    };
}

/// <summary>
/// One port of one module inside a group, drawn on the edge of the box.
/// </summary>
/// <remarks>
/// The module and the port rather than a number of its own, and that is the
/// point: a socket on the box is only ever a way of pointing at a socket on a
/// module. Nothing renumbers, and a wire drawn to one is a wire drawn to the
/// module it names.
/// </remarks>
/// <param name="IsOutput">
/// Which side it is, which is a fact about the port and not about the box. Part
/// of what a socket <em>is</em> rather than something worked out later, because
/// a module's inputs and outputs are numbered separately — port 0 is very often
/// both, and the two are different sockets.
/// </param>
public readonly record struct GroupSocket(Guid Node, int Port, bool IsOutput);

/// <summary>
/// A collapsed group's whole interface: every port at which a wire crosses in,
/// and every port from which one leaves.
/// </summary>
/// <remarks>
/// Derived rather than declared, and derived from the wires alone. An input
/// resting on a knob is not here, and neither is one normalled to Time
/// ([0050](0050-normalled-sockets-carry-a-signal-with-no-wire.md)) — no wire
/// crosses at either, so there is nothing for the box to show and nothing a
/// person could do with it if it did.
/// <para>
/// The two sides are not symmetric, for the reason
/// <see cref="Patch.SoleOutgoingFrom"/> gives: an input takes at most one wire
/// by construction, so a crossing-in wire is one socket and never shares; an
/// output may fan out to as many as it likes, so several wires leaving one inner
/// output are one socket with several wires on it — which is what fan-out
/// already looks like on an ordinary module.
/// </para>
/// </remarks>
public readonly record struct GroupSockets(
    IReadOnlyList<GroupSocket> Inputs,
    IReadOnlyList<GroupSocket> Outputs)
{
    public int Rows => Inputs.Count + Outputs.Count;

    /// <summary>Where <paramref name="socket"/> sits among the inputs, or -1.</summary>
    public int IndexOfInput(GroupSocket socket)
    {
        for (var i = 0; i < Inputs.Count; i++)
            if (Inputs[i] == socket) return i;

        return -1;
    }

    /// <inheritdoc cref="IndexOfInput"/>
    public int IndexOfOutput(GroupSocket socket)
    {
        for (var i = 0; i < Outputs.Count; i++)
            if (Outputs[i] == socket) return i;

        return -1;
    }
}
