using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The canvas is a finite rectangle and a module cannot be put outside it.
/// </summary>
/// <remarks>
/// Held on the coordinate rather than on any gesture, so it is true however the
/// module got there: a guard on the drag would be a guard on the mouse, and a
/// hand-edited file, a paste and an assistant all place modules without one.
/// </remarks>
public class NodeBoundsTests
{
    private static NodeDef Sine => NodeCatalog.BuiltIn.Require("osc.sine");

    private static NodeInstance At(double x, double y) => NodeInstance.Create(Sine, x, y);

    [Fact]
    public void A_module_inside_the_canvas_is_left_where_it_is()
    {
        var node = At(1620, -940);

        node.X.ShouldBe(1620);
        node.Y.ShouldBe(-940);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, -1)]
    public void A_module_past_an_edge_lands_on_it(double dx, double dy)
    {
        var node = At(dx * (NodeInstance.Across + 5000), dy * (NodeInstance.Down + 5000));

        node.X.ShouldBe(dx * NodeInstance.Across);
        node.Y.ShouldBe(dy * NodeInstance.Down);
    }

    [Fact]
    public void Moving_one_past_an_edge_lands_on_it_too()
    {
        var node = At(0, 0);

        node.X = double.MaxValue;
        node.Y = double.MinValue;

        node.X.ShouldBe(NodeInstance.Across);
        node.Y.ShouldBe(-NodeInstance.Down);
    }

    /// <summary>
    /// Infinity is genuinely far away in a direction, so it lands on the edge
    /// like any other overshoot.
    /// </summary>
    [Fact]
    public void Infinity_lands_on_the_edge()
    {
        var node = At(double.PositiveInfinity, double.NegativeInfinity);

        node.X.ShouldBe(NodeInstance.Across);
        node.Y.ShouldBe(-NodeInstance.Down);
    }

    /// <summary>
    /// Not a number at all is not far away in some direction — it is a
    /// coordinate that was never worked out, and clamping leaves it exactly as
    /// it was. Left where it is, it poisons every comparison that looks for the
    /// corners of a patch, so framing shows an empty grid for ever after.
    /// </summary>
    [Fact]
    public void A_coordinate_that_is_not_a_number_goes_to_the_origin()
    {
        var node = At(double.NaN, double.NaN);

        node.X.ShouldBe(0);
        node.Y.ShouldBe(0);
    }

    [Fact]
    public void A_file_cannot_carry_a_module_outside_the_canvas()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var sine = b.Add("osc.sine", 40, 20);
        var screen = b.Add(NodeCatalog.OutputTypeId, 400, 20);
        b.Wire(sine, 0, screen, NodeCatalog.OutputColorPort);

        // Straight into the JSON, the way a hand-edited file would arrive: the
        // object cannot be made to hold this, so the text has to be.
        var json = PatchIO.ToJson(b.Patch, NodeCatalog.BuiltIn)
            .Replace("\"X\": 40", "\"X\": 1e12");

        var read = PatchIO.Read(json, NodeCatalog.BuiltIn);

        read.IsComplete.ShouldBeTrue(read.Summary);
        read.Patch.Find(sine.Id).ShouldNotBeNull().X.ShouldBe(NodeInstance.Across);
    }

    [Fact]
    public void Pasting_far_enough_across_stops_at_the_edge()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var sine = b.Add("osc.sine", 40, 20);

        var fragment = PatchClipboard.Copy(b.Patch, [sine.Id]);
        var pasted = PatchClipboard.Paste(new Patch(), fragment, NodeInstance.Across * 3);

        pasted.ShouldHaveSingleItem().X.ShouldBe(NodeInstance.Across);
    }
}
