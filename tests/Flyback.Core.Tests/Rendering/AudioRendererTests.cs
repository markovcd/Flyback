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
    private const int SampleRate = 48_000;

    /// <summary>A tone patch: Time into a sine, sine into the audio sink.</summary>
    private static CompiledPatch Tone(string oscillator, float hz, float gain = 1f)
    {
        var builder = new PatchBuilder();
        var time = builder.Add("time", 0, 0, (0, 1f));
        var osc = builder.Add(oscillator, 0, 0, (1, hz));
        var sink = builder.Add(NodeCatalog.AudioOutputTypeId, 0, 0, (2, gain));

        builder.Wire(time, 0, osc, 0).Wire(osc, 0, sink, 0);

        return Compile(builder.Patch);
    }

    private static CompiledPatch Compile(Patch patch) =>
        PatchCompiler.Compile(patch, NodeCatalog.AudioOutputTypeId, NodeCatalog.AudioChannels).Program;

    private static float[] Render(CompiledPatch program, int frames, int oversample = 4)
    {
        var buffer = new float[frames * 2];
        new AudioRenderer(SampleRate, oversample).Render(program, buffer, AudioScan.TimeDriven);
        return buffer;
    }

    [Fact]
    public void A_220_hz_sine_really_comes_out_at_220_hz()
    {
        var buffer = Render(Tone("osc.sine", 220f), SampleRate);

        // Two zero crossings per cycle. Skip the first few samples: the DC
        // blocker settles from a cold start.
        var crossings = 0;
        for (var frame = 20; frame < SampleRate - 1; frame++)
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
        new AudioRenderer(SampleRate).Render(program, whole, AudioScan.TimeDriven);

        var split = new float[2_048 * 2];
        var renderer = new AudioRenderer(SampleRate);
        renderer.Render(program, split.AsSpan(0, 1_024 * 2), AudioScan.TimeDriven);
        renderer.Render(program, split.AsSpan(1_024 * 2), AudioScan.TimeDriven);

        split.ShouldBe(whole);
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
        var sink = builder.Add(NodeCatalog.AudioOutputTypeId, 0, 0, (2, 1f));
        builder.Wire(knob, 0, sink, 0);

        var buffer = Render(Compile(builder.Patch), SampleRate);

        // Starts as a step, then settles: pure DC carries no sound.
        Peak(buffer.AsSpan(0, 200)).ShouldBeGreaterThan(0.5f);
        Peak(buffer.AsSpan(buffer.Length - 2_000)).ShouldBeLessThan(0.02f);
    }

    [Fact]
    public void A_patch_with_no_audio_sink_is_silent_rather_than_a_failure()
    {
        var result = PatchCompiler.Compile(
            Presets.Plasma(), NodeCatalog.AudioOutputTypeId, NodeCatalog.AudioChannels);

        result.Issues.ShouldBeEmpty();

        Render(result.Program, 1_000).ShouldAllBe(v => v == 0f);
    }

    [Fact]
    public void Scanning_reads_the_image_rather_than_the_clock()
    {
        // Coordinates -> Output makes a horizontal ramp; scanning it must produce
        // signal, while the same patch driven by time alone sits at x = 0.
        var builder = new PatchBuilder();
        var coords = builder.Add("coord", 0, 0);
        var sink = builder.Add(NodeCatalog.AudioOutputTypeId, 0, 0, (2, 1f));
        builder.Wire(coords, 0, sink, 0);

        var program = Compile(builder.Patch);

        var still = new float[4_000 * 2];
        new AudioRenderer(SampleRate).Render(program, still, AudioScan.TimeDriven);

        var scanned = new float[4_000 * 2];
        new AudioRenderer(SampleRate).Render(program, scanned, new AudioScan(true, 220f, 16f / 9f));

        Peak(still).ShouldBeLessThan(0.01f);
        Peak(scanned).ShouldBeGreaterThan(0.3f);
    }

    [Fact]
    public void A_written_wav_round_trips()
    {
        var samples = Render(Tone("osc.sine", 440f), 1_000);

        using var stream = new MemoryStream();
        WavWriter.Write(stream, samples, SampleRate, 2);
        var bytes = stream.ToArray();

        Ascii(bytes, 0).ShouldBe("RIFF");
        Ascii(bytes, 8).ShouldBe("WAVE");
        Ascii(bytes, 36).ShouldBe("data");
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22, 2)).ShouldBe((short)2);
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4)).ShouldBe(SampleRate);
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
