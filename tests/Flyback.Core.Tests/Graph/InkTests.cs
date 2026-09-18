using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Ink and Vignette: the two ends of a drawn picture — a shape given a color and
/// laid on what is there, and the corners darkened once everything is.
/// </summary>
/// <remarks>
/// Evaluated rather than rendered, so the comparison with the modules each stands
/// for is of the color to the last bit, before a frame rounds it to bytes.
/// </remarks>
public class InkTests
{
    private const string Ink = "color.ink";
    private const string Vignette = "color.vignette";

    [Fact]
    public void Ink_is_a_color_module_with_a_mode_that_is_not_a_socket()
    {
        var def = NodeCatalog.BuiltIn.Require(Ink);

        def.Name.ShouldBe("Ink");
        def.Category.ShouldBe(ModuleCategories.Color);
        def.Inputs.Select(p => p.Name).ShouldBe(["under", "mask", "r", "g", "b"]);
        def.Extra<SettingsExtra>().ShouldNotBeNull().Fields.ShouldHaveSingleItem().Key.ShouldBe(NodeCatalog.InkModeKey);
    }

    [Fact]
    public void Added_it_is_an_rgb_into_a_gain_into_an_add()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var color = b.Add("color.rgb", (0, 1f), (1, 0.35f), (2, 0.5f));
        var lit = b.Add("color.gain");
        var sum = b.Add("math.add");

        b.Wire(color, 0, lit, 0).Wire(Mask(b), 0, lit, 1)
         .Wire(Ground(b), 0, sum, 0).Wire(lit, 0, sum, 1);

        Inked(null).ShouldBe(Seen(b, sum));
    }

    [Fact]
    public void Over_it_is_a_blend_towards_an_rgb()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var color = b.Add("color.rgb", (0, 1f), (1, 0.35f), (2, 0.5f));
        var blend = b.Add("color.mix");

        b.Wire(Ground(b), 0, blend, 0).Wire(color, 0, blend, 1).Wire(Mask(b), 0, blend, 2);

        Inked(NodeCatalog.InkOver).ShouldBe(Seen(b, blend));
    }

    [Fact]
    public void With_nothing_under_it_the_ink_is_the_color_times_the_mask()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var color = b.Add("color.rgb", (0, 1f), (1, 0.35f), (2, 0.5f));
        var lit = b.Add("color.gain");
        b.Wire(color, 0, lit, 0).Wire(Mask(b), 0, lit, 1);

        var inked = new PatchBuilder(NodeCatalog.BuiltIn);
        var ink = inked.Add(Ink, (2, 1f), (3, 0.35f), (4, 0.5f));
        inked.Wire(Mask(inked), 0, ink, 1);

        Seen(inked, ink).ShouldBe(Seen(b, lit));
    }

    [Fact]
    public void Vignette_is_a_video_module_with_the_darkening_on_a_second_output()
    {
        var def = NodeCatalog.BuiltIn.Require(Vignette);

        def.Name.ShouldBe("Vignette");
        def.Sinks.ShouldBe(ModuleSinks.Video);
        def.Inputs.Select(p => p.Name).ShouldBe(["color", "x", "y", "from", "to", "dark"]);
        def.Outputs.Select(p => p.Name).ShouldBe(["color", "shade"]);
    }

    [Theory]
    [InlineData(0.5f, 2f, 0.35f)]
    [InlineData(0.3f, 1.6f, 0.35f)]
    [InlineData(0.4f, 1.7f, 0.3f)]
    public void Vignette_is_the_radius_through_a_remap_and_a_clamp_into_a_gain(float from, float to, float dark)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var coord = b.Add("coord");
        var fall = b.Add("math.remap", (1, from), (2, to), (3, 1f), (4, dark));
        var held = b.Add("math.clamp", (1, 0f), (2, 1f));
        var shaded = b.Add("color.gain");

        b.Wire(coord, 2, fall, 0).Wire(fall, 0, held, 0)
         .Wire(Ground(b), 0, shaded, 0).Wire(held, 0, shaded, 1);

        var wrapped = new PatchBuilder(NodeCatalog.BuiltIn);
        var vignette = wrapped.Add(Vignette, (3, from), (4, to), (5, dark));
        wrapped.Wire(Ground(wrapped), 0, vignette, 0);

        Seen(wrapped, vignette).ShouldBe(Seen(b, shaded));
    }

    [Fact]
    public void The_shade_is_one_in_the_middle_and_dark_at_the_far_end()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var vignette = b.Add(Vignette);
        var grey = b.Add("color.rgb");
        var sink = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(vignette, 1, grey, 0).Wire(grey, 0, sink, NodeCatalog.OutputColorPort);

        var program = b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        var registers = program.AllocateRegisters();

        program.Evaluate(0d, 0d, 0d, registers, default);
        registers[program.OutputBase].ShouldBe(1d);

        program.Evaluate(2d, 0d, 0d, registers, default);
        registers[program.OutputBase].ShouldBe(0.35d, 1e-6);
    }

    // --- harness -----------------------------------------------------------------

    private static double[] Inked(string? mode)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var ink = b.Add(Ink, (2, 1f), (3, 0.35f), (4, 0.5f));

        if (mode is not null)
            ink.SetState(NodeCatalog.InkStateKey, new JsonObject { [NodeCatalog.InkModeKey] = mode });

        b.Wire(Ground(b), 0, ink, 0).Wire(Mask(b), 0, ink, 1);

        return Seen(b, ink);
    }

    /// <summary>A picture to draw on: a hue that changes across the frame.</summary>
    private static NodeInstance Ground(PatchBuilder b)
    {
        var coord = b.Add("coord");
        var hue = b.Add("color.hsv", (1, 0.7f), (2, 0.6f));
        b.Wire(coord, 0, hue, 0);
        return hue;
    }

    /// <summary>A soft disc, and brighter than one at its middle, as a struck shape is.</summary>
    private static NodeInstance Mask(PatchBuilder b)
    {
        var coord = b.Add("coord");
        var disc = b.Add("math.smoothstep", (0, 0.9f), (1, 0.2f));
        var struck = b.Add("math.mul", (1, 1.7f));
        b.Wire(coord, 2, disc, 2).Wire(disc, 0, struck, 0);
        return struck;
    }

    private static double[] Seen(PatchBuilder b, NodeInstance color)
    {
        var sink = b.Add(NodeCatalog.OutputTypeId);
        b.Wire(color, 0, sink, NodeCatalog.OutputColorPort);

        var result = b.Patch.CompileForVideo(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var registers = program.AllocateRegisters();
        var seen = new List<double>();

        foreach (var (x, y) in new[] { (0.31, -0.72), (-1.4, 0.05), (0.0, 0.0), (1.77, 0.99), (0.6, 0.2) })
        {
            program.Evaluate(x, y, 0.5, registers, default);

            for (var channel = 0; channel < 3; channel++) seen.Add(registers[program.OutputBase + channel]);
        }

        return [.. seen];
    }
}
