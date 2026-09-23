using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Core.Specs.Support;

/// <summary>
/// State shared between the steps of one scenario. Reqnroll creates a fresh
/// instance per scenario and injects it into every binding class that asks for
/// it, so scenarios cannot leak into each other.
/// </summary>
/// <remarks>
/// Steps that build the patch never compile it. Whatever looks at the screen or
/// listens to the speakers compiles for that sink on the way, and again after
/// any edit.
/// </remarks>
public sealed class PatchContext
{
    /// <summary>
    /// The size scenarios render at unless they name one. Small enough to be
    /// quick and 16:9, so the aspect ratio a coordinate carries is the one a
    /// real window would give it.
    /// </summary>
    public const int Width = 32;

    public const int Height = 18;

    /// <summary>
    /// The rate the sound is heard at sample by sample. Low enough that a second is
    /// a thousand samples and a step in the feature files is a millisecond.
    /// </summary>
    public const int SampleRate = 1_000;

    private const string Video = "video";
    private const string Audio = "audio";

    private readonly Dictionary<string, Guid> named = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<double> heard = [];

    private CompileResult? result;
    private string? compiledFor;
    private DelayState? memory;
    private CompiledPatch? memoryFor;
    private LiveValues? live;
    private CompiledPatch? liveFor;

    /// <summary>What the scenarios call the drum machine whose clock a patch follows.</summary>
    public const string Machine = "midi:drum-machine";

    /// <summary>The drum machine's clock, as the shell would keep it.</summary>
    public MidiClock Clock { get; } = new();

    public Patch Patch { get; private set; } = new();

    /// <summary>Every sample of the left channel played so far, across every edit.</summary>
    public IReadOnlyList<double> Heard => heard;

    /// <summary>The highest frequency the scenario's tone reaches, which bounds how steeply a smooth wave can move.</summary>
    public double HighestFrequency { get; set; }

    // --- building -------------------------------------------------------------

    public NodeInstance Add(string name, string typeId)
    {
        var node = NodeInstance.Create(NodeCatalog.Require(typeId), 0, 0);
        Patch.Nodes.Add(node);
        named[name] = node.Id;
        Changed();
        return node;
    }

    /// <summary>Gives a module already in the patch a name the steps can use.</summary>
    public void Name(string name, NodeInstance node) => named[name] = node.Id;

    /// <summary>Swaps in the patch an undo, a reopened file or a text hands back. Names follow the ids.</summary>
    public void Replace(Patch patch)
    {
        Patch = patch;
        Changed();
    }

    public void Remove(string name)
    {
        Patch.Remove(Node(name).Id);
        Changed();
    }

    /// <summary>Places a node whose type the catalog does not know, as a patch from a newer Flyback would.</summary>
    public NodeInstance AddUnknown(string name, string typeId)
    {
        var node = new NodeInstance { Id = Guid.NewGuid(), TypeId = typeId, InputValues = [] };
        Patch.Nodes.Add(node);
        named[name] = node.Id;
        Changed();
        return node;
    }

    public NodeInstance Node(string name) =>
        named.TryGetValue(name, out var id)
            ? Patch.Find(id) ?? throw new KeyNotFoundException($"'{name}' is no longer in the patch.")
            : throw new KeyNotFoundException($"No node named '{name}' in this scenario.");

    public void Wire(string source, string sourcePort, string target, string targetPort)
    {
        Patch.Connect(
            Node(source).Id,
            PortIndex(Definition(source).Outputs, sourcePort, source, "output"),
            Node(target).Id,
            PortIndex(Definition(target).Inputs, targetPort, target, "input"));
        Changed();
    }

    /// <summary>Wires from an output by position, the only option when the source's type is unknown.</summary>
    public void Wire(string source, int sourcePort, string target, string targetPort)
    {
        Patch.Connect(Node(source).Id, sourcePort, Node(target).Id, PortIndex(Definition(target).Inputs, targetPort, target, "input"));
        Changed();
    }

    public void SetInput(string name, string port, float value)
    {
        Node(name).InputValues[PortIndex(Definition(name).Inputs, port, name, "input")] = value;
        Changed();
    }

    public float StoredInput(string name, string port) =>
        Node(name).InputValues[PortIndex(Definition(name).Inputs, port, name, "input")];

    /// <summary>Drops the stored values past the first few, as a patch saved before the module gained them.</summary>
    public void Truncate(string name, int count)
    {
        Node(name).InputValues = [.. Node(name).InputValues.Take(count)];
        Changed();
    }

    public void SwitchOff(string name)
    {
        Node(name).Off = true;
        Changed();
    }

    // --- compiling ------------------------------------------------------------

    public CompileResult Picture => CompiledFor(Video);

    public CompileResult Sound => CompiledFor(Audio);

