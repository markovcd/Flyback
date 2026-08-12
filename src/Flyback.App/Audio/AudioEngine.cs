using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;

namespace Flyback.App.Audio;

/// <summary>
/// Joins the compiled audio program to a sound device, and is the clock the
/// video preview follows while sound is playing. The device arrives from a
/// plugin, so this class is the last thing that is the same on every platform.
/// </summary>
/// <remarks>
/// The callback never locks. Everything it needs is reachable through a single
/// immutable <see cref="State"/> reference swapped with <see cref="Volatile"/>,
/// so a recompile mid-buffer is a clean switch rather than a torn read — the
/// same discipline ADR-0018 established for the video path.
/// </remarks>
public sealed class AudioEngine(IAudioDevice device) : IDisposable
{
    /// <summary>
    /// A program and everything that goes with it. The delay lines belong here
    /// rather than to the renderer because they are a property of one program:
    /// swapped separately, a callback still rendering the previous program would
    /// index into the new program's lines, and if the count differed at all — a
    /// Delay added or removed — that is a fault on the audio thread.
    /// </summary>
    private sealed record State(CompiledPatch Program, AudioScan Scan, DelayState? Memory);

    private readonly AudioRenderer renderer = new(device.SampleRate);
    private State activeState = new(CompiledPatch.Silent, AudioScan.TimeDriven, null);

    public bool IsRunning => device.IsRunning;

    /// <summary>Sample-accurate position, and the master timeline while sound is on.</summary>
    public double Time => renderer.Time;

    public void Start()
    {
        if (!device.IsRunning) device.Start(Fill);
    }

    public void Stop() => device.Stop();

    /// <summary>Rewinds the cursor and clears the decimation and DC filter state.</summary>
    public void Rewind() => renderer.Reset();

    /// <summary>
    /// Swaps in a freshly compiled patch. Sizing the register scratch and the
    /// delay lines happens here, on the UI thread, so the callback never has to
    /// allocate — and both go in with the program they belong to, in one write.
    /// </summary>
    public void Update(Patch patch)
    {
        var program = patch.CompileForAudio().Program;

        renderer.Prepare(program);

        // Reusing the lines when the shape has not changed is what keeps a delay
        // ringing through an edit; only adding or removing one cuts the tail.
        var memory = renderer.DelayMemoryFor(program, Volatile.Read(ref activeState).Memory);

        Volatile.Write(ref activeState, new State(program, ScanFor(patch), memory));
    }

    /// <summary>
    /// Scan mode is a property of how the program is driven, not of the program
    /// itself — x and y are inputs the caller supplies. So it is read off the
    /// node's knobs rather than compiled in.
    /// </summary>
    private static AudioScan ScanFor(Patch patch)
    {
        var sink = patch.Nodes.FirstOrDefault(n => n.TypeId == NodeCatalog.AudioOutputTypeId);
        var def = NodeCatalog.Get(NodeCatalog.AudioOutputTypeId);

        if (sink is null || def is null) return AudioScan.TimeDriven;

        var scan = Knob(sink, def, "scan");
        var rate = Knob(sink, def, "scan rate");

        return new AudioScan(scan >= 0.5f, MathF.Max(rate, 1f), SynthRenderer.AspectOf(16, 9));
    }

    private static float Knob(NodeInstance node, NodeDef def, string port)
    {
        for (var i = 0; i < def.Inputs.Count; i++)
            if (def.Inputs[i].Name == port)
                return i < node.InputValues.Length ? node.InputValues[i] : def.Inputs[i].Default;

        return 0f;
    }

    private void Fill(Span<float> buffer)
    {
        var state = Volatile.Read(ref activeState);
        renderer.Render(state.Program, buffer, state.Scan, state.Memory);
    }

    public void Dispose() => device.Dispose();
}
