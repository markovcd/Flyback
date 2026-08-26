using Flyback.App.Capture;
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
    /// A program and everything that goes with it. The memory belongs here rather
    /// than to the renderer because it is a property of one program: swapped
    /// separately, a callback still rendering the previous program would index
    /// into the new program's lines and phases, and if either count differed at
    /// all — a Delay or an oscillator added or removed — that is a fault on the
    /// audio thread.
    /// </summary>
    /// <param name="Live">
    /// What the patch is being played with. Here for the same reason the memory
    /// is: it is sized from one program's live inputs, so a callback still
    /// rendering the previous program must be reading that program's block and
    /// not the new one's.
    /// </param>
    private sealed record State(
        CompiledPatch Program,
        AudioScan Scan,
        DelayState? Memory,
        LiveValues Live);

    private readonly AudioRenderer renderer = new(device.SampleRate);
    private State activeState = new(CompiledPatch.Silent, AudioScan.TimeDriven, null, LiveValues.None);
    private IAudioSink? capture;

    public bool IsRunning => device.IsRunning;

    /// <summary>The rate the device actually opened at, which a recording has to match.</summary>
    public int SampleRate => device.SampleRate;

    /// <summary>
    /// Where a recording listens, while one is running. A reference, swapped the
    /// same way the program is, so the callback sees one sink or none and never
    /// half of a change.
    /// </summary>
    internal IAudioSink? Capture
    {
        get => Volatile.Read(ref capture);
        set => Volatile.Write(ref capture, value);
    }

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
    /// program's memory happens here, on the UI thread, so the callback never has
    /// to allocate — and both go in with the program they belong to, in one write.
    /// </summary>
    public void Update(Patch patch, ISampleLibrary? samples = null)
    {
        var program = patch.CompileForAudio(samples: samples).Program;

        renderer.Prepare(program);

        // Reusing it when the shape has not changed is what keeps a delay ringing
        // and the oscillators in phase through an edit; only adding or removing a
        // stateful op cuts the tail and restarts the tone.
        var memory = renderer.DelayMemoryFor(program, Volatile.Read(ref activeState).Memory);

        // A fresh block rather than the old one carried over, even where the
        // program asks for the same inputs. What was being held is written back
        // into it at once by whoever is following, so nothing is dropped, and
        // sharing one across a swap would mean the callback reading a block being
        // resized under it.
        var live = new LiveValues(program.LiveInputs);

        Volatile.Write(ref activeState, new State(program, ScanFor(patch), memory, live));
    }

    /// <summary>
    /// The block whoever is playing should be writing into — the one belonging to
    /// the program that is actually being heard.
    /// </summary>
    /// <remarks>
    /// Read after every <see cref="Update"/>, because each one makes a new one:
    /// a recompile may have added a module listening to something, and a block
    /// sized for the program before it has nowhere to put that. What was held
    /// across the edit is written in again by whoever is following — see
    /// <c>MidiHub.Follow</c>.
    /// </remarks>
    public LiveValues Live => Volatile.Read(ref activeState).Live;

    /// <summary>
    /// Refills every Scope in <paramref name="drawn"/> from what has been played
    /// since this last ran.
    /// </summary>
    /// <remarks>
    /// The one place the two programs of a patch meet while both are running,
    /// and it belongs here because the rings belong to the state this swaps: a
    /// caller holding the audio program and its memory separately could be
    /// handed a mismatched pair by a recompile between the two reads. One
    /// <see cref="Volatile"/> read, exactly as the callback takes.
    /// </remarks>
    public void RefreshTraces(CompiledPatch drawn)
    {
        var state = Volatile.Read(ref activeState);
        Traces.Refresh(drawn, state.Program, state.Memory);
    }

    /// <summary>
    /// Scan mode is a property of how the program is driven, not of the program
    /// itself — x and y are inputs the caller supplies. So it is read off the
    /// node's knobs rather than compiled in.
    /// </summary>
    private static AudioScan ScanFor(Patch patch)
    {
        var sink = patch.FirstOf(NodeCatalog.OutputTypeId);
        var def = NodeCatalog.Get(NodeCatalog.OutputTypeId);

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
        renderer.Render(state.Program, buffer, state.Scan, state.Memory, state.Live);

        // After the render and before anything else, so what is recorded is what
        // was heard — the same samples, not a second evaluation that would drift
        // from them the moment a knob moved between the two.
        Volatile.Read(ref capture)?.WriteAudio(buffer);
    }

    public void Dispose() => device.Dispose();
}
