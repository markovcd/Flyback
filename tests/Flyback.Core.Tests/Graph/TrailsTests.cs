using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Trails: the last frame read back through a zoom, a turn and a shift, dimmed,
/// and laid under the new one.
/// </summary>
/// <remarks>
/// Rendered rather than evaluated, because what it reads is a frame and only a
/// renderer keeps one. The first test is the one that matters: it is a Scale, a
/// Rotate, a Translate, a Feedback, a Gain and a Maximum, and a patch that swaps
/// those six for this has to draw the same picture.
/// </remarks>
public class TrailsTests
{
    private const string Trails = "feedback.trails";

    private const int In = 0;
    private const int Zoom = 3;
    private const int Angle = 4;
    private const int Dx = 5;
    private const int Dy = 6;
    private const int Persist = 7;

    private const int Color = 0;
    private const int Tail = 1;

    private const int Width = 64;
    private const int Height = 36;
    private const int Frames = 12;

    [Fact]
    public void It_is_a_video_module_beside_the_feedback_it_wraps()
    {
        var def = NodeCatalog.BuiltIn.Require(Trails);

        def.Name.ShouldBe("Trails");
        def.Category.ShouldBe(ModuleCategories.Feedback);
        def.Sinks.ShouldBe(ModuleSinks.Video);
        def.Outputs.Select(p => p.Name).ShouldBe(["color", "tail"]);
        def.Inputs[Zoom].Default.ShouldBe(1f);
        def.Inputs[Angle].Default.ShouldBe(0f);
    }

    [Theory]
    [InlineData(1.03f, 0.02f, 0f, 0f, 0.88f)]
    [InlineData(0.982f, -0.008f, 0f, 0f, 0.8f)]
    [InlineData(1f, 0f, 0.007f, 0f, 0.6f)]
    [InlineData(1.01f, 0.3f, -0.02f, 0.015f, 0.95f)]
    public void It_draws_what_the_six_modules_it_stands_for_draw(
        float zoom, float angle, float dx, float dy, float persist)
    {
        var byHand = new PatchBuilder(NodeCatalog.BuiltIn);
        var scaled = byHand.Add("space.scale", (2, zoom));
        var turned = byHand.Add("space.rotate", (2, angle));
        var moved = byHand.Add("space.translate", (2, dx), (3, dy));
        var before = byHand.Add("feedback");
        var dimmed = byHand.Add("color.gain", (1, persist));
        var brighter = byHand.Add("math.max");
        var sink = byHand.Add(NodeCatalog.OutputTypeId);

        byHand.Wire(scaled, 0, turned, 0).Wire(scaled, 1, turned, 1)
            .Wire(turned, 0, moved, 0).Wire(turned, 1, moved, 1)
            .Wire(moved, 0, before, 0).Wire(moved, 1, before, 1)
            .Wire(before, 0, dimmed, 0)
            .Wire(dimmed, 0, brighter, 0)
            .Wire(Spark(byHand), 0, brighter, 1)
            .Wire(brighter, 0, sink, NodeCatalog.OutputColorPort);

        var wrapped = new PatchBuilder(NodeCatalog.BuiltIn);
        var trails = wrapped.Add(Trails, (Zoom, zoom), (Angle, angle), (Dx, dx), (Dy, dy), (Persist, persist));
        var sink2 = wrapped.Add(NodeCatalog.OutputTypeId);

        wrapped.Wire(Spark(wrapped), 0, trails, In)
            .Wire(trails, Color, sink2, NodeCatalog.OutputColorPort);

        Rendered(wrapped.Patch).ShouldBe(Rendered(byHand.Patch));
    }

    [Fact]
    public void A_moving_spark_leaves_a_tail_and_no_persist_leaves_none()
    {
        Lit(Rendered(Sparked(0.95f))).ShouldBeGreaterThan(Lit(Rendered(Sparked(0f))));
    }

    /// <summary>The second output is the dimmed last frame without the new one over it.</summary>
    [Fact]
    public void The_tail_alone_is_a_frame_behind()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var trails = b.Add(Trails, (Persist, 1f));
        var sink = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(Spark(b), 0, trails, In)
         .Wire(trails, Tail, sink, NodeCatalog.OutputColorPort);

        // Nothing but the tail reaches the screen, so there is never a frame for it
        // to be the tail of.
        Lit(Rendered(b.Patch)).ShouldBe(0);
    }

    // --- harness -----------------------------------------------------------------

    /// <summary>A small bright disc that moves with the clock, in color so all three channels are tested.</summary>
    private static NodeInstance Spark(PatchBuilder b)
    {
        var clock = b.Add("time");
        var across = b.Add("osc.sine", (1, 0.9f), (3, 0.6f));
        var moved = b.Add("space.translate");
        var far = b.Add("math.hypot");
        var disc = b.Add("math.smoothstep", (0, 0.3f), (1, 0.2f));
        var hue = b.Add("color.hsv", (1, 0.8f));

        b.Wire(clock, 0, across, 0)
         .Wire(across, 0, moved, 2)
         .Wire(moved, 0, far, 0)
         .Wire(moved, 1, far, 1)
         .Wire(far, 0, disc, 2)
         .Wire(clock, 0, hue, 0)
         .Wire(disc, 0, hue, 2);

        return hue;
    }

    private static Patch Sparked(float persist)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var trails = b.Add(Trails, (Persist, persist));
        var sink = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(Spark(b), 0, trails, In)
         .Wire(trails, Color, sink, NodeCatalog.OutputColorPort);

        return b.Patch;
    }

    private static byte[] Rendered(Patch patch)
    {
        var result = patch.CompileForVideo(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var buffer = new byte[stride * Height];

        for (var frame = 0; frame < Frames; frame++)
            renderer.Render(result.Program, frame / 30d, Width, Height, buffer, stride);

        return buffer;
    }

    /// <summary>How many pixels have anything in them.</summary>
    private static int Lit(byte[] frame)
    {
        var lit = 0;
        for (var at = 0; at < frame.Length; at += 4)
            if (frame[at] > 8 || frame[at + 1] > 8 || frame[at + 2] > 8) lit++;
        return lit;
    }
}
