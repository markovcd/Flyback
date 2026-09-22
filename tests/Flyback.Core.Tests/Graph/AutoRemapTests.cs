using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A Remap whose ranges are read off its wires: fractions of the range at each
/// end, and plain numbers where an end has none.
/// </summary>
public class AutoRemapTests
{
    private static readonly ModuleCatalog Catalog = NodeCatalog.BuiltIn;

    private sealed record Rig(PatchBuilder Builder, Guid Remap)
    {
        public Patch Patch => Builder.Patch;

        public RemapSpans Spans => AutoRemap.Of(Patch, Patch.Find(Remap)!, Catalog);

        /// <summary>What the remap puts out, read as the root of a program at t = 0.</summary>
        public float Out()
        {
            var result = Patch.CompileForProbe(Remap, Catalog);
            var registers = result.Program.AllocateRegisters();

            result.Program.Evaluate(0d, 0d, 0d, registers, default);

            return (float)registers[result.Program.OutputBase];
        }
    }

    /// <summary>A Sine resting at <paramref name="phase"/> of its cycle into an Auto remap with <paramref name="knobs"/>.</summary>
    private static Rig FromSine(float phase = 0f, params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(Catalog);
        var sine = b.Add(NodeCatalog.SineTypeId, 0, 0, (1, 0f), (2, phase));
        var remap = b.Add(NodeCatalog.AutoRemapTypeId, 200, 0, knobs);

        b.Wire(sine, 0, remap, AutoRemap.In);

        return new Rig(b, remap.Id);
    }

    private static void Into(Rig rig, string typeId, int port)
    {
        var target = rig.Builder.Add(typeId, 400, 0);
        rig.Builder.Wire(rig.Patch.Find(rig.Remap)!, 0, target, port);
    }

    [Fact]
    public void Its_input_takes_the_range_of_what_feeds_it_and_its_output_the_range_of_what_it_feeds()
    {
        var rig = FromSine();
        Into(rig, NodeCatalog.FilterTypeId, 1);

        rig.Spans.In.ShouldBe(new RemapSpan(-1f, 1f));
        rig.Spans.Out.ShouldBe(new RemapSpan(20f, 12_000f, 20f));
    }

    [Fact]
    public void An_oscillators_range_moves_with_its_amp_and_bias()
    {
        var b = new PatchBuilder(Catalog);
        var sine = b.Add(NodeCatalog.SineTypeId, 0, 0, (3, 0.5f), (4, 0.5f));
        var remap = b.Add(NodeCatalog.AutoRemapTypeId, 200, 0);
        b.Wire(sine, 0, remap, AutoRemap.In);

        AutoRemap.Of(b.Patch, remap, Catalog).In.ShouldBe(new RemapSpan(0f, 1f));
    }

    [Fact]
    public void An_unwired_side_is_fractions_of_nought_to_one()
    {
        var b = new PatchBuilder(Catalog);
        var remap = b.Add(NodeCatalog.AutoRemapTypeId, 0, 0);

        AutoRemap.Of(b.Patch, remap, Catalog).ShouldBe(RemapSpans.Unwired);
    }

    [Fact]
    public void A_color_is_nought_to_one_on_either_end()
    {
        var b = new PatchBuilder(Catalog);
        var tint = b.Add("color.hsv", 0, 0);
        var remap = b.Add(NodeCatalog.AutoRemapTypeId, 200, 0);
        var output = b.Add(NodeCatalog.OutputTypeId, 400, 0);
        b.Wire(tint, 0, remap, AutoRemap.In);
        b.Wire(remap, 0, output, NodeCatalog.OutputColorPort);

        AutoRemap.Of(b.Patch, remap, Catalog).ShouldBe(RemapSpans.Unwired);
    }

    /// <summary>
    /// Pure red into a frequency: its brightness, about a fifth, remapped onto the
    /// knob, rather than a fifth of the top of the knob.
    /// </summary>
    [Fact]
    public void A_color_bound_for_a_single_number_is_remapped_as_its_brightness()
    {
        var b = new PatchBuilder(Catalog);
        var red = b.Add("color.hsv", 0, 0);
        var remap = b.Add(NodeCatalog.AutoRemapTypeId, 200, 0);
        var sine = b.Add(NodeCatalog.SineTypeId, 400, 0);
        b.Wire(red, 0, remap, AutoRemap.In);
        b.Wire(remap, 0, sine, 1);

        var rig = new Rig(b, remap.Id);

        rig.Spans.Narrow.ShouldBeTrue();
        rig.Out().ShouldBe(rig.Spans.Out!.Value.At(0.2126f), 1e-3f);
    }

