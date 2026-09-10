using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Flyback.Core.Graph;

/// <summary>
/// One note in a sequence: what it plays, how long it lasts and how loud it is.
/// </summary>
/// <param name="Value">A note number on a Note Sequencer, an ordinary signal on a Sequencer.</param>
/// <param name="Length">In steps, and never zero — a note of no duration has nowhere to sound.</param>
/// <param name="Volume">
/// 0 to 1, and a level rather than a switch, so a rest and a quiet note are the
/// same control.
/// </param>
public readonly record struct Step(float Value, float Length = 1f, float Volume = 1f)
{
    /// <summary>The shortest a note may be, so that a length is always something to divide by.</summary>
    public const float ShortestLength = 0.01f;

    /// <summary>The same note with every field held to what the sequencer can play.</summary>
    public Step Sane() => new(
        float.IsFinite(Value) ? Value : 0f,
        float.IsFinite(Length) ? MathF.Max(Length, ShortestLength) : 1f,
        float.IsFinite(Volume) ? Math.Clamp(Volume, 0f, 1f) : 1f);
}

/// <summary>One placed module: a node type, where it sits, and its knob values.</summary>
public sealed class NodeInstance
{
    /// <summary>
    /// The longest a module may be renamed to, so that a name pasted from
    /// somewhere else cannot make a patch file enormous or a header undrawable.
    /// </summary>
    public const int NameLimit = 26;

    /// <summary>
    /// How far from the origin a module may sit to either side, in graph units.
    /// Fifteen thousand holds the widest drawing in the box with no room to get
    /// lost in: framing clamps its zoom, so a module flung further cannot be got
    /// back. Held on the coordinate, so it is true however the module was placed.
    /// </summary>
    public const double Across = 7_500d;

    /// <summary>
    /// The same going down: ten thousand. Smaller than <see cref="Across"/>
    /// because a signal chain runs left to right, so patches grow across faster
    /// than they grow down.
    /// </summary>
    public const double Down = 5_000d;

    public required Guid Id { get; init; }

    public required string TypeId { get; init; }

    /// <summary>Where it sits. Always inside the canvas — see <see cref="Across"/>.</summary>
    public double X
    {
        get;
        set => field = Inside(value, Across);
    }

    /// <inheritdoc cref="X"/>
    public double Y
    {
        get;
        set => field = Inside(value, Down);
    }

    /// <summary>
    /// What this one has been renamed to, and null where it has not been, so an
    /// unrenamed module writes no name into the file. A label and nothing more:
    /// nothing is ever found by name. Set through <see cref="Rename"/>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Per-input constants, used for any input with nothing wired into it.
    /// Length always matches the definition's input count.
    /// </summary>
    public float[] InputValues { get; set; } = [];

    /// <summary>
    /// Everything this instance carries that is not a knob — a sequencer's
    /// notes, a quantiser's scale, a player's file — each under its
    /// <see cref="NodeExtra.Key"/>, and null for the modules that carry nothing.
    /// </summary>
    /// <remarks>
    /// One store rather than a field per kind (ADR-0061), holding
    /// <see cref="JsonNode"/> because the engine must round-trip a plugin's
    /// shape without understanding it. <see cref="StepsExtra.Of"/> and its
    /// siblings are what read a typed shape back out.
    /// </remarks>
    public Dictionary<string, JsonNode>? State { get; set; }

    /// <summary>
    /// What the extra called <paramref name="key"/> has stored here, or null
    /// where it has stored nothing.
    /// </summary>
    public JsonNode? StateOf(string key) =>
        State is { } held && held.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Stores an extra's state under its key, making the dictionary on first use
    /// and taking it away with the last entry, so a module that carries nothing
    /// writes no empty object into the file.
    /// </summary>
    public void SetState(string key, JsonNode? value)
    {
        if (value is null)
        {
            State?.Remove(key);
            if (State is { Count: 0 }) State = null;

            return;
        }

        (State ??= [])[key] = value;
    }

    /// <summary>
    /// One coordinate held inside the canvas. Not a number at all becomes the
    /// origin rather than the near edge: NaN is a coordinate that was never
    /// computed, and it would poison every comparison looking for the corners.
    /// </summary>
    private static double Inside(double value, double edge) =>
        double.IsNaN(value) ? 0d : Math.Clamp(value, -edge, edge);

    /// <summary>
    /// What to call this one: the name it was given, or its definition's. The
    /// one way anything should ask, so a renamed module reads the same on the
    /// canvas, in the panel and in a compiler complaint.
    /// </summary>
    public string Title(NodeDef def) => Name ?? def.Name;

