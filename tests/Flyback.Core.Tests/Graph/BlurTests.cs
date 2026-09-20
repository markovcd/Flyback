using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Blur: the last frame averaged over nine readings and mixed back over the new
/// one, so softness accumulates pass by pass.
/// </summary>
/// <remarks>
/// Rendered rather than evaluated, because what it reads is a frame and only a
/// renderer keeps one. What is being checked is the loop rather than the kernel:
/// that a wire at nought is exactly a wire, that an edge keeps spreading while
/// the picture holds still, and that a flat field does not lose its corners —
/// which it would if the readings that fall outside the frame came back black.
/// </remarks>
public class BlurTests
{
    private const string Blur = "feedback.blur";

    private const int In = 0;
    private const int Radius = 3;
    private const int Amount = 4;

    private const int Color = 0;
    private const int Soft = 1;

    private const int Width = 64;
    private const int Height = 36;

    [Fact]
    public void It_is_a_video_module_in_the_feedback_section()
    {
        var def = NodeCatalog.BuiltIn.Require(Blur);

        def.Name.ShouldBe("Blur");
        def.Category.ShouldBe(ModuleCategories.Feedback);
        def.Sinks.ShouldBe(ModuleSinks.Video);
        def.Outputs.Select(p => p.Name).ShouldBe(["color", "soft"]);
        def.Inputs[Radius].Default.ShouldBe(0.02f);
        def.Inputs[Amount].Default.ShouldBe(0.7f);
    }

    /// <summary>
    /// Nothing of the last frame survives a mix of nought, so the module is the
    /// wire the knob says it is — the same pixels, not merely similar ones.
    /// </summary>
    [Fact]
    public void At_nought_it_is_the_wire_it_says_it_is()
    {
        var straight = new PatchBuilder(NodeCatalog.BuiltIn);
        var sink = straight.Add(NodeCatalog.OutputTypeId);
        straight.Wire(Edge(straight), 0, sink, NodeCatalog.OutputColorPort);

        Rendered(Blurred(0.02f, 0f), 8).ShouldBe(Rendered(straight.Patch, 8));
    }

    [Fact]
    public void An_edge_comes_out_softer_than_it_went_in()
    {
        Between(Rendered(Blurred(0.02f, 0.7f), 8))
            .ShouldBeGreaterThan(Between(Rendered(Blurred(0.02f, 0f), 8)));
    }

    /// <summary>
    /// One pass at this radius is under a pixel wide at this size, so a band
    /// several pixels across is the passes piling up — and how much of each
    /// survives into the next is what decides how wide the pile settles.
    /// </summary>
    [Fact]
    public void More_of_each_pass_surviving_settles_wider()
    {
        Between(Rendered(Blurred(0.02f, 0.9f), 40))
            .ShouldBeGreaterThan(Between(Rendered(Blurred(0.02f, 0.5f), 40)));
    }

    /// <summary>
    /// The kernel sums to one and a reading off the edge is clamped to the edge,
    /// so a white frame stays white to its corners instead of being eaten inward.
    /// </summary>
    [Fact]
    public void A_flat_field_keeps_its_corners()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var white = b.Add("color.rgb");
        var blur = b.Add(Blur, (Radius, 0.05f), (Amount, 0.9f));
        var sink = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(white, 0, blur, In).Wire(blur, Color, sink, NodeCatalog.OutputColorPort);

        Darkest(Rendered(b.Patch, 60)).ShouldBeGreaterThan(250);
    }

    /// <summary>The second output is the average alone, with nothing new mixed into it.</summary>
    [Fact]
    public void The_average_alone_is_a_frame_behind()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var blur = b.Add(Blur);
        var sink = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(Edge(b), 0, blur, In).Wire(blur, Soft, sink, NodeCatalog.OutputColorPort);

        // Nothing but the average reaches the screen, so there is never a frame
        // for it to be the average of.
        Darkest(Rendered(b.Patch, 8)).ShouldBe(0);
    }

    // --- harness -----------------------------------------------------------------

    /// <summary>A hard vertical edge: white right of the middle, black left of it.</summary>
    private static NodeInstance Edge(PatchBuilder b)
    {
        var where = b.Add(NodeCatalog.CoordTypeId);
        var edge = b.Add("math.step", (0, 0f));

        b.Wire(where, NodeCatalog.CoordXPort, edge, 1);

        return edge;
    }

    private static Patch Blurred(float radius, float amount)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var blur = b.Add(Blur, (Radius, radius), (Amount, amount));
        var sink = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(Edge(b), 0, blur, In).Wire(blur, Color, sink, NodeCatalog.OutputColorPort);

        return b.Patch;
    }

    private static byte[] Rendered(Patch patch, int frames)
    {
        var result = patch.CompileForVideo(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var renderer = new SynthRenderer();
        var stride = Width * 4;
        var buffer = new byte[stride * Height];

        for (var frame = 0; frame < frames; frame++)
            renderer.Render(result.Program, frame / 30d, Width, Height, buffer, stride);

        return buffer;
    }

    /// <summary>How many pixels are neither black nor white, which is the width of the edge.</summary>
    private static int Between(byte[] frame)
    {
        var grey = 0;
        for (var at = 0; at < frame.Length; at += 4)
            if (frame[at] > 8 && frame[at] < 247) grey++;
        return grey;
    }

    private static int Darkest(byte[] frame)
    {
        var darkest = 255;
        for (var at = 0; at < frame.Length; at += 4)
            darkest = Math.Min(darkest, frame[at]);
        return darkest;
    }
}
