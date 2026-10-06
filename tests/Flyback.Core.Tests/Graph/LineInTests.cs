using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Line In: what a microphone hears, read by the sound's program through two live
/// inputs the renderer fills once a frame.
/// </summary>
public class LineInTests
{
    private const int Frames = 4800;

    private static CompiledPatch Heard(float gain = 1f)
    {
        var builder = new PatchBuilder();
        var line = builder.Add(NodeCatalog.LineInTypeId, 0, 0, (0, gain));
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));

        builder
            .Wire(line, 0, sink, NodeCatalog.OutputLeftPort)
            .Wire(line, 1, sink, NodeCatalog.OutputRightPort);

        return builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
    }

    private static float[] Tone(float hz, float amplitude = 0.5f) =>
        [.. Enumerable.Range(0, Frames).Select(i => amplitude * MathF.Sin(MathF.Tau * hz * i / GlobalConstants.SampleRate))];

    private static float[] Render(CompiledPatch program, ILineInSource? input)
    {
        var buffer = new float[Frames * 2];
        new AudioRenderer(oversample: 1) { Input = input }.Render(program, buffer);
        return buffer;
    }

    /// <summary>The loudness of one channel, past the first few hundred frames where the DC blocker settles.</summary>
    private static double Rms(float[] interleaved, int channel)
    {
        var sum = 0d;
        var count = 0;

        for (var frame = 500; frame < Frames; frame++, count++)
        {
            var sample = interleaved[frame * 2 + channel];
            sum += sample * sample;
        }

        return Math.Sqrt(sum / count);
    }

    [Fact]
    public void A_line_in_plays_what_it_hears()
    {
        var heard = Render(Heard(), new RecordedLineIn(Tone(1000f)));

        Rms(heard, 0).ShouldBe(0.5 / Math.Sqrt(2), 0.02);
        Rms(heard, 1).ShouldBe(0.5 / Math.Sqrt(2), 0.02);
    }

    [Fact]
    public void Its_gain_scales_what_it_hears()
    {
        var quiet = Render(Heard(0.5f), new RecordedLineIn(Tone(1000f)));

        Rms(quiet, 0).ShouldBe(0.25 / Math.Sqrt(2), 0.02);
    }

    [Fact]
    public void The_two_channels_are_heard_apart()
    {
        var heard = Render(Heard(), new RecordedLineIn(Tone(1000f), new float[Frames]));

        Rms(heard, 0).ShouldBeGreaterThan(0.3);
        Rms(heard, 1).ShouldBe(0d, 1e-6);
    }

    [Fact]
    public void With_nothing_to_hear_it_is_silent()
    {
        var heard = Render(Heard(), null);

        heard.ShouldAllBe(sample => sample == 0f);
    }

    [Fact]
    public void A_program_without_a_line_in_reads_no_input()
    {
        var program = new PatchBuilder().Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        program.LiveInputs.ShouldNotContain(LineInSignal.Left);
        Heard().LiveInputs.ShouldBe([LineInSignal.Left, LineInSignal.Right], ignoreOrder: true);
    }

    [Fact]
    public void A_live_block_is_played_into_the_same_way()
    {
        var program = Heard();
        var live = new LiveValues(program.LiveInputs);
        var buffer = new float[Frames * 2];

        new AudioRenderer(oversample: 1) { Input = new RecordedLineIn(Tone(1000f)) }.Render(program, buffer, live: live);

        Rms(buffer, 0).ShouldBeGreaterThan(0.3);
        live.Find(LineInSignal.Left).ShouldNotBeNull();
    }

    // --- the feed ---------------------------------------------------------------

    [Fact]
    public void A_feed_hands_back_what_was_written_in_order()
    {
        var feed = new LineInFeed();

        feed.Write([0.1f, -0.1f, 0.2f, -0.2f], channels: 2);

        feed.Next(out var l1, out var r1);
        feed.Next(out var l2, out var r2);

        (l1, r1, l2, r2).ShouldBe((0.1f, -0.1f, 0.2f, -0.2f));
    }

    [Fact]
    public void A_microphone_is_heard_on_both_sides()
    {
        var feed = new LineInFeed();

        feed.Write([0.3f], channels: 1);
        feed.Next(out var left, out var right);

        (left, right).ShouldBe((0.3f, 0.3f));
    }

    [Fact]
    public void An_empty_feed_is_silence_and_stays_empty()
    {
        var feed = new LineInFeed();

        feed.Next(out var left, out var right);

        (left, right).ShouldBe((0f, 0f));
        feed.Pending.ShouldBe(0);
    }

    [Fact]
    public void A_sound_that_fell_behind_skips_forward_instead_of_trailing_for_ever()
    {
        var feed = new LineInFeed();
        var heard = new float[LineInFeed.MaxLag + 2000];

        for (var i = 0; i < heard.Length; i++) heard[i] = i / (float)heard.Length;

        feed.Write(heard, channels: 1);
        feed.Next(out var first, out _);

        feed.Pending.ShouldBeLessThanOrEqualTo(LineInFeed.Target);
        first.ShouldBe((heard.Length - LineInFeed.Target) / (float)heard.Length, 1e-6f);
    }

    [Fact]
    public void A_feed_with_a_cushion_is_silent_until_it_holds_one_and_again_after_it_runs_dry()
    {
        var feed = new LineInFeed { Cushion = 4 };

        feed.Write([0.1f, 0.2f, 0.3f], channels: 1);
        feed.Next(out var early, out _);

        feed.Write([0.4f], channels: 1);
        feed.Next(out var first, out _);
        feed.Next(out var second, out _);
        feed.Next(out var third, out _);
        feed.Next(out var fourth, out _);
        feed.Next(out var dry, out _);

        feed.Write([0.5f], channels: 1);
        feed.Next(out var waiting, out _);

        (early, first, second, third, fourth, dry, waiting).ShouldBe((0f, 0.1f, 0.2f, 0.3f, 0.4f, 0f, 0f));
    }

    [Fact]
    public void A_number_that_is_not_one_is_not_heard()
    {
        var feed = new LineInFeed();

        feed.Write([float.NaN, 5f], channels: 2);
        feed.Next(out var left, out var right);

        (left, right).ShouldBe((0f, 1f));
    }

    [Fact]
    public void A_recorded_clip_ends_in_silence()
    {
        var clip = new RecordedLineIn([0.5f]);

        clip.Next(out var first, out _);
        clip.Next(out var after, out _);

        (first, after).ShouldBe((0.5f, 0f));
    }
}
