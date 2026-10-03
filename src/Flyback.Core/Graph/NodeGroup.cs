namespace Flyback.Core.Graph;

/// <summary>
/// Several modules drawn as one box, and nothing whatever besides.
/// </summary>
/// <remarks>
/// A group is a fact about the canvas and not about the patch: the modules stay
/// where they were, the wires stay as they were drawn, and the compiler is never
/// told, which is why it costs so little.
/// <para>
/// It is deliberately not a definition. There is one per set of modules rather
/// than one per shape, because an instanced group would need ports that outlive
/// an edit and a <see cref="Connection"/> names a port by index — so rearranging
/// the inside would quietly rewire every patch holding one. Nothing here writes a
/// port index down.
/// </para>
/// </remarks>
public sealed class NodeGroup
{
    /// <inheritdoc cref="NodeInstance.NameLimit"/>
    public const int NameLimit = NodeInstance.NameLimit;

    /// <summary>
    /// The fewest modules a group may hold. A box round one is the module again
    /// with a second name: every socket it could show is one that module already
    /// has, in the same order. Held here rather than on the gesture, so it is true
    /// of a group however it arrived — see <see cref="Patch.Group"/> and
    /// <see cref="Patch.Remove"/>.
    /// </summary>
    public const int Fewest = 2;

    /// <summary>Names this group within its patch.</summary>
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
    /// Calls it something else, or takes the name off again. The counterpart of
    /// <see cref="NodeInstance.Rename"/> and the same rules: trimmed, held to a
    /// length a header can draw, and emptied back to null rather than kept blank.
    /// </summary>
    public void Rename(string? to)
    {
        var trimmed = to?.Trim();

        if (trimmed is { Length: > NameLimit }) trimmed = TextLimit.Clip(trimmed, NameLimit).TrimEnd();

        Name = string.IsNullOrEmpty(trimmed) || trimmed == Counted ? null : trimmed;
    }

    /// <summary>
    /// A copy holding the same members, under <paramref name="id"/> or, where none is given,
    /// this group's own.
    /// </summary>
    public NodeGroup Clone(Guid? id = null) => new()
    {
        Id = id ?? Id,
        Name = Name,
        Members = [.. Members],
        Collapsed = Collapsed,
        Exposed = [.. Exposed],
    };
}