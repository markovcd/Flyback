using Flyback.App.Audio;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Audio;

/// <summary>
/// The seam between a compiled program and a sound device. What is worth
/// pinning here is not the arithmetic — the engine does none — but what happens
/// to the state around a program when the patch is edited while it plays.
/// </summary>
/// <remarks>
/// Every stateful op is identified by its position among the stateful ops
/// (ADR-0027, ADR-0030), so a program's memory only fits that program. The
/// engine is what decides whether an edit keeps the memory or starts again, and
/// getting that wrong is inaudible in every test that renders one buffer: it is
/// a click on each edit, which is exactly the failure ADR-0023 says is easy to
/// ship and hard to diagnose.
/// </remarks>
public class AudioEngineTests
{
    private const int BufferFrames = 512;

    /// <summary>
    /// Stands in for a sound card by keeping the callback and letting the test
    /// be the audio thread, so a buffer is produced when the test asks for one
    /// rather than on a clock.
    /// </summary>
    private sealed class LoopbackDevice : IAudioDevice
    {
        private AudioCallback? fill;

        public int SampleRate => GlobalConstants.SampleRate;

        public bool IsRunning => fill is not null;

        public void Start(AudioCallback callback) => fill = callback;

        public void Stop() => fill = null;

        public void Dispose() => Stop();

        public float[] Pump(int frames = BufferFrames)
        {
            var buffer = new float[frames * 2];
            (fill ?? throw new InvalidOperationException("The engine never started the device."))(buffer);
            return buffer;
        }
    }

