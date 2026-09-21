using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The stereo desk: four channels of left, right and level, a bus to chain on,
/// a trim, and the rails.
/// </summary>
/// <remarks>
/// It stands for a box every track otherwise builds by hand — a Mixer, a Multiply
/// and a Clamp per ear — so the property worth pinning is that it is that box:
/// the same sum, in the same order, to the last bit.
/// </remarks>
public class DeskTests
{
    private const string Desk = "math.desk";

    private const int Left = 0;
    private const int Right = 1;
    private const int BusLeftOut = 2;
    private const int BusRightOut = 3;

    private const int BusLeftIn = 12;
    private const int BusRightIn = 13;
    private const int Trim = 14;

    private static int LeftIn(int channel) => (channel - 1) * 3;

    private static int RightIn(int channel) => (channel - 1) * 3 + 1;

    private static int Level(int channel) => (channel - 1) * 3 + 2;

    [Fact]
    public void It_is_four_stereo_channels_a_bus_and_a_trim()
    {
        var def = NodeCatalog.BuiltIn.Require(Desk);

        def.Name.ShouldBe("Desk");
        def.Category.ShouldBe(ModuleCategories.Routing);
        def.Inputs.Count.ShouldBe(15);
        def.Outputs.Select(p => p.Name).ShouldBe(["left", "right", "bus left", "bus right"]);

        for (var channel = 1; channel <= 4; channel++)
        {
            def.Inputs[LeftIn(channel)].NeedsAWire.ShouldBeTrue();
            def.Inputs[RightIn(channel)].NormalledFrom.ShouldBe(LeftIn(channel));
            def.Inputs[Level(channel)].Default.ShouldBe(1f);
        }

        def.Inputs[Trim].Default.ShouldBe(1f);
    }

    [Fact]
    public void A_right_left_unpatched_carries_the_left()
    {
        var (left, right) = Heard(desk => desk
            .Feed(LeftIn(1), 0.5f)
            .Knob(Level(1), 0.5f));

        left.ShouldBe(0.25, 1e-12);
        right.ShouldBe(0.25, 1e-12);
    }

    [Fact]
    public void A_right_that_is_patched_is_its_own_side()
    {
        var (left, right) = Heard(desk => desk
            .Feed(LeftIn(1), 0.5f)
            .Feed(RightIn(1), -0.25f)
            .Feed(LeftIn(3), 0.125f));

        left.ShouldBe(0.625, 1e-12);
        right.ShouldBe(-0.125, 1e-12);
    }

    [Fact]
    public void The_trim_comes_before_the_rails_and_the_rails_hold()
    {
        var hot = Heard(desk => desk.Feed(LeftIn(1), 3f).Feed(RightIn(2), -5f));
        hot.Left.ShouldBe(1d);
        hot.Right.ShouldBe(-1d);

        var trimmed = Heard(desk => desk.Feed(LeftIn(1), 3f).Knob(Trim, 0.25f));
        trimmed.Left.ShouldBe(0.75, 1e-12);
    }

    /// <summary>
    /// Which is what lets two Desks be one of eight: what the first hands on has
    /// been neither trimmed nor clipped, so only the master does either.
    /// </summary>
    [Fact]
    public void The_bus_is_the_sum_before_the_trim_and_the_rails()
    {
        var (left, right) = Heard(
            desk => desk.Feed(LeftIn(1), 3f).Feed(RightIn(1), -2f).Knob(Trim, 0.1f),
            BusLeftOut,
            BusRightOut);

        left.ShouldBe(3d);
        right.ShouldBe(-2d);
    }

    [Fact]
    public void A_bus_patched_in_is_added_at_full_level()
    {
        var (left, right) = Heard(desk => desk
            .Feed(LeftIn(1), 0.25f)
            .Feed(BusLeftIn, 0.5f)
            .Feed(BusRightIn, -0.5f));

        left.ShouldBe(0.75, 1e-12);
        right.ShouldBe(-0.25, 1e-12);
    }