    /// <summary>
    /// Renames this module, or puts it back to its definition's name.
    /// </summary>
    /// <param name="def">The definition, which is what "no name" means.</param>
    /// <param name="to">
    /// The new name. Blank puts it back, and so does the definition's own name:
    /// storing that would leave a file claiming a name that changes under it the
    /// day the module is renamed in the catalogue.
    /// </param>
    public void Rename(NodeDef def, string? to)
    {
        var trimmed = to?.Trim();

        if (trimmed is { Length: > NameLimit }) trimmed = trimmed[..NameLimit].TrimEnd();

        Name = string.IsNullOrEmpty(trimmed) || trimmed == def.Name ? null : trimmed;
    }

    /// <summary>
    /// A deep copy of this module, optionally with a fresh identity and shifted
    /// on the canvas.
    /// </summary>
    /// <remarks>
    /// Deep, so a clipboard holds the patch as it was rather than a view onto
    /// one still being edited. A copy must not need a definition — a fragment
    /// naming a module this build has no plugin for still has to keep its notes
    /// — and cloning <see cref="State"/> keeps them without knowing what they are.
    /// </remarks>
    /// <param name="id">The copy's identity, or null to keep this one's.</param>
    /// <param name="dx">How far to move it across.</param>
    /// <param name="dy">How far to move it down.</param>
    public NodeInstance Clone(Guid? id = null, double dx = 0d, double dy = 0d) => new()
    {
        Id = id ?? Id,
        TypeId = TypeId,
        Name = Name,
        X = X + dx,
        Y = Y + dy,
        InputValues = [.. InputValues],

        // Deep here too, and it has to be said explicitly: a JsonNode is a
        // mutable tree, so copying the dictionary alone would hand the copy
        // the very nodes the original goes on being edited through.
        State = State is { } held
            ? held.ToDictionary(entry => entry.Key, entry => entry.Value.DeepClone())
            : null,
    };

    /// <param name="id">
    /// What to call it, or null for a name nothing has had before. Supplying one
    /// says this is the same module as something that existed before, which is
    /// what lets a patch rebuilt from its source keep the memory, the positions
    /// and the selection of the one it replaces. Two nodes sharing an id is a
    /// patch that cannot be wired.
    /// </param>
    /// <param name="def"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public static NodeInstance Create(NodeDef def, double x, double y, Guid? id = null)
    {
        var node = new NodeInstance
        {
            Id = id ?? Guid.NewGuid(),
            TypeId = def.TypeId,
            X = x,
            Y = y,
            InputValues = [.. def.Inputs.Select(p => p.Default)],
        };

        // Whatever this module carries that is not a knob, each kind writing
        // under its own key — see NodeExtra. A module with none, which is nearly
        // all of them, leaves State null.
        foreach (var extra in def.Extras) extra.Seed(node);

        return node;
    }
}

/// <summary>A wire from one node's output socket to another node's input socket.</summary>
public sealed record Connection(Guid SourceNode, int SourcePort, Guid TargetNode, int TargetPort);

/// <summary>The whole document: modules plus the wires between them.</summary>
public sealed class Patch
{
    /// <summary>
    /// Which layout of the file this came from, stamped as it is written and
    /// declared first. Null on a patch that has not been through
    /// <see cref="PatchIO.ToJson"/> and on every file written before the stamp
    /// existed, which is why reading treats null as
    /// <see cref="PatchIO.FirstVersion"/> rather than as a fault.
    /// </summary>
    public int? Version { get; set; }

    /// <summary>
    /// The plugins this patch cannot be opened without, stamped as it is
    /// written. Null rather than empty where it uses only the engine's own
    /// modules, so an ordinary file looks as it always did.
    /// </summary>
    public List<ModuleProvider>? Requires { get; set; }

    public List<NodeInstance> Nodes { get; set; } = [];

    public List<Connection> Connections { get; set; } = [];

    /// <summary>
    /// Which modules are drawn together as one box, and null where none are.
    /// </summary>
    /// <remarks>
    /// The one field here that says nothing about what the patch computes: a
    /// reader that does not know about groups draws every module separately and
    /// is otherwise correct, which is why this could be added without moving
    /// <see cref="PatchIO.FormatVersion"/>.
    /// </remarks>
    public List<NodeGroup>? Groups { get; set; }

