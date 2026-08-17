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
    public required Guid Id { get; init; }

    public required string TypeId { get; init; }

    public double X { get; set; }

    public double Y { get; set; }

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

    public static NodeInstance Create(NodeDef def, double x, double y) => new()
    {
        Id = Guid.NewGuid(),
        TypeId = def.TypeId,
        X = x,
        Y = y,
        InputValues = [.. def.Inputs.Select(p => p.Default)],
        Steps = def.DefaultSteps is { } notes ? [.. notes] : null,
    };
}

/// <summary>A wire from one node's output socket to another node's input socket.</summary>
public sealed record Connection(Guid SourceNode, int SourcePort, Guid TargetNode, int TargetPort);

/// <summary>The whole document: modules plus the wires between them.</summary>
public sealed class Patch
{
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
