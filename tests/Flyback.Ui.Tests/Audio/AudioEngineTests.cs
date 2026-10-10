using System.Text.Json.Nodes;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Ui.Capture;
using Flyback.Ui.Audio;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Audio;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Flyback.Plugins.Testing;

namespace Flyback.Ui.Tests.Audio;

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
            using var engine = new AudioEngine(new AudioSetup(device));
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

    /// <summary>A change of oversampling plays on from where the clock was, at the new rate.</summary>
    [Fact]
    public void A_change_of_oversampling_plays_on_at_the_new_rate()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));
        engine.Update(Tone(220f));
        engine.Start();

        engine.Oversample.ShouldBe(AudioRenderer.DefaultOversample);
        device.Pump();
        var at = engine.Time;

        engine.Oversample = 4;
        engine.Oversample.ShouldBe(4);
        engine.Time.ShouldBe(at, 1e-9);

        var heard = device.Pump();
        heard.ShouldContain(sample => sample != 0f, "the sound stopped at the change");
        engine.Time.ShouldBe(at + BufferFrames / (double)GlobalConstants.SampleRate, 1e-9);
    }

    /// <summary>A sound that has played says how many times real time it renders at, and one stopped says nothing.</summary>
    [Fact]
    public void Its_speed_is_measured_while_it_plays()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));
        engine.Update(Tone(220f));

        engine.Speed.ShouldBe(0);

        engine.Start();
        for (var i = 0; i < 20; i++) device.Pump();

        engine.Speed.ShouldBeGreaterThan(1, "a lone sine renders far faster than it plays");

        engine.Stop();

        engine.Speed.ShouldBe(0);
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
        using var engine = new AudioEngine(new AudioSetup(device));
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
        using var engine = new AudioEngine(new AudioSetup(device));

        engine.Start();

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        device.Pump().ShouldAllBe(v => v == 0f);
    }

    [Fact]
    public void A_patch_with_no_speaker_is_silent_rather_than_a_failure()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));
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
        using var engine = new AudioEngine(new AudioSetup(device));
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

        // After the buffer the jump lands in: the decimating filter rings on a jump in level for a few dozen samples.
        device.Pump(4_096);
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
        using var engine = new AudioEngine(new AudioSetup(device));

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
        // ReSharper disable once CompareOfFloatsByEqualityOperator
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
        using var engine = new AudioEngine(new AudioSetup(device));

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
    /// A Clock In runs ahead by the speakers' latency, which a device can only
    /// say for certain once it plays, and which can move while it does.
    /// </summary>
    [Fact]
    public void AClockInIsToldHowFarBehindTheSpeakersRun()
    {
        var device = new LoopbackDevice { Latency = TimeSpan.FromMilliseconds(20) };
        using var engine = new AudioEngine(new AudioSetup(device));

        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var clock = builder.Add(NodeCatalog.ClockTypeId, 0, 0);
        var speaker = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));
        builder.Wire(clock, 0, speaker, NodeCatalog.OutputLeftPort);

        engine.Update(builder.Patch);
        engine.Live.Find(MidiSignal.LeadKey).ShouldBe(0.02f);

        engine.Start();
        device.Latency = TimeSpan.FromMilliseconds(12);
        device.Pump();
        engine.Live.Find(MidiSignal.LeadKey).ShouldBe(0.012f);
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
        using var first = new AudioEngine(new AudioSetup(fresh));
        first.Update(patch);
        first.Start();
        var opening = fresh.Pump(4_096);

        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));
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
        var engine = new AudioEngine(new AudioSetup(device));

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
        using var unbroken = new AudioEngine(new AudioSetup(only));
        unbroken.Update(patch);
        unbroken.Start();

        for (var i = 0; i < 10; i++) only.Pump();

        var expected = only.Pump();

        var speakers = new LoopbackDevice();
        using var headphones = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(speakers));
        engine.Update(patch);
        engine.Start();

        for (var i = 0; i < 10; i++) speakers.Pump();

        engine.Stop();
        engine.Use(new AudioSetup(headphones)).ShouldBeTrue();
        engine.Start();

        headphones.Pump().ShouldBe(expected);
    }

    [Fact]
    public void A_device_is_not_changed_under_a_running_callback()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));
        using var other = new LoopbackDevice();

        engine.Start();

        Should.Throw<InvalidOperationException>(() => engine.Use(new AudioSetup(other)));
    }

    /// <summary>
    /// The renderer is built for one rate, so a device at another is refused and the
    /// one already there is kept — not disposed from under the caller.
    /// </summary>
    [Fact]
    public void A_device_at_another_rate_is_refused()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));
        using var slower = new SilentAudioDevice(GlobalConstants.SampleRate / 2);

        engine.Use(new AudioSetup(slower)).ShouldBeFalse();

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
        using var engine = new AudioEngine(new AudioSetup(device));

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
    /// A preset's picture that draws what the speakers played, a Beam, has
    /// something to draw while the preset is auditioned.
    /// </summary>
    [Fact]
    public void An_audition_hands_its_beam_what_it_played()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));

        var patch = Presets.All.Single(p => p.Name == "Lissajous").Build(NodeCatalog.BuiltIn);
        var picture = patch.CompileForVideo().Program;
        var beam = picture.Taps.Single(tap => tap.Chart is ChartKind.Beam).Trace.Samples;

        engine.Start();

        var audition = engine.PrepareAudition(patch).ShouldNotBeNull();
        engine.StartAudition(audition);

        for (var i = 0; i < 40; i++) device.Pump();

        beam.ShouldAllBe(texel => texel == 0f);

        audition.Listen(picture);

        beam.ShouldContain(texel => texel > 0f);
    }

    /// <summary>
    /// The patch that was playing goes quiet while a preset is auditioned, rather
    /// than the two being heard at once, and comes back when it ends.
    /// </summary>
    [Fact]
    public void A_patch_is_faded_out_under_an_audition_and_back_in_after()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));

        engine.Update(Tone(220f));
        engine.Start();
        var playing = Peak(device.Pump());

        // The same preset heard with nothing under it, to tell the patch apart from it.
        using var aloneDevice = new LoopbackDevice();
        using var alone = new AudioEngine(new AudioSetup(aloneDevice));
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
    public async Task An_audition_is_compiled_like_the_patch()
    {
        using var device = new LoopbackDevice();
        using var services = new ServiceCollection().AddTransport().AddSingleton(new AudioSetup(device)).BuildServiceProvider();
        var engine = services.GetRequiredService<AudioEngine>();
        var compiler = services.GetRequiredService<IlCompiler>();

        var audition = engine.PrepareAudition(Tone(220f)).ShouldNotBeNull();
        await compiler.Settled();

        audition.Program.Il.ShouldNotBeNull();
    }

    [Fact]
    public void A_patch_that_makes_no_sound_is_not_auditioned()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));

        engine.PrepareAudition(new PatchBuilder(NodeCatalog.BuiltIn).Patch).ShouldBeNull();
    }

    private sealed class Tap : IAudioSink
    {
        public float[] Heard = [];

        public void WriteAudio(ReadOnlySpan<float> interleaved) => Heard = interleaved.ToArray();
    }

    [Fact]
    public void Full_gain_is_the_sound_exactly_as_it_was()
    {
        float[] Play(float? gain)
        {
            using var device = new LoopbackDevice();
            using var engine = new AudioEngine(new AudioSetup(device));

            engine.Update(Tone(220f));
            if (gain is { } level) engine.Gain = level;
            engine.Start();

            return device.Pump();
        }

        Play(1f).ShouldBe(Play(null));
    }

    [Fact]
    public void Zero_gain_is_silence_and_the_recording_still_hears_the_patch()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));
        var tap = new Tap();

        engine.Update(Tone(220f));
        engine.Gain = 0f;
        engine.Capture = tap;
        engine.Start();

        Peak(device.Pump()).ShouldBe(0f);
        Peak(tap.Heard).ShouldBeGreaterThan(0.1f);
    }

    [Fact]
    public void A_level_between_turns_the_speakers_down_by_that_much()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));
        using var half = new LoopbackDevice();
        using var reference = new AudioEngine(new AudioSetup(half));

        engine.Update(Tone(220f));
        engine.Gain = 0.5f;
        engine.Start();

        reference.Update(Tone(220f));
        reference.Start();

        Peak(device.Pump()).ShouldBe(Peak(half.Pump()) * 0.5f, tolerance: 1e-5f);
    }

    [Fact]
    public void Seeking_a_stopped_engine_starts_the_sound_there()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));

        engine.Update(Tone(220f));
        engine.SeekTo(12);
        engine.Start();

        device.Pump(GlobalConstants.SampleRate / 10);

        engine.Time.ShouldBeGreaterThanOrEqualTo(12.0);
        engine.Time.ShouldBeLessThan(12.5);
    }

    [Fact]
    public void Seeking_a_running_engine_is_carried_out_by_the_next_buffer()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));

        engine.Update(Tone(220f));
        engine.Start();
        device.Pump(GlobalConstants.SampleRate);

        engine.SeekTo(30);
        device.Pump(GlobalConstants.SampleRate / 10);

        engine.Time.ShouldBeGreaterThanOrEqualTo(30.0);
        engine.Time.ShouldBeLessThan(30.5);
    }

    [Fact]
    public void A_rewind_after_a_seek_takes_it_back()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));

        engine.Update(Tone(220f));
        engine.Start();

        engine.SeekTo(30);
        engine.Rewind();
        device.Pump();

        engine.Time.ShouldBeLessThan(0.1);
    }

    /// <summary>A held note is read as held by the first buffer of the program an edit swaps in.</summary>
    /// <remarks>
    /// The new program's block starts at nought, so a callback that ran before
    /// whoever follows the keyboard had written into it heard the note let go and
    /// came back to it a moment later: a pop on every edit made while a key was down.
    /// </remarks>
    [Fact]
    public void A_held_note_is_not_let_go_by_the_edit_that_swaps_the_program_in()
    {
        using var device = new LoopbackDevice();
        using var engine = new AudioEngine(new AudioSetup(device));

        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var key = builder.Add(NodeCatalog.MidiTypeId, 0, 0);

        key.SetState(MidiExtra.StateKey, new JsonObject { [MidiExtra.IndexField] = 1f });

        var time = builder.Add("time", 0, 0);
        var osc = builder.Add("osc.sine", 0, 0, (1, 220f));
        var gated = builder.Add("math.mul", 0, 0);
        var speaker = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));

        builder
            .Wire(time, 0, osc, 0)
            .Wire(osc, 0, gated, 0)
            .Wire(key, 1, gated, 1)
            .Wire(gated, 0, speaker, NodeCatalog.OutputLeftPort);

        var gate = MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Gate);

        engine.Update(builder.Patch);
        engine.Live.Set(gate, 1f);
        engine.Start();

        var before = device.Pump();

        engine.Update(builder.Patch, seed: block => block.Set(gate, 1f));

        var after = device.Pump();

        // The end of the buffer, since the speakers' own filters are still ringing at its start.
        Peak(after[^(BufferFrames / 2 * 2)..]).ShouldBeGreaterThan(Peak(before) * 0.5f, "the note went quiet across the edit");
    }
}