    public NodeInstance? Find(Guid id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>The group holding <paramref name="nodeId"/>, or null where none does.</summary>
    public NodeGroup? GroupOf(Guid nodeId)
    {
        if (Groups is null) return null;

        foreach (var group in Groups)
            if (group.Members.Contains(nodeId))
                return group;

        return null;
    }

    /// <summary>
    /// The group holding <paramref name="nodeId"/> if it is collapsed, which is
    /// the question every drawing and hit-testing decision asks.
    /// </summary>
    public NodeGroup? CollapsedGroupOf(Guid nodeId) =>
        GroupOf(nodeId) is { Collapsed: true } group ? group : null;

    /// <summary>
    /// Draws <paramref name="members"/> together, and hands back the group. The
    /// sink is left out rather than refused, the way copying leaves it out
    /// (ADR-0045). A module already in a group leaves it, since two boxes both
    /// claiming to draw one module is a picture with no meaning.
    /// </summary>
    /// <returns>
    /// The new group, or null where what was asked for would not be one — an
    /// empty selection, the sink on its own, or fewer than
    /// <see cref="NodeGroup.Fewest"/> modules.
    /// </returns>
    public NodeGroup? Group(IEnumerable<Guid> members)
    {
        var inside = members
            .Distinct()
            .Where(id => Find(id) is { } node && !NodeCatalog.IsSink(node.TypeId))
            .ToList();

        if (inside.Count < NodeGroup.Fewest) return null;

        foreach (var id in inside) Forget(id);

        var group = new NodeGroup { Id = Guid.NewGuid(), Members = inside, Collapsed = true };

        // The edge it is born with: whatever is wired across it right now, kept
        // so that unplugging one of those wires leaves the socket behind rather
        // than taking it away. See NodeGroup.Exposed.
        var sockets = SocketsOf(group);

        foreach (var socket in sockets.Inputs) group.Expose(socket);
        foreach (var socket in sockets.Outputs) group.Expose(socket);

        (Groups ??= []).Add(group);
        return group;
    }

    /// <summary>Stops drawing a group, without touching anything inside it.</summary>
    public bool Ungroup(Guid groupId)
    {
        if (Groups is null) return false;

        var went = Groups.RemoveAll(g => g.Id == groupId) > 0;

        if (Groups.Count == 0) Groups = null;

        return went;
    }

    /// <summary>
    /// Takes a module out of whatever group holds it, dropping any group left
    /// with fewer than <see cref="NodeGroup.Fewest"/> — so the rule against a box
    /// round one module holds after an edit and not only when one is made.
    /// </summary>
    private void Forget(Guid nodeId)
    {
        if (Groups is null) return;

        foreach (var group in Groups)
        {
            group.Members.Remove(nodeId);

            // And the sockets that were pointing at it. A socket names a module,
            // so one whose module has left the box is a socket onto nothing —
            // SocketsOf would decline to draw it either way, and leaving it here
            // would be leaving it to come back if the module ever rejoined.
            group.Exposed.RemoveAll(s => s.Node == nodeId);
        }

        Groups.RemoveAll(g => g.Members.Count < NodeGroup.Fewest);

        if (Groups.Count == 0) Groups = null;
    }

    /// <summary>
    /// Every port of <paramref name="group"/> at which a wire crosses its
    /// boundary — see <see cref="GroupSockets"/> for what is and is not one.
    /// </summary>
    /// <remarks>
    /// A wire crossing now and <see cref="NodeGroup.Exposed"/> each put a socket
    /// there: the first is why a box round a wired chain arrives with an edge on
    /// it, the second why taking a wire off leaves the socket. Ordered down the
    /// canvas and then across it, and free to change on the next edit, since a
    /// row's position is never written down.
    /// </remarks>
    public GroupSockets SocketsOf(NodeGroup group)
    {
        var inside = group.Members.ToHashSet();

        var arriving = new List<GroupSocket>();
        var leaving = new List<GroupSocket>();

        foreach (var wire in Connections)
        {
            var from = inside.Contains(wire.SourceNode);
            var to = inside.Contains(wire.TargetNode);

            // Both ends inside is a wire the box hides; both ends outside is a
            // wire it has nothing to do with. Only one of each is a crossing.
            if (from == to) continue;

            if (to)
            {
                var socket = new GroupSocket(wire.TargetNode, wire.TargetPort, IsOutput: false);
                if (!arriving.Contains(socket)) arriving.Add(socket);
            }
            else
            {
                var socket = new GroupSocket(wire.SourceNode, wire.SourcePort, IsOutput: true);
                if (!leaving.Contains(socket)) leaving.Add(socket);
            }
        }

        foreach (var socket in group.Exposed)
        {
            // Dropped rather than drawn where it names a module that has left the
            // group or gone from the patch, or a port that module no longer has —
            // a socket onto nothing is worse than a socket that is missing.
            if (!inside.Contains(socket.Node)) continue;
            if (Find(socket.Node) is not { } node) continue;
            if (NodeCatalog.Get(node.TypeId) is not { } def) continue;

            var side = socket.IsOutput ? leaving : arriving;
            var ports = socket.IsOutput ? def.Outputs : def.Inputs;

            if (socket.Port < ports.Count && !side.Contains(socket)) side.Add(socket);
        }

        arriving.Sort(Down);
        leaving.Sort(Down);

        return new GroupSockets(arriving, leaving);
    }

    /// <summary>
    /// Whether a wire is on this socket right now, which decides whether it can
    /// be taken off the edge — one that is wired comes straight back.
    /// </summary>
    public bool Wired(NodeGroup group, GroupSocket socket)
    {
        var inside = group.Members.ToHashSet();

        foreach (var wire in Connections)
        {
            if (socket.IsOutput)
            {
                if (wire.SourceNode == socket.Node
                    && wire.SourcePort == socket.Port
                    && !inside.Contains(wire.TargetNode))
                    return true;
            }
            else if (wire.TargetNode == socket.Node
                && wire.TargetPort == socket.Port
                && !inside.Contains(wire.SourceNode))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Down the canvas, then across it, then by port.</summary>
    private int Down(GroupSocket a, GroupSocket b)
    {
        var first = Find(a.Node);
        var second = Find(b.Node);

        if (first is null || second is null) return 0;

        var vertical = first.Y.CompareTo(second.Y);
        if (vertical != 0) return vertical;

        var horizontal = first.X.CompareTo(second.X);
        if (horizontal != 0) return horizontal;

        return a.Port.CompareTo(b.Port);
    }

    /// <summary>The first module of a type, or null where the patch has none.</summary>
    public NodeInstance? FirstOf(string typeId) => Nodes.FirstOrDefault(n => n.TypeId == typeId);

    /// <summary>
    /// The Output. Every patch has exactly one — <see cref="EnsureOutput"/> puts
    /// it there and <see cref="Remove"/> will not take it away. Ignored by the
    /// serialiser, which would otherwise write a second copy of a node already
    /// in <see cref="Nodes"/>.
    /// </summary>
    [JsonIgnore]
    public NodeInstance Output =>
        FirstOf(NodeCatalog.OutputTypeId)
        ?? throw new InvalidOperationException("This patch has no Output. Call EnsureOutput after building it by hand.");

    /// <summary>
    /// Whether another module of this type may be placed. Everything says yes
    /// but the Output, of which a patch has exactly one, always.
    /// </summary>
    /// <remarks>
    /// Compilation roots at the Output it finds and walks backwards (ADR-0011),
    /// so a second is never reached: whatever is wired into it looks connected,
    /// renders nothing, and raises no complaint, because the patch compiled.
    /// </remarks>
    public bool CanAdd(string typeId) => !NodeCatalog.IsSink(typeId);

    /// <summary>
    /// Puts the Output in place if it is not already there, and hands it back.
    /// Called on every patch that enters the program — built, loaded or
    /// assembled by a plugin — so nothing downstream has to cope with a patch
    /// that has no sink. Making it unremovable is what lets the shell hang every
    /// audio and video setting off it (ADR-0037).
    /// </summary>
    /// <param name="id">What to call one that has to be made — see <see cref="NodeInstance.Create"/>.</param>
    /// <param name="modules"></param>
    public NodeInstance EnsureOutput(ModuleCatalog? modules = null, Guid? id = null)
    {
        if (FirstOf(NodeCatalog.OutputTypeId) is { } existing) return existing;

        var catalog = modules ?? NodeCatalog.Current;
        var node = NodeInstance.Create(catalog.Require(NodeCatalog.OutputTypeId), OutputX, OutputY, id);

        Nodes.Add(node);
        return node;
    }

    /// <summary>
    /// Where a freshly made Output lands. To the right of centre, because the
    /// editor frames the whole patch and a sink is what everything else points at.
    /// </summary>
    private const double OutputX = 1100;
    private const double OutputY = 320;

    /// <summary>The wire feeding an input, if any. An input takes at most one.</summary>
    public Connection? IncomingTo(Guid node, int port) =>
        Connections.FirstOrDefault(c => c.TargetNode == node && c.TargetPort == port);

    /// <summary>
    /// The one wire leaving an output, or null where none does or several do.
    /// Not quite the mirror of <see cref="IncomingTo"/>: an input takes at most
    /// one wire, an output fans out, so this answers only where there is exactly
    /// one — lifting one of four would be picking for the user.
    /// </summary>
    public Connection? SoleOutgoingFrom(Guid node, int port)
    {
        Connection? only = null;

        foreach (var wire in Connections)
        {
            if (wire.SourceNode != node || wire.SourcePort != port) continue;
            if (only is not null) return null;

            only = wire;
        }

        return only;
    }

    /// <summary>
    /// Which halves of the Output have anything wired into them: whether there
    /// is a picture to see, and whether there is a sound to hear.
    /// </summary>
    /// <remarks>
    /// Both compile whatever the answer — an unwired sink is a flat color and
    /// silence. This is the question before that: whether writing a file of
    /// either would be writing anything at all.
    /// </remarks>
    public (bool Picture, bool Sound) Reaches()
    {
        if (FirstOf(NodeCatalog.OutputTypeId) is not { } sink) return (false, false);

        return (
            IncomingTo(sink.Id, NodeCatalog.OutputColorPort) is not null,
            IncomingTo(sink.Id, NodeCatalog.OutputLeftPort) is not null
            || IncomingTo(sink.Id, NodeCatalog.OutputRightPort) is not null);
    }

    /// <summary>
    /// Whether wiring <paramref name="source"/>'s output into
    /// <paramref name="target"/>'s input would close a loop the compiler refuses.
    /// </summary>
    /// <remarks>
    /// The new wire completes a loop exactly when the target can already reach
    /// the source going forward. The walk stops at every cycle breaker, and a
    /// wire leaving one is answered without walking: every loop it could complete
    /// runs through that breaker.
    /// </remarks>
    public bool WouldCycle(Guid source, Guid target, ModuleCatalog? modules = null)
    {
        var catalog = modules ?? NodeCatalog.Current;

        // Connect refuses a wire from a node to itself, so this agrees with it
        // rather than reporting a loop nothing can draw.
        if (source == target) return false;
        if (IsBreaker(source)) return false;

        var seen = new HashSet<Guid>();
        var walk = new Stack<Guid>();
        walk.Push(target);

        while (walk.Count > 0)
        {
            var at = walk.Pop();

            if (at == source) return true;
            if (!seen.Add(at) || IsBreaker(at)) continue;

            foreach (var wire in Connections)
                if (wire.SourceNode == at)
                    walk.Push(wire.TargetNode);
        }

        return false;

        bool IsBreaker(Guid id) =>
            Find(id) is { } node && catalog.Get(node.TypeId) is { IsCycleBreaker: true };
    }

    /// <summary>
    /// Wires two sockets together, replacing whatever already fed the target
    /// input. Outputs may fan out to any number of inputs.
    /// </summary>
    public void Connect(Guid sourceNode, int sourcePort, Guid targetNode, int targetPort)
    {
        if (sourceNode == targetNode) return;

        Connections.RemoveAll(c => c.TargetNode == targetNode && c.TargetPort == targetPort);
        Connections.Add(new Connection(sourceNode, sourcePort, targetNode, targetPort));

        // A wire drawn across a box's edge puts a socket there for good. Here
        // rather than at the canvas, so it holds for a wire made by an assistant
        // or a preset too — and deliberately not in Disconnect: taking the wire
        // off leaves the socket. See NodeGroup.Exposed.
        Cross(sourceNode, sourcePort, targetNode, targetPort);
    }

    /// <summary>
    /// Puts a socket on the edge of whichever box a new wire crosses, at each end
    /// of it that a box has an edge at.
    /// </summary>
    private void Cross(Guid sourceNode, int sourcePort, Guid targetNode, int targetPort)
    {
        if (Groups is null) return;

        var from = GroupOf(sourceNode);
        var to = GroupOf(targetNode);

        // A wire inside one box crosses nothing, so it puts no socket anywhere.
        if (ReferenceEquals(from, to)) return;

        from?.Expose(new GroupSocket(sourceNode, sourcePort, IsOutput: true));
        to?.Expose(new GroupSocket(targetNode, targetPort, IsOutput: false));
    }

    public void Disconnect(Guid targetNode, int targetPort) =>
        Connections.RemoveAll(c => c.TargetNode == targetNode && c.TargetPort == targetPort);

    /// <summary>
    /// Takes a module out, along with every wire touching it. The Output is
    /// refused: a patch cannot be without it.
    /// </summary>
    /// <returns>Whether anything was removed.</returns>
    public bool Remove(Guid nodeId)
    {
        if (Find(nodeId) is { } node && NodeCatalog.IsSink(node.TypeId)) return false;

        var removed = Nodes.RemoveAll(n => n.Id == nodeId) > 0;
        Connections.RemoveAll(c => c.SourceNode == nodeId || c.TargetNode == nodeId);

        // A box may not go on claiming to draw a module that is not there any
        // more, and one emptied by this stops existing — see Forget.
        if (removed) Forget(nodeId);

        return removed;
    }
}
