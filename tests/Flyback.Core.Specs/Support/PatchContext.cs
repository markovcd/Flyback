using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Core.Specs.Support;

/// <summary>
/// State shared between the steps of one scenario. Reqnroll creates a fresh
/// instance per scenario and injects it into every binding class that asks for
/// it, so scenarios cannot leak into each other.
/// </summary>
public sealed class PatchContext
{
    private const int Width = 32;
    private const int Height = 18;

    private readonly PatchBuilder _builder = new();
    private readonly Dictionary<string, NodeInstance> _named = new(StringComparer.OrdinalIgnoreCase);

    private CompileResult? _result;

    public Patch Patch => _builder.Patch;

    public CompileResult Result =>
        _result ?? throw new InvalidOperationException("The patch has not been compiled yet.");

    public CompiledPatch Program => Result.Program;

    public NodeInstance Add(string name, string typeId)
    {
        var node = _builder.Add(typeId, 0, 0);
        _named[name] = node;
        return node;
    }

    /// <summary>Places a node whose type the catalogue does not know, as a stale saved patch would.</summary>
    public NodeInstance AddUnknown(string name, string typeId)
    {
        var node = new NodeInstance { Id = Guid.NewGuid(), TypeId = typeId, InputValues = [] };
        Patch.Nodes.Add(node);
        _named[name] = node;
        return node;
    }

    public NodeInstance Node(string name) =>
        _named.TryGetValue(name, out var node)
            ? node
            : throw new KeyNotFoundException($"No node named '{name}' in this scenario.");

    public NodeDef Definition(string name) => NodeCatalog.Require(Node(name).TypeId);

    public void Wire(string source, string sourcePort, string target, string targetPort) =>
        Patch.Connect(
            Node(source).Id,
            PortIndex(Definition(source).Outputs, sourcePort, source, "output"),
            Node(target).Id,
            PortIndex(Definition(target).Inputs, targetPort, target, "input"));

    /// <summary>
    /// Wires from a source port by position rather than by name, which is the
    /// only option when the source module's type is not in the catalogue.
    /// </summary>
    public void Wire(string source, int sourcePort, string target, string targetPort) =>
        Patch.Connect(
            Node(source).Id,
            sourcePort,
            Node(target).Id,
            PortIndex(Definition(target).Inputs, targetPort, target, "input"));

    public void SetInput(string name, string port, float value) =>
        Node(name).InputValues[PortIndex(Definition(name).Inputs, port, name, "input")] = value;

    public void Compile() => _result = PatchCompiler.Compile(Patch);

    public int CountOps(OpCode code) => Program.Ops.Count(op => op.Code == code);

    /// <summary>
    /// Renders from a cold renderer each time, so a scenario that asks about
    /// frame 3 is not affected by one that asked about frame 1.
    /// </summary>
    public (float R, float G, float B) RenderCentre(int frames)
    {
        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var buffer = new byte[stride * Height];

        for (var frame = 0; frame < frames; frame++)
            renderer.Render(Program, 0f, Width, Height, buffer, stride);

        var pixel = (Height / 2) * stride + (Width / 2) * 4;
        return (buffer[pixel + 2] / 255f, buffer[pixel + 1] / 255f, buffer[pixel + 0] / 255f);
    }

    public float StoredInput(string name, string port) =>
        Node(name).InputValues[PortIndex(Definition(name).Inputs, port, name, "input")];

    /// <summary>
    /// Renders, clears the feedback history the way the Rewind button does, then
    /// renders again — so the assertion is about Reset rather than about this
    /// helper handing back a fresh renderer.
    /// </summary>
    public (float R, float G, float B) RenderCentreAfterReset(int before, int after)
    {
        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var buffer = new byte[stride * Height];

        for (var frame = 0; frame < before; frame++)
            renderer.Render(Program, 0f, Width, Height, buffer, stride);

        renderer.Reset();

        for (var frame = 0; frame < after; frame++)
            renderer.Render(Program, 0f, Width, Height, buffer, stride);

        var pixel = (Height / 2) * stride + (Width / 2) * 4;
        return (buffer[pixel + 2] / 255f, buffer[pixel + 1] / 255f, buffer[pixel + 0] / 255f);
    }

    public bool RenderedFrameIsBlack(int frames)
    {
        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var buffer = new byte[stride * Height];

        for (var frame = 0; frame < frames; frame++)
            renderer.Render(Program, 0f, Width, Height, buffer, stride);

        for (var i = 0; i < buffer.Length; i += 4)
            if (buffer[i] != 0 || buffer[i + 1] != 0 || buffer[i + 2] != 0)
                return false;

        return true;
    }

    private static int PortIndex(IReadOnlyList<PortSpec> ports, string name, string node, string kind)
    {
        for (var i = 0; i < ports.Count; i++)
            if (string.Equals(ports[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;

        throw new KeyNotFoundException(
            $"'{node}' has no {kind} called '{name}'. Available: {string.Join(", ", ports.Select(p => p.Name))}");
    }
}
