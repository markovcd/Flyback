using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Delay and Reverb modules, loaded off disk and driven sample by sample
/// with real delay state behind them.
/// </summary>
/// <remarks>
/// The signal goes in through the Coordinates module's x, because
/// <see cref="CompiledPatch.Evaluate"/> takes x per evaluation — which makes it
/// the one way to feed a module an arbitrary waveform without building an
/// oscillator to produce it. Everything runs at 1 kHz so a delay in seconds is a
/// whole number of samples.
/// </remarks>
public class SpaceTests
{
    private const string DelayType = "flyback.space.delay";
    private const string ReverbType = "flyback.space.reverb";

    private const int Rate = 1_000;

    /// <summary>A line is read before it is written, so everything lands one sample late.</summary>
    private const int Lag = 1;

    /// <summary>
    /// How long a module that asks whether it has a memory takes to find out. The
    /// flag is a cell read before it is written, like everything else here, so the
    /// first evaluation of a program reads the answer the video path gets — which
    /// for a Reverb means one sample of dry before the room appears. The same
    /// sample the Filter spends, for the same reason, and inaudible at any rate
    /// the speakers actually run at.
    /// </summary>
    private const int Settled = 1;

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    [Fact]
    public void The_plugin_offers_both_modules_from_one_assembly()
    {
        Catalog.Get(DelayType).ShouldNotBeNull().Name.ShouldBe("Delay");
        Catalog.Get(ReverbType).ShouldNotBeNull().Name.ShouldBe("Reverb");

        Catalog.ProviderOf(DelayType).ShouldBe(Catalog.ProviderOf(ReverbType));
    }

    [Fact]
    public void A_delay_at_no_mix_is_exactly_a_wire()
    {
        var signal = Noise(64);
        var output = Through(DelayType, signal, (1, 0.01f), (2, 0.7f), (3, 0f));

        for (var i = 0; i < signal.Length; i++) output[i].ShouldBe(signal[i], 1e-5f);
    }

    [Fact]
    public void What_goes_into_a_delay_comes_back_out_later()
    {
        var output = Through(DelayType, Impulse(128), (1, 0.02f), (2, 0f), (3, 1f));

        output[20 + Lag].ShouldBe(1f, 1e-3f);
    }

    [Fact]
    public void Turning_feedback_up_gives_more_repeats()
    {
        var quiet = Through(DelayType, Impulse(256), (1, 0.02f), (2, 0.2f), (3, 1f));
        var loud = Through(DelayType, Impulse(256), (1, 0.02f), (2, 0.8f), (3, 1f));

        Energy(quiet, 100, 256).ShouldBeLessThan(Energy(loud, 100, 256));
    }

    /// <summary>Sweeping the delay time must glide, which is what interpolation is for.</summary>
    [Fact]
    public void A_delay_time_between_two_samples_lands_between_them()
    {
        var early = Through(DelayType, Impulse(128), (1, 0.020f), (2, 0f), (3, 1f));
        var late = Through(DelayType, Impulse(128), (1, 0.021f), (2, 0f), (3, 1f));
        var between = Through(DelayType, Impulse(128), (1, 0.0205f), (2, 0f), (3, 1f));

        // The whole impulse sits on one sample in each of the outer cases, and is
        // split across both in the middle one.
        early[20 + Lag].ShouldBe(1f, 1e-3f);
        late[21 + Lag].ShouldBe(1f, 1e-3f);

        between[20 + Lag].ShouldBe(0.5f, 0.05f);
        between[21 + Lag].ShouldBe(0.5f, 0.05f);
    }

    [Fact]
    public void A_reverb_at_no_mix_is_exactly_a_wire()
    {
        var signal = Noise(64);
        var output = Through(ReverbType, signal, (1, 0.8f), (2, 0.9f), (3, 0f));

        for (var i = 0; i < signal.Length; i++) output[i].ShouldBe(signal[i], 1e-5f);
    }

    /// <summary>
    /// The thing that makes it a reverb rather than an echo: sound keeps arriving
    /// after the input has stopped, and it fades rather than repeating.
    /// </summary>
    [Fact]
    public void A_reverb_rings_on_after_the_input_stops()
    {
        var signal = new float[4_000];
        for (var i = 0; i < 20; i++) signal[i] = 1f;

        var output = Through(ReverbType, signal, (1, 0.5f), (2, 0.8f), (3, 1f));

        var early = Energy(output, 200, 1_000);
        var late = Energy(output, 1_000, 2_000);
        var latest = Energy(output, 3_000, 4_000);

        early.ShouldBeGreaterThan(0f);
        late.ShouldBeGreaterThan(0f);

        // Still going long after the input, and quieter every time you look.
        latest.ShouldBeLessThan(late);
        late.ShouldBeLessThan(early);
    }

