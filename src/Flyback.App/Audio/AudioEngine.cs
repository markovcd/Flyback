using Flyback.App.Capture;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;

namespace Flyback.App.Audio;

/// <summary>
/// Joins the compiled audio program to a sound device, and is the clock the video
/// preview follows while sound is playing. The device arrives from a plugin, so
/// this class is the last thing that is the same on every platform.
/// </summary>
/// <remarks>
/// The callback never locks: everything it needs is reachable through a single
/// immutable <see cref="State"/> reference swapped with <see cref="Volatile"/>, so
/// a recompile mid-buffer is a clean switch rather than a torn read.
/// </remarks>
public sealed class AudioEngine(IAudioDevice device) : IDisposable
{
    /// <summary>
    /// A program and everything that goes with it. The memory belongs here rather
    /// than to the renderer because it is a property of one program: swapped
    /// separately, a callback still rendering the previous program would index into
    /// the new program's lines and phases.
    /// </summary>
    /// <param name="Live">
    /// What the patch is being played with. Here for the same reason the memory is:
    /// it is sized from one program's live inputs.
    /// </param>
    private sealed record State(
        CompiledPatch Program,
        DelayState? Memory,
        LiveValues Live);

    // The shape a sound export is told it has, so a patch reading Coordinates'
    // aspect sounds the same played as written.
    private readonly AudioRenderer renderer = new(device.SampleRate) { Aspect = SynthRenderer.AspectOf(16, 9) };
    private State activeState = new(CompiledPatch.Silent, null, LiveValues.None);
    private IAudioSink? capture;

    // One while a rewind is waiting for the callback to carry it out.
    private int rewindPending;

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

    /// <summary>
    /// Takes the sound back to nought: the cursor, the decimation and DC filter
    /// state, and the memory of the program that is playing.
    /// </summary>
    /// <remarks>
    /// The memory has to go with the cursor. It holds the clock as it last read,
    /// which is how a stateful module measures the interval, and a clock that says
    /// minutes against a cursor at nought is an interval of minus minutes — an
    /// envelope takes that as one enormous step and jumps to the rails, and decays
    /// from there at its own rate.
    /// <para>
    /// Asked for here and done by the callback, because the callback is the one
    /// thread that may touch either while the device runs. A stopped device has no
    /// callback to do it, so it is done at once as well; doing it again when the
    /// device starts clears what is already clear.
    /// </para>
    /// </remarks>
    public void Rewind()
    {
        Volatile.Write(ref rewindPending, 1);

        if (!device.IsRunning) Restart(Volatile.Read(ref activeState));
    }

    private void Restart(State state)
    {
        renderer.Reset();
        state.Memory?.Clear();
    }

    /// <summary>What turns each program swapped in here into IL, or null for a program that is only ever interpreted.</summary>
    public IlCompiler? Compiler { get; init; }

    /// <summary>
    /// Swaps in a freshly compiled patch. Sizing the register scratch and the
    /// program's memory happens here, on the UI thread, so the callback never has
    /// to allocate — and both go in with the program they belong to, in one write.
    /// </summary>

    public void Update(Patch patch, ISampleLibrary? samples = null)
    {
        var program = patch.CompileForAudio(samples: samples).Program;

        // Interpreted from the first buffer; the compiler attaches IL to this same
        // program when it has some, and the callback picks it up on the next buffer.
        Compiler?.Submit(program, IlLane.Sound);

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

        Volatile.Write(ref activeState, new State(program, memory, live));
    }

    /// <summary>
    /// The block whoever is playing should be writing into — the one belonging to
    /// the program that is actually being heard.
    /// </summary>
    /// <remarks>
    /// Read after every <see cref="Update"/>, because each one makes a new one: a
    /// recompile may have added a module listening to something. What was held
    /// across the edit is written in again by whoever is following — see
    /// <c>MidiHub.Follow</c>.
    /// </remarks>
    public LiveValues Live => Volatile.Read(ref activeState).Live;

    /// <summary>
    /// Hands the picture what has been played since this last ran: every Scope in
    /// <paramref name="drawn"/> refilled, and every Meter's reading played into
    /// <paramref name="watching"/>.
    /// </summary>
    /// <remarks>
    /// The one place the two programs meet while both are running, and it belongs
    /// here because the rings belong to the state this swaps — a caller holding the
    /// program and its memory separately could be handed a mismatched pair. Both
    /// blocks are written, not just the screen's, because a level may be wired back
    /// into the sound as readily as into the picture.
    /// </remarks>
    public void Listen(CompiledPatch drawn, LiveValues watching)
    {
        var state = Volatile.Read(ref activeState);

        Traces.Refresh(drawn, state.Program, state.Memory);
        Meters.Refresh(state.Program, state.Memory, watching, state.Live);
    }

    /// <summary>
    /// Every Meter back to nothing, for when the sound is switched off while the
    /// picture goes on being drawn. The one place this differs from a Scope, which
    /// holds its last sweep: a level that stayed where it was would be a picture lit
    /// by a sound that is not playing.
    /// </summary>
    public void Deafen(LiveValues watching)
    {
        var state = Volatile.Read(ref activeState);

        Meters.Silence(state.Program, watching, state.Live);
    }

    private void Fill(Span<float> buffer)
    {
        var state = Volatile.Read(ref activeState);

        if (Interlocked.Exchange(ref rewindPending, 0) == 1) Restart(state);

        renderer.Render(state.Program, buffer, state.Memory, state.Live);

        // After the render and before anything else, so what is recorded is what
        // was heard — the same samples, not a second evaluation that would drift
        // from them the moment a knob moved between the two.
        Volatile.Read(ref capture)?.WriteAudio(buffer);
    }

    public void Dispose() => device.Dispose();
}