    [Fact]
    public void A_color_bound_for_a_color_stays_three_channels()
    {
        var b = new PatchBuilder(Catalog);
        var tint = b.Add("color.hsv", 0, 0);
        var remap = b.Add(NodeCatalog.AutoRemapTypeId, 200, 0);
        var output = b.Add(NodeCatalog.OutputTypeId, 400, 0);
        b.Wire(tint, 0, remap, AutoRemap.In);
        b.Wire(remap, 0, output, NodeCatalog.OutputColorPort);

        AutoRemap.Of(b.Patch, remap, Catalog).Narrow.ShouldBeFalse();
    }

    [Fact]
    public void A_source_with_no_range_leaves_the_input_pair_as_plain_numbers()
    {
        var b = new PatchBuilder(Catalog);
        var value = b.Add(NodeCatalog.ValueTypeId, 0, 0);
        var remap = b.Add(NodeCatalog.AutoRemapTypeId, 200, 0);
        b.Wire(value, 0, remap, AutoRemap.In);

        var spans = AutoRemap.Of(b.Patch, remap, Catalog);

        spans.In.ShouldBeNull();
        spans.InWhy.ShouldBe("Value's 'out' has no range");
    }

    [Fact]
    public void A_socket_with_no_range_leaves_the_output_pair_as_plain_numbers()
    {
        var rig = FromSine();
        Into(rig, "math.add", 0);

        rig.Spans.Out.ShouldBeNull();
        rig.Spans.OutWhy.ShouldBe("Add's 'a' has no range");
    }

    [Fact]
    public void Two_sockets_that_disagree_leave_the_output_pair_as_plain_numbers()
    {
        var rig = FromSine();
        Into(rig, NodeCatalog.FilterTypeId, 1);
        Into(rig, "color.hsv", 0);

        rig.Spans.Out.ShouldBeNull();
        rig.Spans.OutWhy.ShouldBe("Filter's 'cutoff' and HSV's 'hue' take different ranges");
    }

    [Fact]
    public void A_socket_with_no_range_beside_one_with_a_range_follows_the_one_with()
    {
        var rig = FromSine();
        Into(rig, NodeCatalog.FilterTypeId, 1);
        Into(rig, "math.add", 0);

        rig.Spans.Out.ShouldBe(new RemapSpan(20f, 12_000f, 20f));
        rig.Spans.OutWhy.ShouldBeNull();
    }

    /// <summary>
    /// The sweep follows the socket's taper all the way along: halfway through its
    /// input, a remap spanning a cutoff's whole range sits at the octave midpoint,
    /// not at the arithmetic one.
    /// </summary>
    [Fact]
    public void Halfway_through_its_input_it_sits_halfway_round_the_sockets_knob()
    {
        var rig = FromSine();
        Into(rig, NodeCatalog.FilterTypeId, 1);

        rig.Out().ShouldBe(MathF.Sqrt(20f * 12_000f), 0.5f);
    }

    [Fact]
    public void Its_knobs_pick_a_slice_of_each_range()
    {
        // The sine at its peak is the top of the upper-half input slice, so the
        // output sits at the top of its own slice.
        var rig = FromSine(0.25f, (AutoRemap.InLow, 0.5f), (AutoRemap.InHigh, 1f), (AutoRemap.OutLow, 0.2f), (AutoRemap.OutHigh, 0.5f));
        Into(rig, NodeCatalog.FilterTypeId, 1);

        rig.Out().ShouldBe(rig.Spans.Out!.Value.At(0.5f), 0.5f);
    }

