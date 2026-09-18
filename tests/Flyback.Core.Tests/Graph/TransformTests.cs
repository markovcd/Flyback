using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Transform: a Scale, a Rotate and a Translate in one, in either order of the
/// first two.
/// </summary>
/// <remarks>
/// Evaluated rather than rendered, so what is compared is the coordinates to the
/// last bit and not the bytes a picture of them rounds to.
/// </remarks>
public class TransformTests
{
    private const string Transform = "space.transform";

    private const int Zoom = 2;
    private const int Angle = 3;
    private const int Dx = 4;
    private const int Dy = 5;

    [Fact]
    public void It_is_a_geometry_module_with_the_knobs_trails_has()
    {
        var def = NodeCatalog.BuiltIn.Require(Transform);

        def.Name.ShouldBe("Transform");
        def.Category.ShouldBe(ModuleCategories.Geometry);
        def.Inputs.Select(p => p.Name).ShouldBe(["x", "y", "zoom", "angle", "dx", "dy"]);
        def.Outputs.Select(p => p.Name).ShouldBe(["x", "y"]);
        def.Extra<SettingsExtra>().ShouldNotBeNull().Fields.ShouldHaveSingleItem().Key
            .ShouldBe(NodeCatalog.TransformOrderKey);
    }

    [Fact]
    public void At_rest_it_is_a_wire()
    {
        Placed(new PatchBuilder(NodeCatalog.BuiltIn), null).ShouldBe(Untouched());
    }

    [Theory]
    [InlineData(1.7f, 0.4f, 0f, 0f)]
    [InlineData(0.62f, -2.1f, 0.3f, -0.15f)]
    [InlineData(1f, 0.0035f, 0f, 0f)]
    public void Zoom_first_it_is_a_scale_into_a_rotate_into_a_translate(float zoom, float angle, float dx, float dy)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var scaled = b.Add("space.scale", (2, zoom));
        var turned = b.Add("space.rotate", (2, angle));
        var moved = b.Add("space.translate", (2, dx), (3, dy));

        b.Wire(scaled, 0, turned, 0).Wire(scaled, 1, turned, 1)
         .Wire(turned, 0, moved, 0).Wire(turned, 1, moved, 1);

        Placed(new PatchBuilder(NodeCatalog.BuiltIn), null, (Zoom, zoom), (Angle, angle), (Dx, dx), (Dy, dy))
            .ShouldBe(Seen(b, moved));
    }

    [Theory]
    [InlineData(1.7f, 0.4f, 0f, 0f)]
    [InlineData(0.62f, -2.1f, 0.3f, -0.15f)]
    public void Turn_first_it_is_a_rotate_into_a_scale_into_a_translate(float zoom, float angle, float dx, float dy)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var turned = b.Add("space.rotate", (2, angle));
        var scaled = b.Add("space.scale", (2, zoom));
        var moved = b.Add("space.translate", (2, dx), (3, dy));

        b.Wire(turned, 0, scaled, 0).Wire(turned, 1, scaled, 1)
         .Wire(scaled, 0, moved, 0).Wire(scaled, 1, moved, 1);

        Placed(
                new PatchBuilder(NodeCatalog.BuiltIn),
                NodeCatalog.TurnThenZoom,
                (Zoom, zoom), (Angle, angle), (Dx, dx), (Dy, dy))
            .ShouldBe(Seen(b, moved));
    }

    // --- harness -----------------------------------------------------------------

    private static double[] Placed(PatchBuilder b, string? order, params (int Port, float Value)[] knobs)
    {
        var placed = b.Add(Transform, knobs);

        if (order is not null)
            placed.SetState(NodeCatalog.TransformStateKey, new JsonObject { [NodeCatalog.TransformOrderKey] = order });

        return Seen(b, placed);
    }

    private static double[] Untouched()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        return Seen(b, b.Add("coord"));
    }

    /// <summary>A module's x and y as red and green, read at a handful of pixels.</summary>
    private static double[] Seen(PatchBuilder b, NodeInstance plane)
    {
        var color = b.Add("color.rgb", (2, 0f));
        var sink = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(plane, 0, color, 0).Wire(plane, 1, color, 1).Wire(color, 0, sink, NodeCatalog.OutputColorPort);

        var result = b.Patch.CompileForVideo(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var program = result.Program;
        var registers = program.AllocateRegisters();
        var seen = new List<double>();

        foreach (var (x, y) in new[] { (0.31, -0.72), (-1.4, 0.05), (0.0, 0.0), (1.77, 0.99) })
        {
            program.Evaluate(x, y, 1.25, registers, default);
            seen.Add(registers[program.OutputBase]);
            seen.Add(registers[program.OutputBase + 1]);
        }

        return [.. seen];
    }
}
