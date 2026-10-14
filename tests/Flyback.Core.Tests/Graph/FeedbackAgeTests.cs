using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Feedback's 'age': the seconds since the frame it reads was drawn, so a loop
/// built by hand fades by the second as Trails does.
/// </summary>
public class FeedbackAgeTests
{
    private const int Width = 64;
    private const int Height = 36;
    private const int Frames = 12;

    private const int Color = 0;
    private const int Age = 1;

    [Fact]
    public void Feedback_says_how_old_its_frame_is()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.FeedbackTypeId);

        def.Outputs.Select(p => p.Name).ShouldBe(["color", "age"]);
        def.Outputs[Age].Kind.ShouldBe(PortKind.Scalar);
    }

    /// <summary>A Gain raised to 'age' times 60 is the dimming Trails does inside.</summary>
    [Theory]
    [InlineData(24d, 0.9f)]
    [InlineData(30d, 0.95f)]
    [InlineData(144d, 0.8f)]
    public void A_loop_dimmed_by_its_age_draws_what_trails_draws(double fps, float persist)
    {
        var byHand = new PatchBuilder(NodeCatalog.BuiltIn);
        var before = byHand.Add(NodeCatalog.FeedbackTypeId);
        var sixtieths = byHand.Add("math.mul", (1, NodeCatalog.TrailsFrameRate));
        var dimming = byHand.Add("math.pow", (0, persist));
        var dimmed = byHand.Add("color.gain");
        var brighter = byHand.Add("math.max");

        byHand.Wire(before, Age, sixtieths, 0)
            .Wire(sixtieths, 0, dimming, 1)
            .Wire(before, Color, dimmed, 0)
            .Wire(dimming, 0, dimmed, 1)
            .Wire(dimmed, 0, brighter, 0)
            .Wire(Spark(byHand), 0, brighter, 1)
            .Wire(brighter, 0, byHand.Add(NodeCatalog.OutputTypeId), NodeCatalog.OutputColorPort);

        var wrapped = new PatchBuilder(NodeCatalog.BuiltIn);
        var trails = wrapped.Add("feedback.trails", (7, persist));

        wrapped.Wire(Spark(wrapped), 0, trails, 0)
            .Wire(trails, 0, wrapped.Add(NodeCatalog.OutputTypeId), NodeCatalog.OutputColorPort);

        Rendered(byHand.Patch, fps).ShouldBe(Rendered(wrapped.Patch, fps));
    }

    /// <summary>A small bright disc that moves with the clock.</summary>
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

    private static byte[] Rendered(Patch patch, double fps)
    {
        var compiled = patch.CompileForVideo(NodeCatalog.BuiltIn);
        compiled.HasErrors.ShouldBeFalse(string.Join("; ", compiled.Issues.Select(i => i.Message)));

        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var buffer = new byte[stride * Height];

        for (var frame = 0; frame < Frames; frame++)
            renderer.Render(compiled.Program, frame / fps, Width, Height, buffer, stride);

        return buffer;
    }
}