    [Fact]
    public void With_both_ends_unranged_it_is_a_plain_remap()
    {
        var b = new PatchBuilder(Catalog);
        var value = b.Add(NodeCatalog.ValueTypeId, 0, 0, (0, 0f));
        var remap = b.Add(NodeCatalog.AutoRemapTypeId, 200, 0,
            (AutoRemap.InLow, -2f), (AutoRemap.InHigh, 2f), (AutoRemap.OutLow, 90f), (AutoRemap.OutHigh, 320f));
        var add = b.Add("math.add", 400, 0);
        b.Wire(value, 0, remap, AutoRemap.In);
        b.Wire(remap, 0, add, 0);

        new Rig(b, remap.Id).Out().ShouldBe(205f, 1e-3f);
    }

    [Fact]
    public void A_pair_left_as_plain_numbers_is_said_when_the_patch_is_compiled()
    {
        var b = new PatchBuilder(Catalog);
        var value = b.Add(NodeCatalog.ValueTypeId, 0, 0);
        var remap = b.Add(NodeCatalog.AutoRemapTypeId, 200, 0);
        b.Wire(value, 0, remap, AutoRemap.In);

        b.Patch.CompileForVideo(Catalog).Issues
            .ShouldContain(i => i.NodeId == remap.Id
                && i.Message == "Auto remap's 'in low' and 'in high' are plain numbers: Value's 'out' has no range.");
    }

    [Theory]
    [InlineData(NodeCatalog.SineTypeId, NodeCatalog.FilterTypeId, 1, true)]
    [InlineData(NodeCatalog.SineTypeId, NodeCatalog.FilterTypeId, 0, false)]
    [InlineData(NodeCatalog.ValueTypeId, NodeCatalog.FilterTypeId, 1, false)]
    [InlineData(NodeCatalog.SineTypeId, "math.add", 0, false)]
    [InlineData(NodeCatalog.SineTypeId, NodeCatalog.AutoRemapTypeId, 0, false)]
    public void A_wire_is_offered_a_remap_only_where_both_ends_have_ranges_that_differ(
        string from, string into, int port, bool offered)
    {
        var b = new PatchBuilder(Catalog);
        var source = b.Add(from, 0, 0);
        var target = b.Add(into, 200, 0);
        b.Wire(source, 0, target, port);

        AutoRemap.Offered(b.Patch, b.Patch.Connections.Single(), Catalog).ShouldBe(offered);
    }

    [Theory]
    [InlineData(NodeCatalog.SineTypeId, "color.hsv", 2, true)]
    [InlineData(NodeCatalog.SineTypeId, "color.hsv", 0, false)]
    [InlineData(NodeCatalog.PulseTypeId, "env.adsr", 0, false)]
    [InlineData(NodeCatalog.SineTypeId, NodeCatalog.FilterTypeId, 0, false)]
    [InlineData("env.adsr", NodeCatalog.SineTypeId, 1, false)]
    [InlineData(NodeCatalog.ValueTypeId, "color.hsv", 2, false)]
    public void A_wire_is_warned_about_only_where_its_source_swings_past_either_end_of_its_socket(
        string from, string into, int port, bool warned)
    {
        var b = new PatchBuilder(Catalog);
        var source = b.Add(from, 0, 0);
        var target = b.Add(into, 200, 0);
        b.Wire(source, 0, target, port);

        (AutoRemap.Overflow(b.Patch, b.Patch.Connections.Single(), Catalog) is not null).ShouldBe(warned);
    }

    [Fact]
    public void A_wire_swinging_past_its_socket_is_said_when_the_patch_is_compiled()
    {
        var b = new PatchBuilder(Catalog);
        var sine = b.Add(NodeCatalog.SineTypeId, 0, 0);
        var tint = b.Add("color.hsv", 200, 0);
        b.Wire(sine, 0, tint, 2);

        b.Patch.CompileForVideo(Catalog).Issues.ShouldContain(i => i.NodeId == tint.Id
            && i.Message == "Sine's 'out' swings -1 to 1, past the 0 to 1 HSV's 'value' takes. An Auto remap in the wire fits it.");
    }

    [Fact]
    public void Travel_undoes_at_across_a_tapered_range()
    {
        var span = new RemapSpan(20f, 12_000f, 20f);

        foreach (var travel in (float[])[0f, 0.2f, 0.5f, 0.9f, 1f])
            span.Travel(span.At(travel)).ShouldBe(travel, 1e-4f);
    }
}