    /// <summary>
    /// Three buses into a master, a trim and a Clamp, against three Desks chained by
    /// their buses. Equal rather than close: a track moved onto this module is heard
    /// by ear, and "the same" has to mean it.
    /// </summary>
    [Fact]
    public void A_chain_of_desks_is_the_mixers_it_stands_for_to_the_last_bit()
    {
        float[] drums = [0.31f, -0.77f, 0.123f, 0.9f];
        float[] music = [-0.45f, 0.61f, 0.07f, -0.2f];
        float[] space = [0.5f, -0.33f, 0.19f];
        float[] drumLevels = [0.85f, 0.55f, 0.3f, 0.55f];
        float[] musicLevels = [0.7f, 0.4f, 0.5f, 0.45f];
        float[] spaceLevels = [0.45f, 0.5f, 0.4f];
        const float trim = 0.83f;

        var byHand = new PatchBuilder(NodeCatalog.BuiltIn);
        var master = byHand.Add("math.mixer");
        foreach (var (bus, (values, levels)) in new[] { (drums, drumLevels), (music, musicLevels), (space, spaceLevels) }.Index())
        {
            var mixer = byHand.Add("math.mixer");
            for (var ch = 0; ch < values.Length; ch++)
            {
                byHand.Wire(byHand.Add("value", (0, values[ch])), 0, mixer, ch * 2);
                mixer.InputValues[ch * 2 + 1] = levels[ch];
            }

            byHand.Wire(mixer, 0, master, bus * 2);
        }

        var trimmed = byHand.Add("math.mul", (1, trim));
        var safe = byHand.Add("math.clamp", (1, -1f), (2, 1f));
        var sink = byHand.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        byHand.Wire(master, 0, trimmed, 0).Wire(trimmed, 0, safe, 0).Wire(safe, 0, sink, NodeCatalog.OutputLeftPort);

        var chained = new PatchBuilder(NodeCatalog.BuiltIn);
        NodeInstance? before = null;
        foreach (var (values, levels) in new[] { (drums, drumLevels), (music, musicLevels), (space, spaceLevels) })
        {
            var desk = chained.Add(Desk);
            for (var ch = 0; ch < values.Length; ch++)
            {
                chained.Wire(chained.Add("value", (0, values[ch])), 0, desk, ch * 3);
                desk.InputValues[ch * 3 + 2] = levels[ch];
            }

            if (before is not null) chained.Wire(before, BusLeftOut, desk, BusLeftIn);
            before = desk;
        }

        before!.InputValues[Trim] = trim;
        var out2 = chained.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        chained.Wire(before, Left, out2, NodeCatalog.OutputLeftPort);

        Sample(chained.Patch).ShouldBe(Sample(byHand.Patch));
    }

    // --- harness -----------------------------------------------------------------

    private sealed class Rig(PatchBuilder b, NodeInstance desk)
    {
        public Rig Feed(int port, float value)
        {
            b.Wire(b.Add("value", (0, value)), 0, desk, port);
            return this;
        }

        public Rig Knob(int port, float value)
        {
            desk.InputValues[port] = value;
            return this;
        }
    }

    private static (double Left, double Right) Heard(Action<Rig> set, int left = Left, int right = Right)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var desk = b.Add(Desk);
        set(new Rig(b, desk));

        var sink = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(desk, left, sink, NodeCatalog.OutputLeftPort)
         .Wire(desk, right, sink, NodeCatalog.OutputRightPort);

        var result = b.Patch.CompileForAudio(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var registers = result.Program.AllocateRegisters();
        result.Program.Evaluate(0d, 0d, 0d, registers, default);

        return (registers[result.Program.OutputBase], registers[result.Program.OutputBase + 1]);
    }

    private static double Sample(Patch patch)
    {
        var result = patch.CompileForAudio(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));

        var registers = result.Program.AllocateRegisters();
        result.Program.Evaluate(0d, 0d, 0d, registers, default);

        return registers[result.Program.OutputBase];
    }
}
