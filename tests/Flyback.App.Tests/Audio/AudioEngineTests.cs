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
/// The seam between a compiled program and a sound device. What is worth pinning
/// is not the arithmetic — the engine does none — but what happens to the state
/// around a program when the patch is edited while it plays.
/// </summary>
/// <remarks>
/// Every stateful op is identified by its position among the stateful ops, so a
/// program's memory only fits that program. Getting the decision wrong is
/// inaudible in any test that renders one buffer: it is a click on each edit.
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
        var speaker = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));

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
    /// The speakers have no frame, so the engine is told which one they are the
    /// sound of — <see cref="AudioEngine.Aspect"/> — and a patch reading
    /// Coordinates' aspect hears exactly that number (ADR-0083). The engine
    /// itself picks nothing: a fresh one hears <see cref="AudioRenderer"/>'s own
    /// default of a square frame until something sets it.
    /// </summary>
    [Fact]
    public void The_engine_plays_a_patch_as_the_sound_of_whatever_frame_it_is_told()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = builder.Add("time", 0, 0);
        var coords = builder.Add("coord", 0, 0);
        var osc = builder.Add("osc.sine", 0, 0, (1, 220f));
        var scaled = builder.Add("math.mul", 0, 0);
        var speaker = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 0.5f));

        builder.Wire(time, 0, osc, 0)
            .Wire(osc, 0, scaled, 0)
            .Wire(coords, NodeCatalog.CoordAspectPort, scaled, 1)
            .Wire(scaled, 0, speaker, NodeCatalog.OutputLeftPort);

        engine.Update(builder.Patch);
        engine.Start();

        device.Pump(4_096).Max(MathF.Abs).ShouldBe(0.5f, 0.02f, "a fresh engine has not been told any shape but square");

        engine.Aspect = 16f / 9f;

        device.Pump(4_096).Max(MathF.Abs).ShouldBe(0.5f * 16f / 9f, 0.02f, "and hears whatever shape it is given next");
    }

    /// <summary>
    /// A Scope's chart comes out of the run that made the sound, so the engine is
    /// where the two paths meet: it holds the one reference that pairs a program
    /// with the memory it filled.
    /// </summary>
    /// <remarks>
    /// Here rather than only in the compiler's tests, because the hazard is the
    /// pairing: a caller reading the two separately could be handed a mismatched
    /// pair by a recompile, and what that produces is a chart of the wrong node.
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
    /// The other half of what the engine hands the picture: a Meter's reading,
    /// played into the block the frame reads rather than copied into a buffer.
    /// Through the real device loop, because what is pinned is the engine's part —
    /// one read of the state, both blocks written, nothing published before
    /// anything was played.
    /// </summary>
    [Fact]
    public void A_meter_is_played_into_the_picture_from_what_the_engine_heard()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);

        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var held = builder.Add("value", 0, 0, (0, 0.5f));
        var meter = builder.Add(NodeCatalog.MeterTypeId, 0, 0);
        var output = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));

        builder.Wire(held, 0, meter, 0)
               .Wire(held, 0, output, NodeCatalog.OutputLeftPort)
               .Wire(meter, 0, output, NodeCatalog.OutputColorPort);

        var drawn = builder.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        var watching = new LiveValues(drawn.LiveInputs);

        engine.Update(builder.Patch);
        engine.Start();

        var level = MeterSignals.Key(meter.Id, MeterSignals.Level);

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

    /// <summary>
    /// A rewind is the patch starting again, so what it plays after one is what a
    /// fresh engine plays. In key because its envelope measures the interval from
    /// a clock it remembers: left holding the old time, the first step after the
    /// rewind is minus several seconds and the envelope leaps to the rails.
    /// </summary>
    [Fact]
    public void A_rewind_plays_the_patch_as_it_first_began()
    {
        var patch = Presets.InKey(NodeCatalog.BuiltIn);

        using var fresh = new LoopbackDevice();
        using var first = new AudioEngine(fresh);
        first.Update(patch);
        first.Start();
        var opening = fresh.Pump(4_096);

        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);
        engine.Update(patch);
        engine.Start();

        for (var i = 0; i < 200; i++) device.Pump();

        engine.Rewind();

        device.Pump(4_096).ShouldBe(opening);
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

    /// <summary>
    /// Picking another output on Save moves the sound rather than restarting it: the
    /// new device plays exactly what the old one would have played next (ADR-0085).
    /// </summary>
    [Fact]
    public void A_new_device_carries_on_from_where_the_old_one_stopped()
    {
        var patch = Tone(440);

        using var only = new LoopbackDevice();
        using var unbroken = new AudioEngine(only);
        unbroken.Update(patch);
        unbroken.Start();

        for (var i = 0; i < 10; i++) only.Pump();

        var expected = only.Pump();

        var speakers = new LoopbackDevice();
        using var headphones = new LoopbackDevice();
        using var engine = new AudioEngine(speakers);
        engine.Update(patch);
        engine.Start();

        for (var i = 0; i < 10; i++) speakers.Pump();

        engine.Stop();
        engine.Use(headphones).ShouldBeTrue();
        engine.Start();

        headphones.Pump().ShouldBe(expected);
    }

    [Fact]
    public void A_device_is_not_changed_under_a_running_callback()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);
        using var other = new LoopbackDevice();

        engine.Start();

        Should.Throw<InvalidOperationException>(() => engine.Use(other));
    }

    /// <summary>
    /// The renderer is built for one rate, so a device at another is refused and the
    /// one already there is kept — not disposed from under the caller.
    /// </summary>
    [Fact]
    public void A_device_at_another_rate_is_refused()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);
        using var slower = new SilentAudioDevice(GlobalConstants.SampleRate / 2);

        engine.Use(slower).ShouldBeFalse();

        engine.Start();
        device.IsRunning.ShouldBeTrue();
    }
    /// <summary>The loudest sample in <paramref name="buffer"/>.</summary>
    private static float Peak(float[] buffer) => buffer.Max(MathF.Abs);

    /// <summary>Buffers enough to cover <paramref name="time"/>.</summary>
    private static int BuffersFor(TimeSpan time) =>
        (int)Math.Ceiling(time.TotalSeconds * GlobalConstants.SampleRate / BufferFrames);

    /// <summary>
    /// A preset heard from the gallery begins at nothing and swells, and never
    /// reaches the level the patch itself plays at.
    /// </summary>
    [Fact]
    public void An_audition_swells_in_and_stays_quieter_than_a_patch()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);

        engine.Start();
        engine.StartAudition(engine.PrepareAudition(Tone(220f)).ShouldNotBeNull());

        var first = device.Pump();
        var buffers = Enumerable.Range(0, BuffersFor(AudioEngine.AuditionFadeIn) + 4).Select(_ => device.Pump()).ToList();

        Peak(first).ShouldBeLessThan(0.01f);
        Peak(buffers[buffers.Count / 4]).ShouldBeLessThan(Peak(buffers[^1]));
        Peak(buffers[^1]).ShouldBe(AudioEngine.AuditionLevel, tolerance: 0.02f);
        LargestStep([first, .. buffers]).ShouldBeLessThan(0.05f);
    }

    /// <summary>
    /// The patch that was playing goes quiet while a preset is auditioned, rather
    /// than the two being heard at once, and comes back when it ends.
    /// </summary>
    [Fact]
    public void A_patch_is_faded_out_under_an_audition_and_back_in_after()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);

        engine.Update(Tone(220f));
        engine.Start();
        var playing = Peak(device.Pump());

        // The same preset heard with nothing under it, to tell the patch apart from it.
        using var aloneDevice = new LoopbackDevice();
        using var alone = new AudioEngine(aloneDevice);
        alone.Start();

        engine.StartAudition(engine.PrepareAudition(Tone(330f)).ShouldNotBeNull());
        alone.StartAudition(alone.PrepareAudition(Tone(330f)).ShouldNotBeNull());
        engine.IsAuditioning.ShouldBeTrue();

        var fading = BuffersFor(AudioEngine.AuditionFadeOut) + 1;
        var fadingOut = Enumerable.Range(0, fading).Select(_ => device.Pump()).ToList();
        for (var i = 0; i < fading; i++) aloneDevice.Pump();

        // Once the patch has faded, what is heard is the preset and nothing else.
        var faded = device.Pump();
        faded.ShouldBe(aloneDevice.Pump(), tolerance: 1e-6f);

        engine.EndAudition();
        engine.IsAuditioning.ShouldBeFalse();

        var back = Enumerable.Range(0, 2 * BuffersFor(AudioEngine.AuditionFadeOut) + 1).Select(_ => device.Pump()).ToList();

        LargestStep([.. fadingOut, faded, .. back]).ShouldBeLessThan(0.05f);
        Peak(back[^1]).ShouldBe(playing, tolerance: 0.01f);
    }

    [Fact]
    public void A_patch_that_makes_no_sound_is_not_auditioned()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(device);

        engine.PrepareAudition(new PatchBuilder(NodeCatalog.BuiltIn).Patch).ShouldBeNull();
    }
}