    [Fact]
    public void A_longer_decay_rings_for_longer()
    {
        var signal = new float[4_000];
        for (var i = 0; i < 20; i++) signal[i] = 1f;

        var quick = Through(ReverbType, signal, (1, 0.5f), (2, 0.2f), (3, 1f));
        var slow = Through(ReverbType, signal, (1, 0.5f), (2, 0.95f), (3, 1f));

        Energy(quick, 2_000, 4_000).ShouldBeLessThan(Energy(slow, 2_000, 4_000));
    }

    /// <summary>
    /// The tail comes back at about the level that went in, whatever the decay —
    /// which is what makes 'mix' a crossfade between two comparable things rather
    /// than a fade from the signal down into a whisper.
    /// </summary>
    /// <remarks>
    /// Scaled off the comb's broadband gain and not its gain at DC, which is the
    /// tallest point of its response and six to twenty decibels above what it
    /// does to an actual tail. Getting that wrong is inaudible from inside the
    /// module — everything still decays, everything is still stable — and is only
    /// visible against the dry signal, so this is the test that has to hold it.
    /// </remarks>
    [Fact]
    public void The_tail_comes_back_at_about_the_level_that_went_in()
    {
        var signal = White(24_000);
        var went = Rms(signal, 12_000, 24_000);

        foreach (var decay in new[] { 0f, 0.4f, 0.7f, 1f })
        {
            var wet = Through(ReverbType, signal, (1, 0.5f), (2, decay), (3, 1f));
            var came = Rms(wet, 12_000, 24_000);

            // Within six decibels either way. Loose because a comb bank's gain
            // depends on what is played through it — and this harness runs at
            // 1 kHz, where interpolating a fractional delay costs far more of the
            // band than it does at the rate the speakers see — but tight enough
            // that the twenty the old scaling cost would not fit inside it.
            came.ShouldBeGreaterThan(went * 0.5f);
            came.ShouldBeLessThan(went * 2f);

            // A tail is a great many echoes arriving at once, so it peaks higher
            // above its own average than the signal that made it does. Some of
            // that is the point and all of it is why a patch puts a limiter after
            // a reverb; what matters here is that it stays a crest factor rather
            // than becoming a runaway.
            Peak(wet).ShouldBeLessThan(Peak(signal) * 4f);
        }
    }

    /// <summary>
    /// Nothing a comb bank is fed can run away with it, and the input a bank
    /// amplifies most is the one a patch here is likeliest to produce: DC. An
    /// envelope, an offset Remap and a slow LFO are all mostly DC, and all of them
    /// are ordinary things to wire into a reverb.
    /// </summary>
    [Fact]
    public void A_reverb_does_not_get_louder_than_what_went_in()
    {
        var signal = new float[8_000];
        Array.Fill(signal, 1f);

        foreach (var decay in new[] { 0f, 0.5f, 0.95f, 1f })
        {
            var output = Through(ReverbType, signal, (1, 0.5f), (2, decay), (3, 1f));

            foreach (var sample in output)
            {
                float.IsFinite(sample).ShouldBeTrue();
                MathF.Abs(sample).ShouldBeLessThan(2f);
            }

            // And it is the highpass at the door doing it rather than luck: what
            // survives a steady input is the edge at the start of it, not the
            // steadiness afterwards.
            Rms(output, 4_000, 8_000).ShouldBeLessThan(0.05f);
        }
    }

    /// <summary>
    /// What separates the room from the pipe: every trip round a comb loses a
    /// little more of the top, so the tail darkens as it dies instead of keeping
    /// one timbre all the way down. A comb with a plain gain in its loop would
    /// hold the same brightness from the first repeat to the last, which is the
    /// metallic ring a cheap reverb is recognised by.
    /// </summary>
    /// <remarks>
    /// The one test here that cannot run at 1 kHz. The corner is fixed in hertz
    /// and sits at four thousand of them, which at this harness's rate is past
    /// Nyquist and therefore no filtering at all — so this one is heard at a rate
    /// the speakers would recognise instead.
    /// </remarks>
    [Fact]
    public void The_tail_darkens_as_it_dies()
    {
        const int Heard = 48_000;

        var signal = new float[Heard];
        for (var i = 0; i < Heard / 100; i++) signal[i] = 1f;

        var tail = ThroughAt(Heard, ReverbType, signal, (1, 0.5f), (2, 0.9f), (3, 1f));

        var young = Brightness(tail, Heard / 10, Heard / 5);
        var old = Brightness(tail, Heard / 2, Heard);

        // A third of the brightness it started with, and the fraction is what
        // makes this a test rather than a formality: a bank with no filter in it
        // still darkens, because interpolating a fractional delay is itself a
        // gentle lowpass and the loop applies it again on every pass. That alone
        // reaches about a half. Only the damping reaches a fifth.
        old.ShouldBeLessThan(young * 0.35f);
    }

