using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;
using static Flyback.Plugins.Tests.MasteringBench;

namespace Flyback.Plugins.Tests;

public class WidthTests
{
    private const string Type = "flyback.mastering.width";

    private const int Width = 2;
    private const int MonoBelow = 3;

    private const int Mid = 2;
    private const int Side = 3;

    [Fact]
    public void The_mastering_plugin_offers_it_under_shaping_for_both()
    {
        var def = Catalog.Get(Type).ShouldNotBeNull();

        def.Name.ShouldBe("Width");
        def.Category.ShouldBe(ModuleCategories.Shaping);
        def.Sinks.ShouldBe(ModuleSinks.Both);
    }

    [Fact]
    public void At_one_it_leaves_the_pair_alone()
    {
        var left = Sine(440d, 0.7f, 0.1);
        var right = Sine(660d, 0.3f, 0.1);

        var heard = Play(Type, left, right);

        for (var i = 0; i < left.Length; i++)
        {
            heard.Left[i].ShouldBe(left[i], 1e-6f);
            heard.Right[i].ShouldBe(right[i], 1e-6f);
        }
    }

    [Fact]
    public void At_nought_it_is_mono()
    {
        var heard = Play(Type, Sine(440d, 0.7f, 0.1), Sine(660d, 0.3f, 0.1), knobs: [(Width, 0f)]);

        heard.Left.ShouldBe(heard.Right);
    }

    [Fact]
    public void Mid_and_side_are_the_halved_sum_and_difference()
    {
        var heard = Play(Type, Hold(0.6f, 0.01), Hold(0.2f, 0.01), heard: (Mid, Side));

        heard.Left[^1].ShouldBe(0.4f, 1e-6f);
        heard.Right[^1].ShouldBe(0.2f, 1e-6f);
    }

    [Fact]
    public void Mono_below_puts_the_bass_in_the_middle_and_leaves_the_top_wide()
    {
        var bass = Sine(20d, 0.5f, 1);
        var top = Sine(5000d, 0.5f, 0.2);

        var low = Play(Type, bass, [.. bass.Select(s => -s)], heard: (Side, Mid), knobs: [(MonoBelow, 200f)]);
        var high = Play(Type, top, [.. top.Select(s => -s)], heard: (Side, Mid), knobs: [(MonoBelow, 200f)]);

        Decibels(Settled(low.Left) / 0.5).ShouldBeLessThan(-15d);
        Decibels(Settled(high.Left) / 0.5).ShouldBe(0d, 0.1d);
    }

    [Fact]
    public void On_the_picture_width_still_works()
    {
        var b = new PatchBuilder(Catalog);
        var coord = b.Add(NodeCatalog.CoordTypeId);
        var width = b.Add(Type, (Width, 0f), (MonoBelow, 200f));
        var sink = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(coord, 0, width, 0).Wire(coord, 1, width, 1).Wire(width, 0, sink, NodeCatalog.OutputColorPort);

        var program = b.Patch.CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        program.Evaluate(0.6f, 0.2f, 0f, registers, default);
        ((float)registers[program.OutputBase]).ShouldBe(0.4f, 1e-6f);
    }
}
