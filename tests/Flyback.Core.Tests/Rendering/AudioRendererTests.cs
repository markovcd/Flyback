using System.Buffers.Binary;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Rendering;

/// <summary>
/// The audio path evaluates the same register machine as the video path, but
/// at a sample cursor rather than over a frame. These check the parts that
/// video never exercises: pitch, stereo normalling, decimation and DC.
/// </summary>
public class AudioRendererTests
{
    /// <summary>A tone patch: Time into a sine, sine into the audio sink.</summary>
    private static CompiledPatch Tone(string oscillator, float hz, float gain = 1f)
    {
        var builder = new PatchBuilder();
        var time = builder.Add("time", 0, 0, (0, 1f));
        var osc = builder.Add(oscillator, 0, 0, (1, hz));
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, gain));

        builder.Wire(time, 0, osc, 0).Wire(osc, 0, sink, NodeCatalog.OutputLeftPort);

        return Compile(builder.Patch);
    }

    private static CompiledPatch Compile(Patch patch) =>
        patch.CompileForAudio().Program;

    private static float[] Render(CompiledPatch program, int frames, int oversample = 4)
    {
        var buffer = new float[frames * 2];
        new AudioRenderer(oversample: oversample).Render(program, buffer, AudioScan.TimeDriven);
        return buffer;
    }

    [Fact]
    public void A_220_hz_sine_really_comes_out_at_220_hz()
    {
        var buffer = Render(Tone("osc.sine", 220f), AudioRenderer.DefaultSampleRate);

        // Two zero crossings per cycle. Skip the first few samples: the DC
        // blocker settles from a cold start.
        var crossings = 0;
        for (var frame = 20; frame < AudioRenderer.DefaultSampleRate - 1; frame++)
        {
            var a = buffer[frame * 2];
            var b = buffer[(frame + 1) * 2];
            if ((a < 0f && b >= 0f) || (a >= 0f && b < 0f)) crossings++;
        }

        crossings.ShouldBeInRange(438, 441);
    }

    /// <summary>ADR-0009's normalled jack, now literal: unpatched right carries left.</summary>
    [Fact]
    public void An_unpatched_right_channel_carries_the_left_one()
    {
        var buffer = Render(Tone("osc.sine", 220f), 2_000);

        for (var frame = 0; frame < 2_000; frame++)
            buffer[frame * 2 + 1].ShouldBe(buffer[frame * 2]);

        // ...and it is genuinely signal, not two channels of silence.
        buffer.Any(v => Math.Abs(v) > 0.5f).ShouldBeTrue();
    }

    [Fact]
    public void Gain_scales_the_output()
    {
        var loud = Render(Tone("osc.sine", 220f, gain: 1f), 4_000);
        var quiet = Render(Tone("osc.sine", 220f, gain: 0.25f), 4_000);

        Peak(quiet).ShouldBeLessThan(Peak(loud) * 0.5f);
    }

    /// <summary>
    /// The bug this design is most likely to have: filter and cursor state must
    /// survive a buffer boundary, or every callback seam clicks.
    /// </summary>
    [Fact]
    public void Rendering_is_continuous_across_buffer_boundaries()
    {
        var program = Tone("osc.sine", 440f);

        var whole = new float[2_048 * 2];
        new AudioRenderer().Render(program, whole, AudioScan.TimeDriven);

        var split = new float[2_048 * 2];
        var renderer = new AudioRenderer();
        renderer.Render(program, split.AsSpan(0, 1_024 * 2), AudioScan.TimeDriven);
        renderer.Render(program, split.AsSpan(1_024 * 2), AudioScan.TimeDriven);

        split.ShouldBe(whole);
    }

    /// <summary>
    /// ADR-0032. A tone must be the same tone however long the synth has been
    /// running, and this is the one defect in here that only appears with age:
    /// a <c>float</c> t cannot hold two consecutive sample times apart once the
    /// clock passes about a minute, and an oscillator measuring how far its
    /// input moved is then handed a staircase instead of a ramp. What comes out
    /// is a high ringing whose pitch falls as the session goes on — inaudible
    /// for the first minute and unmissable after twenty.
    /// </summary>
    /// <remarks>
    /// Measured as ADR-0030's ratio, the largest sample-to-sample step against
    /// the median one: a tear is a step far larger than the wave's own travel.
    /// A clean 220 Hz sine reads 1.40 wherever it is sampled from, to two
    /// decimal places. Narrowing t back to a float reads 1.74 at five minutes,
    /// 5.3 at a thousand seconds and 66 at an hour — so the five-minute case is
    /// here to catch the defect while it is still only a measurement, and the
    /// hour is what it had become by the time anyone heard it.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    [InlineData(3_600)]
    public void A_tone_is_as_clean_an_hour_in_as_it_is_at_the_start(double startSeconds)
    {
        var program = Tone("osc.sine", 220f);
        var renderer = new AudioRenderer();
        var memory = renderer.DelayMemoryFor(program);

        renderer.SeekTo(startSeconds);

        // Let the DC blocker settle and the decimation window fill first.
        renderer.Render(program, new float[2_048 * 2], AudioScan.TimeDriven, memory);

        var buffer = new float[4_096 * 2];
        renderer.Render(program, buffer, AudioScan.TimeDriven, memory);

        var steps = new float[buffer.Length / 2 - 1];
        for (var i = 0; i < steps.Length; i++)
            steps[i] = Math.Abs(buffer[(i + 1) * 2] - buffer[i * 2]);

        var sorted = (float[])steps.Clone();
        Array.Sort(sorted);
        var median = sorted[sorted.Length / 2];

        median.ShouldBeGreaterThan(0f);
        (steps.Max() / median).ShouldBeLessThan(1.6f);
    }

    /// <summary>
    /// A tone above the output Nyquist should be filtered away rather than
    /// folded down. Point-sampling 30 kHz at 48 kHz aliases to 18 kHz; running
    /// the machine at 192 kHz and filtering before decimation removes it.
    /// </summary>
    [Fact]
    public void Oversampling_suppresses_a_tone_above_nyquist()
    {
        var program = Tone("osc.sine", 30_000f);

        var naive = Rms(Render(program, 8_000, oversample: 1));
        var oversampled = Rms(Render(program, 8_000, oversample: 4));

        naive.ShouldBeGreaterThan(0.3f);
        oversampled.ShouldBeLessThan(naive * 0.25f);
    }

    [Fact]
    public void A_constant_patch_decays_to_silence_through_the_dc_blocker()
    {
        var builder = new PatchBuilder();
        var knob = builder.Add("value", 0, 0, (0, 1f));
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));
        builder.Wire(knob, 0, sink, NodeCatalog.OutputLeftPort);

        var buffer = Render(Compile(builder.Patch), AudioRenderer.DefaultSampleRate);

        // Starts as a step, then settles: pure DC carries no sound.
        Peak(buffer.AsSpan(0, 200)).ShouldBeGreaterThan(0.5f);
        Peak(buffer.AsSpan(buffer.Length - 2_000)).ShouldBeLessThan(0.02f);
    }

    /// <summary>
    /// A program with delay lines is really two things — the ops and the memory
    /// they index into — and they have to be swapped together. Handed out rather
    /// than stored, so a caller swapping under a live callback can put both into
    /// one write; kept apart, a callback still on the previous program would
    /// index into the new program's lines and fault if the count had changed.
    /// </summary>
    [Fact]
    public void Delay_memory_belongs_to_the_program_that_asked_for_it()
    {
        var renderer = new AudioRenderer();

        var none = new CompiledPatch([new Op(OpCode.Const, 0)], 1, 0, 1);
        var one = Lines(0.25f);
        var two = Lines(0.25f, 0.5f);

        renderer.DelayMemoryFor(none).ShouldBeNull();

        var first = renderer.DelayMemoryFor(one).ShouldNotBeNull();
        first.Count.ShouldBe(1);

        // The same shape keeps the same buffers, which is what lets a delay carry
        // on ringing while a knob is turned.
        renderer.DelayMemoryFor(one, first).ShouldBeSameAs(first);

        // A different shape must not be handed the old lines.
        renderer.DelayMemoryFor(two, first).ShouldNotBeSameAs(first);
        renderer.DelayMemoryFor(two, first).ShouldNotBeNull().Count.ShouldBe(2);
    }

    /// <summary>Memory brought by the caller is used, and produces what the renderer's own would.</summary>
    [Fact]
    public void Rendering_with_borrowed_memory_matches_rendering_without()
    {
        var program = Echo();

        var mine = new AudioRenderer();
        var theirs = new AudioRenderer();

        var withOwn = new float[4_000];
        var withBorrowed = new float[4_000];

        mine.Render(program, withOwn, AudioScan.TimeDriven);
        theirs.Render(program, withBorrowed, AudioScan.TimeDriven, theirs.DelayMemoryFor(program));

        withBorrowed.ShouldBe(withOwn);
    }

    /// <summary>A program that only declares delay lines, for shape comparisons.</summary>
    private static CompiledPatch Lines(params float[] lengths)
    {
        var emitter = new Emitter();
        var zero = emitter.Constant(0f);

        foreach (var length in lengths)
            emitter.DelayLine(OpCode.Delay, zero, zero, zero, length);

        return new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, 0, 1);
    }

    /// <summary>A tone through a delay, so there is something for the memory to hold.</summary>
    private static CompiledPatch Echo()
    {
        var emitter = new Emitter();

        var tone = emitter.Unary(OpCode.Sin, emitter.Mul(emitter.Load(OpCode.LoadT), 1_382f));
        var echoed = emitter.DelayLine(
            OpCode.Delay, tone, emitter.Constant(0.6f), emitter.Constant(0.01f), 0.05f);

        return new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, echoed.Base, 1);
    }

    /// <summary>
    /// And says nothing about it. Every patch has an Output now, so a patch
    /// built for the eye alone is one whose 'left' is empty — which is a
    /// deliberate thing, not a complaint waiting to happen.
    /// </summary>
    [Fact]
    public void A_patch_with_nothing_wired_to_the_speakers_is_silent_rather_than_a_failure()
    {
        var result = Presets.Plasma(NodeCatalog.BuiltIn).CompileForAudio();

        result.Issues.ShouldBeEmpty();

        // Silence means exactly zero, not nearly zero — a tolerance would pass
        // on a patch that was quietly bleeding signal into the audio path.
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        Render(result.Program, 1_000).ShouldAllBe(v => v == 0f);
    }

    [Fact]
    public void Scanning_reads_the_image_rather_than_the_clock()
    {
        // Coordinates -> Output makes a horizontal ramp; scanning it must produce
        // signal, while the same patch driven by time alone sits at x = 0.
        var builder = new PatchBuilder();
        var coords = builder.Add("coord", 0, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));
        builder.Wire(coords, 0, sink, NodeCatalog.OutputLeftPort);

        var program = Compile(builder.Patch);

        var still = new float[4_000 * 2];
        new AudioRenderer().Render(program, still, AudioScan.TimeDriven);

        var scanned = new float[4_000 * 2];
        new AudioRenderer().Render(program, scanned, new AudioScan(true, 220f, 16f / 9f));

        Peak(still).ShouldBeLessThan(0.01f);
        Peak(scanned).ShouldBeGreaterThan(0.3f);
    }

    [Fact]
    public void A_written_wav_round_trips()
    {
        var samples = Render(Tone("osc.sine", 440f), 1_000);

        using var stream = new MemoryStream();
        WavWriter.Write(stream, samples, AudioRenderer.DefaultSampleRate, 2);
        var bytes = stream.ToArray();

        Ascii(bytes, 0).ShouldBe("RIFF");
        Ascii(bytes, 8).ShouldBe("WAVE");
        Ascii(bytes, 36).ShouldBe("data");
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22, 2)).ShouldBe((short)2);
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4)).ShouldBe(AudioRenderer.DefaultSampleRate);
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4)).ShouldBe(samples.Length * 2);
        bytes.Length.ShouldBe(44 + samples.Length * 2);

        // Decode and confirm it is the same waveform within 16-bit resolution.
        for (var i = 0; i < samples.Length; i++)
        {
            var decoded = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44 + i * 2, 2)) / 32767f;
            decoded.ShouldBe(samples[i], 0.0001f);
        }
    }

    private static string Ascii(byte[] bytes, int offset) =>
        System.Text.Encoding.ASCII.GetString(bytes, offset, 4);

    private static float Peak(ReadOnlySpan<float> buffer)
    {
        var peak = 0f;
        foreach (var v in buffer) peak = Math.Max(peak, Math.Abs(v));
        return peak;
    }

    private static float Rms(ReadOnlySpan<float> buffer)
    {
        var sum = 0.0;
        foreach (var v in buffer) sum += (double)v * v;
        return (float)Math.Sqrt(sum / buffer.Length);
    }
}