    /// <summary>
    /// One comb bank feeding two allpass chains of different lengths: the same
    /// tail arriving at the same time in both channels, smeared into two
    /// patterns that are not each other. That difference is the whole of the
    /// width, since nothing upstream of the allpasses differs at all.
    /// </summary>
    [Fact]
    public void The_two_outputs_are_one_tail_smeared_two_ways()
    {
        var signal = new float[2_000];
        for (var i = 0; i < 20; i++) signal[i] = 1f;

        var (left, right) = Stereo(signal, (1, 0.5f), (2, 0.8f), (3, 1f));

        var carried = Energy(left, 200, 2_000);

        // An allpass passes every frequency at unity, so both chains hand on the
        // energy the bank gave them and neither channel is the loud one.
        carried.ShouldBeGreaterThan(0f);
        Energy(right, 200, 2_000).ShouldBe(carried, carried * 0.25f);

        // And they are not the same signal, which is the point of having two.
        var apart = 0f;
        for (var i = 200; i < 2_000; i++) apart += MathF.Abs(left[i] - right[i]);

        apart.ShouldBeGreaterThan(0.1f);
    }

    /// <summary>
    /// A bigger room answers later. Both halves of 'size' push the same way — the
    /// gap before the first reflection opens up, and every delay behind it
    /// stretches — so the onset is the one place the whole control is visible at
    /// once.
    /// </summary>
    [Fact]
    public void A_bigger_room_takes_longer_to_answer()
    {
        var tiled = Through(ReverbType, Impulse(512), (1, 0f), (2, 0.5f), (3, 1f));
        var hall = Through(ReverbType, Impulse(512), (1, 1f), (2, 0.5f), (3, 1f));

        // Nothing at all until the first reflection arrives, in either room.
        Onset(tiled).ShouldBeGreaterThan(Settled);
        Onset(tiled).ShouldBeLessThan(Onset(hall));
    }

    /// <summary>
    /// A Delay is a wire on the video path. There is no state there — rows render
    /// in parallel — so the op hands its input straight on, and a patch written
    /// for the speakers still draws what it drew before.
    /// </summary>
    [Fact]
    public void With_no_state_a_delay_passes_the_picture_through()
    {
        foreach (var (x, seen) in Painted(DelayType))
            seen.ShouldBe(x, 1e-5f);
    }

    /// <summary>
    /// And so is a Reverb, which it did not used to be. With no state the bank is
    /// eight wires in parallel, so what it would hand the picture back is some
    /// multiple of itself — and which multiple depends on the decay, so the
    /// brightness of a video patch would follow a knob about how long a sound
    /// takes to die. The memory flag decides it instead: no memory, no room.
    /// </summary>
    [Fact]
    public void With_no_state_a_reverb_passes_the_picture_through()
    {
        foreach (var (x, seen) in Painted(ReverbType))
            seen.ShouldBe(x, 1e-5f);
    }

    /// <summary>
    /// The picture is the same picture whatever the decay is set to, which is the
    /// half of it that a fixed scaling could never have managed.
    /// </summary>
    [Fact]
    public void The_decay_knob_does_not_touch_the_picture()
    {
        foreach (var decay in new[] { 0f, 0.5f, 1f })
            foreach (var (x, seen) in Painted(ReverbType, (2, decay)))
                seen.ShouldBe(x, 1e-5f);
    }