    /// <summary>Time into a sine into the speakers — one phase accumulator, no delay lines.</summary>
    private static Patch Tone(float hz)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = builder.Add("time", 0, 0);
        var osc = builder.Add("osc.sine", 0, 0, (1, hz));
        var speaker = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));

        return builder
            .Wire(time, 0, osc, 0)
            .Wire(osc, 0, speaker, NodeCatalog.OutputLeftPort)
            .Patch;
    }

    private static float LeftAt(float[] buffer, int frame) => buffer[frame * 2];

    /// <summary>
    /// The largest step between one sample and the next, which is what a torn
    /// waveform shows up as — the same measurement the Note tests use.
    /// </summary>
    private static float LargestStep(IEnumerable<float[]> buffers)
    {
        var samples = buffers.SelectMany(b => Enumerable.Range(0, b.Length / 2).Select(f => LeftAt(b, f))).ToArray();
        var largest = 0f;

        for (var i = 1; i < samples.Length; i++)
            largest = MathF.Max(largest, MathF.Abs(samples[i] - samples[i - 1]));

        return largest;
    }

    /// <summary>
    /// The heart of it. An edit that leaves the program's shape alone must hand
    /// the running memory to the new program, so the sound is bit-for-bit what
    /// it would have been had nothing been edited at all. Passing fresh memory
    /// instead would restart every accumulator, which no assertion about a
    /// single buffer would notice.
    /// </summary>
    [Fact]
    public void An_edit_that_keeps_the_shape_leaves_the_sound_exactly_where_it_was()
    {
        var undisturbed = Play(edit: false);
        var recompiled = Play(edit: true);

        recompiled.ShouldBe(undisturbed);

        static float[] Play(bool edit)
        {
            using var device = new LoopbackDevice();
            using var engine = new AudioEngine(device);
            var patch = Tone(220f);

            engine.Update(patch);
            engine.Start();
            device.Pump();

            // The same patch object, recompiled: what a knob drag does, minus
            // the knob, so the only thing under test is the swap itself.
            if (edit) engine.Update(patch);

            return device.Pump();
        }
    }

    /// <summary>
    /// The same claim as a listener would put it, and the one that survives a
    /// change to how the memory is carried: an edit during playback bends the
    /// waveform rather than cutting it.
    /// </summary>
    [Fact]
    public void Turning_a_knob_during_playback_does_not_tear_the_waveform()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);
        var patch = Tone(220f);

        engine.Update(patch);
        engine.Start();

        var before = device.Pump();
        var settled = LargestStep([before]);

        // A semitone up, mid-playback. Phase is accumulated, so the wave's value
        // carries across the change and only its slope differs (ADR-0030).
        patch.Nodes[1].InputValues[1] = 233.08f;
        engine.Update(patch);

        var after = device.Pump();

        // Across the join, against the steady state of the higher pitch: a tear
        // is a step far larger than the wave's own travel, and a semitone moves
        // the travel by about 6%.
        LargestStep([before, after]).ShouldBeLessThan(settled * 1.5f);
    }

    /// <summary>
    /// A patch that has never been through <see cref="AudioEngine.Update"/> is
    /// still asked for buffers the moment the device starts, so the engine has
    /// to begin holding a program rather than a null.
    /// </summary>
    [Fact]
    public void An_engine_that_has_been_given_no_patch_plays_silence()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);

        engine.Start();

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        device.Pump().ShouldAllBe(v => v == 0f);
    }

    [Fact]
    public void A_patch_with_no_speaker_is_silent_rather_than_a_failure()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var tint = builder.Add("color.hsv", 0, 0);
        var screen = builder.Add(NodeCatalog.OutputTypeId, 0, 0);
        builder.Wire(tint, 0, screen, 0);

        engine.Update(builder.Patch);
        engine.Start();

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        device.Pump().ShouldAllBe(v => v == 0f);
    }

    /// <summary>
    /// Scan mode is not compiled in — it is read off the sink's knobs by name
    /// every time the patch is updated, so that lookup is load-bearing and
    /// silent when it misses. A patch driven by the coordinates hears nothing at
    /// all until scanning turns them into a sweep.
    /// </summary>
    [Fact]
    public void Scanning_is_read_off_the_sinks_knobs()
    {
        var still = Play(scan: 0f);
        var swept = Play(scan: 1f);

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        still.ShouldAllBe(v => v == 0f);
        swept.Any(v => MathF.Abs(v) > 0.01f).ShouldBeTrue("scanning should make the picture audible");

        static float[] Play(float scan)
        {
            using var device = new LoopbackDevice();
            using var engine = new AudioEngine(device);
            var builder = new PatchBuilder(NodeCatalog.BuiltIn);

            var coords = builder.Add("coord", 0, 0);
            var speaker = builder.Add(
                NodeCatalog.OutputTypeId, 0, 0,
                (NodeCatalog.OutputGainPort, 1f),
                (NodeCatalog.OutputScanPort, scan),
                (NodeCatalog.OutputScanRatePort, 60f));

            builder.Wire(coords, 0, speaker, NodeCatalog.OutputLeftPort);

            engine.Update(builder.Patch);
            engine.Start();

            return device.Pump(4_096);
        }
    }

    /// <summary>
    /// A Scope's chart comes out of the run that made the sound, so the engine
    /// is where the two paths meet: it holds the one reference that pairs a
    /// program with the memory it filled.
    /// </summary>
    /// <remarks>
    /// Worth a test here rather than only in the compiler's, because the whole
    /// hazard is the pairing. A caller reading the program and the memory
    /// separately could be handed a mismatched pair by a recompile in between,
    /// and what that produces is not an exception but a chart of the wrong node.
    /// </remarks>
    [Fact]
    public void A_scope_is_charted_from_what_the_engine_actually_played()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);

        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var held = builder.Add("value", 0, 0, (0, 0.5f));
        var scope = builder.Add(NodeCatalog.ScopeTypeId, 0, 0);
        builder.Add(NodeCatalog.OutputTypeId, 0, 0);

        builder.Wire(held, 0, scope, 0);

        var drawn = builder.Patch.CompileForProbe(scope.Id, NodeCatalog.BuiltIn).Program;

        engine.Update(builder.Patch);
        engine.Start();

        // Nothing played yet, so nothing charted: the promise is that it shows
        // what happened, never what would have.
        engine.Listen(drawn, LiveValues.None);
        drawn.Taps[0].Trace.Samples.ShouldAllBe(v => v == 0f);

        // A fiftieth of a second is under a thousand frames, so this fills the
        // whole window several times over.
        device.Pump(8_192);
        engine.Listen(drawn, LiveValues.None);

        drawn.Taps[0].Trace.Samples.ShouldAllBe(v => Math.Abs(v - 0.5f) < 1e-4f);
    }

    /// <summary>
    /// The other half of what the engine hands the picture, and the newer one: a
    /// Meter's reading, played into the block the frame reads rather than copied
    /// into a buffer the frame charts.
    /// </summary>
    /// <remarks>
    /// Through the real device loop rather than through <c>Meters</c> directly,
    /// because the thing being pinned is the engine's part of it: one read of the
    /// state, both blocks written, and nothing published before anything was
    /// played.
    /// </remarks>
    [Fact]
    public void A_meter_is_played_into_the_picture_from_what_the_engine_heard()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);

        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var held = builder.Add("value", 0, 0, (0, 0.5f));
        var meter = builder.Add(NodeCatalog.MeterTypeId, 0, 0);
        var output = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));

        builder.Wire(held, 0, meter, 0)
               .Wire(held, 0, output, NodeCatalog.OutputLeftPort)
               .Wire(meter, 0, output, NodeCatalog.OutputColorPort);

        var drawn = builder.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        var watching = new LiveValues(drawn.LiveInputs);

        engine.Update(builder.Patch);
        engine.Start();

        var level = Meters.Key(meter.Id, Meters.Level);

        // Nothing played yet, so nothing heard.
        engine.Listen(drawn, watching);
        watching.At(watching.Keys.ToList().IndexOf(level)).ShouldBe(0d);

        device.Pump(8_192);
        engine.Listen(drawn, watching);

        watching.At(watching.Keys.ToList().IndexOf(level)).ShouldBe(0.5d, 0.01d);

        // The speakers' block is written too, and names nothing here because
        // nothing in this patch's sound reads the level — a meter whose reading
        // only lights the picture is eliminated from the audio program like any
        // other unread module, and being written to by name costs it nothing.
        engine.Live.Reads(level).ShouldBeFalse();

        // And back to nothing when the sound is switched off, which is the one
        // place this differs from a Scope — a chart holds its last sweep.
        engine.Deafen(watching);
        watching.At(watching.Keys.ToList().IndexOf(level)).ShouldBe(0d);
    }

    /// <summary>The device is the engine's to hold, and closing one closes the other.</summary>
    [Fact]
    public void Disposing_the_engine_closes_the_device()
    {
        var device = new LoopbackDevice();
        var engine = new AudioEngine(device);

        engine.Start();
        device.IsRunning.ShouldBeTrue();

        engine.Dispose();

        device.IsRunning.ShouldBeFalse();
    }
}