    private CompileResult CompiledFor(string sink)
    {
        if (result is null || compiledFor != sink)
        {
            result = sink == Audio ? Patch.CompileForAudio() : Patch.CompileForVideo();
            compiledFor = sink;
        }

        return result;
    }

    private void Changed() => result = null;

    // --- the speakers ---------------------------------------------------------

    /// <summary>A short stereo buffer through the real renderer, oversampling and filters included.</summary>
    public float[] RenderAudio(int frames = 2_000)
    {
        var buffer = new float[frames * 2];
        new AudioRenderer().Render(Sound.Program, buffer);
        return buffer;
    }

    /// <summary>
    /// Evaluates the audio program straight, without the renderer's oversampling
    /// and filters, so a sample is exactly what the patch computed. Memory is
    /// carried to a rebuilt program the way the live engine carries it.
    /// </summary>
    public void Play(int samples)
    {
        var program = Sound.Program;

        if (!ReferenceEquals(memoryFor, program))
        {
            var fresh = new DelayState(program, SampleRate);
            if (memory is not null) fresh.Adopt(memory);
            memory = fresh;
            memoryFor = program;
        }

        var registers = program.AllocateRegisters();

        for (var i = 0; i < samples; i++)
        {
            program.Evaluate(0d, 0d, (double)heard.Count / SampleRate, registers, default, memory, live: Live);
            heard.Add(registers[program.OutputBase]);
        }
    }

    /// <summary>Plays on until <paramref name="seconds"/> of sound have been heard.</summary>
    public void PlayUntil(double seconds)
    {
        var wanted = (int)Math.Round(seconds * SampleRate);

        if (wanted > heard.Count) Play(wanted - heard.Count);
    }

    /// <summary>Seconds of sound heard so far.</summary>
    public double Now => (double)heard.Count / SampleRate;

    /// <summary>Puts the drum machine's clock where the program will read it.</summary>
    public void Push() => Clock.WriteTo(Live, Machine);

    /// <summary>
    /// The block the sound program reads its live inputs from, fresh for each
    /// program and filled with the drum machine's clock the way the shell fills
    /// a new block with what is already held.
    /// </summary>
    private LiveValues Live
    {
        get
        {
            var program = Sound.Program;

            if (live is null || !ReferenceEquals(liveFor, program))
            {
                live = new LiveValues(program.LiveInputs);
                liveFor = program;
                Clock.WriteTo(live, Machine);
            }

            return live;
        }
    }

    /// <summary>A stretch of the sound starting <paramref name="from"/> seconds in, from fresh memory.</summary>
    public double[] Listen(double from, int samples)
    {
        var program = Sound.Program;
        var fresh = new DelayState(program, SampleRate);
        var registers = program.AllocateRegisters();
        var heardHere = new double[samples];

        for (var i = 0; i < samples; i++)
        {
            program.Evaluate(0d, 0d, from + (double)i / SampleRate, registers, default, fresh);
            heardHere[i] = registers[program.OutputBase];
        }

        return heardHere;
    }

    /// <summary>Plays on until the sample at <paramref name="index"/> has been heard.</summary>
    public double SampleAt(int index)
    {
        if (heard.Count <= index) Play(index + 1 - heard.Count);
        return heard[index];
    }

    // --- the screen -----------------------------------------------------------

    /// <summary>
    /// Renders from a cold renderer each time, so a scenario that asks about
    /// frame 3 is not affected by one that asked about frame 1.
    /// </summary>
    public Frame Render(int frames = 1, int width = Width, int height = Height)
    {
        var program = Picture.Program;
        var renderer = new SynthRenderer();
        var stride = width * 4;
        var buffer = new byte[stride * height];

        for (var frame = 0; frame < frames; frame++)
            renderer.Render(program, 0f, width, height, buffer, stride);

        return new Frame(buffer, width, height);
    }

    /// <summary>
    /// Renders, clears the history the way the Rewind button does, then renders
    /// again, so the assertion is about Reset rather than about a fresh renderer.
    /// </summary>
    public Frame RenderAfterRewind(int before, int after)
    {
        var program = Picture.Program;
        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var buffer = new byte[stride * Height];

        for (var frame = 0; frame < before; frame++)
            renderer.Render(program, 0f, Width, Height, buffer, stride);

        renderer.Reset();

        for (var frame = 0; frame < after; frame++)
            renderer.Render(program, 0f, Width, Height, buffer, stride);

        return new Frame(buffer, Width, Height);
    }

    private NodeDef Definition(string name) => NodeCatalog.Require(Node(name).TypeId);

    private static int PortIndex(IReadOnlyList<PortSpec> ports, string name, string node, string kind)
    {
        for (var i = 0; i < ports.Count; i++)
            if (string.Equals(ports[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;

        throw new KeyNotFoundException(
            $"'{node}' has no {kind} called '{name}'. Available: {string.Join(", ", ports.Select(p => p.Name))}");
    }
}