    /// <summary>Compiles the module into the video sink and reads it with no state, as SynthRenderer does.</summary>
    private static (float X, float Seen)[] Painted(string typeId, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, typeId, [(3, 1f), .. knobs]);
        var screen = Add(patch, NodeCatalog.OutputTypeId);

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, 0, screen.Id, 0);

        var program = patch.CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        return
        [
            .. new[] { 0f, 0.25f, 0.5f, 1f }.Select(x =>
            {
                program.Evaluate(x, 0f, 0f, registers, default);
                return (x, (float)registers[program.OutputBase]);
            }),
        ];
    }

    [Fact]
    public void The_preset_builds_and_compiles_for_both_sinks()
    {
        var loaded = PluginHost.Load();
        var patch = loaded.Presets.Single(p => p.Name == "Echo chamber").Build(loaded.Modules);

        patch.Nodes.Select(n => n.TypeId).ShouldContain(DelayType);
        patch.Nodes.Select(n => n.TypeId).ShouldContain(ReverbType);

        patch.CompileForVideo(loaded.Modules).Issues.ShouldBeEmpty();

        var audio = patch.CompileForAudio(loaded.Modules);
        audio.Issues.ShouldBeEmpty();

        // One delay line for the echo, and the reverb's seventeen — a pre-delay,
        // eight combs, and four allpasses for each of its two outputs.
        audio.Program.DelayLengths.Count.ShouldBe(18);
    }

    // --- harness ----------------------------------------------------------------

    /// <summary>
    /// Feeds a signal through one module into the audio sink, one sample at a
    /// time, with the delay state the program asked for.
    /// </summary>
    private static float[] Through(string typeId, float[] signal, params (int Port, float Value)[] knobs) =>
        ThroughAt(Rate, typeId, signal, knobs);

    /// <summary>
    /// The same at a rate of its own, for the one module whose behaviour is
    /// written in hertz and so cannot be seen at 1 kHz at all.
    /// </summary>
    private static float[] ThroughAt(int rate, string typeId, float[] signal, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, typeId, knobs);
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 1f));

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);

        var program = patch.CompileForAudio(Catalog).Program;
        var delays = new DelayState(program.DelayLengths, rate, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();
        var output = new float[signal.Length];

        for (var i = 0; i < signal.Length; i++)
        {
            program.Evaluate(signal[i], 0f, i / (double)rate, registers, default, delays);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }

    /// <summary>
    /// The same, for a module with two outputs, wired to the two the speakers
    /// actually have. Separate from <see cref="Through"/> rather than folded into
    /// it because a patch with nothing on the right is a mono patch — the sink
    /// reads the left register twice — and every test above means to be one.
    /// </summary>
    private static (float[] Left, float[] Right) Stereo(float[] signal, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var coord = Add(patch, "coord");
        var effect = Add(patch, ReverbType, knobs);
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 1f));

        patch.Connect(coord.Id, 0, effect.Id, 0);
        patch.Connect(effect.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);
        patch.Connect(effect.Id, 1, sink.Id, NodeCatalog.OutputRightPort);

        var program = patch.CompileForAudio(Catalog).Program;
        var delays = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        var registers = program.AllocateRegisters();

        var left = new float[signal.Length];
        var right = new float[signal.Length];

        for (var i = 0; i < signal.Length; i++)
        {
            program.Evaluate(signal[i], 0f, i / (double)Rate, registers, default, delays);
            left[i] = (float)registers[program.OutputBase];
            right[i] = (float)registers[program.OutputBase + 1];
        }

        return (left, right);
    }

    private static NodeInstance Add(Patch patch, string typeId, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Catalog.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        patch.Nodes.Add(node);
        return node;
    }

    private static float[] Impulse(int length)
    {
        var signal = new float[length];
        signal[0] = 1f;
        return signal;
    }

    /// <summary>Deterministic, so a failure is reproducible.</summary>
    private static float[] Noise(int length) =>
        [.. Enumerable.Range(0, length).Select(i => MathF.Sin(i * 12.9898f) * 0.5f)];

    private static float Energy(float[] signal, int from, int to)
    {
        var sum = 0f;
        for (var i = from; i < to && i < signal.Length; i++) sum += signal[i] * signal[i];
        return sum;
    }

    /// <summary>
    /// How much of a stretch of signal is high frequency, as the energy of its
    /// first difference against its own. Differencing is a highpass, so the ratio
    /// rises with brightness and — being a ratio — says nothing about level,
    /// which is what lets it compare two tails that are fading at once.
    /// </summary>
    private static float Brightness(float[] signal, int from, int to)
    {
        var edges = 0f;

        for (var i = Math.Max(from, 1); i < to && i < signal.Length; i++)
        {
            var slope = signal[i] - signal[i - 1];
            edges += slope * slope;
        }

        return edges / Energy(signal, from, to);
    }

    /// <summary>
    /// Where the first reflection arrives, which is the room answering. Counted
    /// past <see cref="Settled"/>, whose one sample of dry is not the room.
    /// </summary>
    private static int Onset(float[] signal)
    {
        for (var i = Settled; i < signal.Length; i++)
            if (MathF.Abs(signal[i]) > 1e-4f) return i;

        return signal.Length;
    }

    private static float Peak(float[] signal)
    {
        var most = 0f;
        foreach (var sample in signal) most = MathF.Max(most, MathF.Abs(sample));
        return most;
    }

    private static float Rms(float[] signal, int from, int to) =>
        MathF.Sqrt(Energy(signal, from, to) / (Math.Min(to, signal.Length) - from));

    /// <summary>
    /// White, and deterministic so a failure is reproducible. <see cref="Noise"/>
    /// is neither — sampling a sine per index folds it down to one slow tone —
    /// and a comb bank is exactly the thing that would answer a single tone with
    /// whatever its response happens to be at that frequency rather than with its
    /// gain.
    /// </summary>
    private static float[] White(int length)
    {
        var state = 22_695_477u;
        var signal = new float[length];

        for (var i = 0; i < length; i++)
        {
            state = (state * 1_664_525u) + 1_013_904_223u;
            signal[i] = ((state >> 8) / (float)(1 << 23)) - 1f;
        }

        return signal;
    }
}
