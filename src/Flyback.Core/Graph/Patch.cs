using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Flyback.Core.Graph;

/// <summary>
/// One note in a sequence: what it plays, how long it lasts and how loud it is.
/// </summary>
/// <param name="Value">A note number on a Note Sequencer, an ordinary signal on a Sequencer.</param>
/// <param name="Length">
/// In steps, so 1 is the ordinary step every note used to be and 2 is a note
/// held twice as long. Never zero — a note of no duration has nowhere to sound
/// and would divide by nothing when the gate asks how far through it we are.
/// </param>
/// <param name="Volume">
/// 0 to 1, and a level rather than a switch: a rest and a quiet note are the
/// same control, which is what makes it a velocity for free.
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
    /// The longest a module may be renamed to. Not a limit anybody working will
    /// meet — it is there so that a name pasted from somewhere else cannot make
    /// a patch file enormous or a header undrawable.
    /// </summary>
    public const int NameLimit = 26;

    /// <summary>
    /// How far from the origin a module may sit, on each axis, in graph units.
    /// The canvas is that square and nothing may be put outside it.
    /// </summary>
    /// <remarks>
    /// Ten thousand units across, which is about thirty modules wide at the
    /// spacing the layout uses and some six times the span of the largest preset
    /// in the box. Room to work in rather than room to get lost in: a module
    /// flung far enough away is a module that cannot be got back, because
    /// framing the patch clamps its zoom — past a certain distance pressing F
    /// shows an empty grid with the patch somewhere off it, and the only way
    /// back would be to close the file without saving.
    /// <para>
    /// Held on the coordinate rather than on the gesture, so it is true of a
    /// module however it was placed — dragged, pasted, laid out, read from a file
    /// somebody edited by hand, or positioned by an assistant that has never seen
    /// the canvas.
    /// </para>
    /// </remarks>
    public const double Extent = 5_000d;

    public required Guid Id { get; init; }

    public required string TypeId { get; init; }

    /// <summary>Where it sits. Always inside the canvas — see <see cref="Extent"/>.</summary>
    public double X
    {
        get;
        set => field = Inside(value);
    }

    /// <inheritdoc cref="X"/>
    public double Y
    {
        get;
        set => field = Inside(value);
    }

    /// <summary>
    /// What this one has been renamed to, and null where it has not been — which
    /// is nearly always, so it is null rather than a copy of the definition's
    /// name and an unrenamed module writes no name into the file at all.
    /// </summary>
    /// <remarks>
    /// A label and nothing more: the compiler roots at the sink and reaches
    /// modules through wires, so nothing is ever found by name and two modules
    /// called the same thing is no more a problem than two called nothing.
    /// <para>
    /// Set through <see cref="Rename"/>, which is what makes "null means the
    /// definition's name" true of every module rather than of the ones that
    /// happened to go through the inspector.
    /// </para>
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Per-input constants, used for any input with nothing wired into it.
    /// Length always matches the definition's input count.
    /// </summary>
    public float[] InputValues { get; set; } = [];

    /// <summary>
    /// The notes a sequencer plays, and null for every other module — the one
    /// thing an instance carries that is not a knob.
    /// </summary>
    /// <remarks>
    /// ADR-0031 refused exactly this and was right to at the time: a pattern of
    /// eight fixed steps is a row of knobs, and knobs already existed. A pattern
    /// that can be inserted into, deleted from and reordered is a list, and no
    /// arrangement of a fixed <see cref="InputValues"/> is one. See ADR-0038 for
    /// what that costs — a step is no longer a socket.
    /// </remarks>
    public List<Step>? Steps { get; set; }

    /// <summary>
    /// Which notes of the octave a quantiser will snap to, and null for every
    /// module that snaps to none — the second thing an instance carries that is
    /// not a knob.
    /// </summary>
    /// <remarks>
    /// Pitch classes, so a scale names every A rather than one of them, and a
    /// set rather than a sequence — see <see cref="Pitch.Scale"/>. Not
    /// <see cref="Steps"/> reused, though the two are both lists on a node: a
    /// tune is ordered and may repeat a note, a scale is neither, and a Step
    /// would carry a length and a volume that mean nothing here. Not
    /// <see cref="InputValues"/> either, for ADR-0038's reason — twelve sockets
    /// for twelve switches is a module nobody can read, and none of the twelve
    /// is a thing a patch could sensibly drive.
    /// </remarks>
    public List<int>? Scale { get; set; }

    /// <summary>
    /// The audio file a player reads, and null for every module that reads none
    /// — the third thing an instance carries that is not a knob, and the first
    /// that is not a number.
    /// </summary>
    /// <remarks>
    /// A path rather than the audio. A patch is a document that is snapshotted
    /// whole on every edit and kept two hundred deep (see
    /// <see cref="PatchHistory"/>), and a megabyte of PCM in it would be a
    /// megabyte per undo step and a re-serialisation of it per knob turn. The
    /// cost of the other choice is that a patch is no longer self-contained,
    /// which is the one thing this gives up and is why a file that has gone is
    /// reported by name rather than passed over.
    /// <para>
    /// It passes the test ADR-0051 set for a third field of this kind: it is a
    /// decision about the piece rather than a signal in it, and there is no
    /// arrangement of sockets that expresses it — a socket carries a number, and
    /// this is not one.
    /// </para>
    /// </remarks>
    public string? Sample { get; set; }

    /// <summary>
    /// What a plugin's own kind of extra carries, keyed by
    /// <see cref="NodeExtra.Key"/>, and null for every module whose extras are
    /// the engine's own.
    /// </summary>
    /// <remarks>
    /// The open half of the store. <see cref="Steps"/>, <see cref="Scale"/> and
    /// <see cref="Sample"/> are the kinds the engine ships and they stay typed
    /// fields — a saved patch reads as it always did, and the emit functions that
    /// use them are unchanged. Anything a plugin invents lands here instead,
    /// because this class is sealed and a plugin cannot add a field to it.
    /// <para>
    /// <see cref="JsonNode"/> rather than a typed value, because the engine does
    /// not know what shape a plugin's kind is and must round-trip it without
    /// understanding it. It also keeps the file ordinary: an extra writes an
    /// object under its own name, which reads and edits like the rest of the
    /// patch. Turning it into something an emit function can use is
    /// <see cref="NodeExtra.Fold"/>'s, and so is tolerating a file somebody
    /// edited into a shape that means nothing — the same contract
    /// <see cref="Step.Sane"/> has.
    /// </para>
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
    /// and taking it away again when the last entry goes — so a module that
    /// carries nothing writes no empty object into the file.
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
    /// One coordinate held inside the canvas.
    /// </summary>
    /// <remarks>
    /// Not a number at all becomes the origin rather than the near edge: NaN is
    /// not far away in some direction, it is a coordinate that was never
    /// computed, and it would otherwise poison every comparison that looks for
    /// the corners of a patch. Infinity is genuinely far away in a direction and
    /// lands on the edge like any other overshoot.
    /// </remarks>
    private static double Inside(double value) =>
        double.IsNaN(value) ? 0d : Math.Clamp(value, -Extent, Extent);

    /// <summary>
    /// What to call this one: the name it was given, or its definition's where
    /// it was given none. The one way anything should ask, so that a renamed
    /// module reads the same on the canvas, in the panel and in a complaint the
    /// compiler makes about it.
    /// </summary>
    public string Title(NodeDef def) => Name ?? def.Name;

    /// <summary>
    /// Renames this module, or puts it back to its definition's name.
    /// </summary>
    /// <param name="def">The definition, which is what "no name" means.</param>
    /// <param name="to">
    /// The new name. Blank puts it back — an empty box is how the panel asks for
    /// the default, and there is no other way to mean it. So is the definition's
    /// own name typed out: it is not a rename, and storing it would leave a file
    /// claiming a name that would change under it the day the module is renamed
    /// in the catalogue.
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
    /// Deep, so that what a clipboard holds is a picture of the patch as it was
    /// rather than a view onto one that goes on being edited: the lists are the
    /// whole of a module's settings, and sharing one would let a knob turned
    /// afterwards change what a paste produces.
    /// <para>
    /// The one place that names every field an instance carries, and deliberately
    /// so — it is the file those fields are declared in, and adding a field
    /// without copying it here is the mistake this being next to them is meant to
    /// prevent. Not routed through <see cref="NodeDef.Extras"/> like seeding is,
    /// because a copy must not need a definition: a fragment naming a module this
    /// build has no plugin for still has to keep its notes, and looking one up to
    /// find nothing would drop them without a word.
    /// </para>
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
        Steps = Steps is { } steps ? [.. steps] : null,
        Scale = Scale is { } scale ? [.. scale] : null,
        Sample = Sample,

        // Deep here too, and it has to be said explicitly: a JsonNode is a
        // mutable tree, so copying the dictionary alone would hand the copy
        // the very nodes the original goes on being edited through.
        State = State is { } held
            ? held.ToDictionary(entry => entry.Key, entry => entry.Value.DeepClone())
            : null,
    };

    public static NodeInstance Create(NodeDef def, double x, double y)
    {
        var node = new NodeInstance
        {
            Id = Guid.NewGuid(),
            TypeId = def.TypeId,
            X = x,
            Y = y,
            InputValues = [.. def.Inputs.Select(p => p.Default)],
        };

        // Whatever this module carries that is not a knob, each kind filling in
        // its own field — see NodeExtra. A module with none, which is nearly all
        // of them, leaves every one of them null.
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
    /// declared first so it is the first thing in the text and the first thing a
    /// reader can act on.
    /// </summary>
    /// <remarks>
    /// Null on a patch that has not been through <see cref="PatchIo.ToJson"/>,
    /// and on every file written before the stamp existed — which is why reading
    /// treats null as <see cref="PatchIo.FirstVersion"/> rather than as a fault.
    /// A patch in memory has no version of its own; it has whatever the file it
    /// last passed through said, and this is where that is kept.
    /// </remarks>
    public int? Version { get; set; }

    /// <summary>
    /// The plugins this patch cannot be opened without, stamped as it is
    /// written. Null rather than empty when it uses nothing but the modules that
    /// ship in the engine, so an ordinary patch file looks exactly as it always
    /// did and one saved by an older build still loads.
    /// </summary>
    public List<ModuleProvider>? Requires { get; set; }

    public List<NodeInstance> Nodes { get; set; } = [];

    public List<Connection> Connections { get; set; } = [];

    public NodeInstance? Find(Guid id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>The first module of a type, or null where the patch has none.</summary>
    public NodeInstance? FirstOf(string typeId) => Nodes.FirstOrDefault(n => n.TypeId == typeId);

    /// <summary>
    /// The Output. Every patch has exactly one — <see cref="EnsureOutput"/> puts
    /// it there and <see cref="Remove"/> will not take it away.
    /// </summary>
    /// <remarks>
    /// Ignored by the serialiser, which would otherwise write it as a second
    /// copy of a node already in <see cref="Nodes"/> — and would throw on the
    /// way past a patch that has not been given one yet.
    /// </remarks>
    [JsonIgnore]
    public NodeInstance Output =>
        FirstOf(NodeCatalog.OutputTypeId)
        ?? throw new InvalidOperationException("This patch has no Output. Call EnsureOutput after building it by hand.");

    /// <summary>
    /// Whether another module of this type may be placed. Everything says yes
    /// but the Output, of which a patch has exactly one, always.
    /// </summary>
    /// <remarks>
    /// A second Output is not a second screen. Compilation roots at the one it
    /// finds and walks backwards from it
    /// ([0011](0011-compile-backwards-from-output.md)), so a second is never
    /// reached: whatever is wired into it looks connected, renders nothing, and
    /// there is no complaint to read, because the patch compiled.
    /// </remarks>
    public bool CanAdd(string typeId) => !NodeCatalog.IsSink(typeId);

    /// <summary>
    /// Puts the Output in place if it is not already there, and hands it back.
    /// Called on every patch that enters the program — built, loaded or
    /// assembled by a plugin — so that nothing downstream has to cope with a
    /// patch that has no sink.
    /// </summary>
    /// <remarks>
    /// A patch is not a document that may or may not have somewhere to go: it is
    /// an instrument, and an instrument with no output is not a state worth
    /// being able to represent. Making it unremovable rather than merely
    /// re-addable is what lets the shell hang every audio and video setting off
    /// it — see ADR-0037.
    /// </remarks>
    public NodeInstance EnsureOutput(ModuleCatalog? modules = null)
    {
        if (FirstOf(NodeCatalog.OutputTypeId) is { } existing) return existing;

        var catalog = modules ?? NodeCatalog.Current;
        var node = NodeInstance.Create(catalog.Require(NodeCatalog.OutputTypeId), OutputX, OutputY);

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
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="IncomingTo"/>, and not quite its mirror:
    /// an input takes at most one wire by construction, and an output may fan
    /// out to as many as it likes. So "the wire from here" is a question with an
    /// answer only when there happens to be exactly one, and this says so rather
    /// than handing back the first of several — the editor lifts a wire off an
    /// output by it, and lifting one of four would be picking for the user.
    /// </remarks>
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
    /// silence, which are legal programs. What this is for is the question
    /// before that: whether writing a file of either would be writing anything
    /// at all. A patch with no Output has neither, and says so rather than
    /// throwing, because the callers are the ones deciding what to offer.
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
    /// Signal runs from a source to a target, so the new wire completes a loop
    /// exactly when the target can already reach the source by following wires
    /// forward. A loop with a cycle breaker anywhere on it is not one the compiler
    /// minds — the break is already in it — so the walk stops at every breaker it
    /// meets, and a wire leaving one is answered without walking at all: every
    /// loop such a wire could complete runs through the breaker it came from.
    /// <para>
    /// Kept here rather than in the editor because it is a fact about the graph,
    /// and the editor is not the only thing that assembles one.
    /// </para>
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
    }

    public void Disconnect(Guid targetNode, int targetPort) =>
        Connections.RemoveAll(c => c.TargetNode == targetNode && c.TargetPort == targetPort);

    /// <summary>
    /// Takes a module out, along with every wire touching it. The Output is
    /// refused: it is the one module a patch cannot be without, so there is no
    /// state in which removing it would be an edit rather than a mistake.
    /// </summary>
    /// <returns>Whether anything was removed.</returns>
    public bool Remove(Guid nodeId)
    {
        if (Find(nodeId) is { } node && NodeCatalog.IsSink(node.TypeId)) return false;

        var removed = Nodes.RemoveAll(n => n.Id == nodeId) > 0;
        Connections.RemoveAll(c => c.SourceNode == nodeId || c.TargetNode == nodeId);

        return removed;
    }
}
