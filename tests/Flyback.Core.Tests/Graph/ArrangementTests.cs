using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Arrangement: each part's level as a function of where the input has got to,
/// read off one compiled program swept through the sections on x.
/// </summary>
public class ArrangementTests
{
    private static readonly PartLevel[][] Song =
    [
        [new(1f), new(0f), new(1f, Glides: true), new(0.5f)],
        [new(0f), new(1f), new(1f), new(0f)],
    ];

    /// <summary>Every output at each of <paramref name="domain"/>, one section a unit of x.</summary>
    private static float[][] Run(
        IEnumerable<float> domain,
        IReadOnlyList<IReadOnlyList<PartLevel>>? parts = null,
        float fade = 0f,
        float before = 0f,
        float rate = 1f,
        float loop = 1f)
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId);
        var emitter = new Emitter();

        Slot[] inputs = Inputs(emitter, emitter.Load(OpCode.LoadX), rate, fade, before, loop);

        var tidy = ArrangementExtra.Tidy(parts ?? Song);
        var outputs = def.Emit(emitter, new EmitContext(inputs) { Parts = tidy });

        var program = new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, outputs[0].Base, 1);
        var registers = program.AllocateRegisters();

        return
        [
            .. domain.Select(x =>
            {
                program.Evaluate(x, 0f, 0f, registers, default);
                return outputs.Select(o => (float)registers[o.Base]).ToArray();
            }),
        ];
    }

    /// <summary>The Arrangement's inputs: 'in', 'rate', 'fade', every part's level before it starts, and 'loop'.</summary>
    private static Slot[] Inputs(Emitter emitter, Slot domain, float rate = 1f, float fade = 0f, float before = 0f, float loop = 1f) =>
        [
            domain, emitter.Constant(rate), emitter.Constant(fade),
            .. Enumerable.Repeat(emitter.Constant(before), NodeCatalog.MaxParts),
            emitter.Constant(loop),
        ];

    [Fact]
    public void Each_part_holds_its_level_through_its_section()
    {
        var readings = Run([0.5f, 1.5f, 3.5f]);

        readings.Select(r => r[0]).ShouldBe([1f, 0f, 0.5f], tolerance: 1e-5);
        readings.Select(r => r[1]).ShouldBe([0f, 1f, 0f], tolerance: 1e-5);
    }

    [Fact]
    public void A_gliding_level_travels_from_the_section_before_across_its_own()
    {
        var readings = Run([2f, 2.25f, 2.5f, 2.999f]);

        readings.Select(r => r[0]).ShouldBe([0f, 0.25f, 0.5f, 0.999f], tolerance: 1e-3);
    }

    [Fact]
    public void A_change_takes_as_long_as_the_fade_and_then_holds()
    {
        var readings = Run([1f, 1.1f, 1.25f, 1.5f], fade: 0.5f);

        readings[0][1].ShouldBe(0f, 1e-5);
        readings[1][1].ShouldBeInRange(0.01f, 0.99f);
        readings[2][1].ShouldBe(0.5f, 1e-5);
        readings[3][1].ShouldBe(1f, 1e-5);
    }

    [Fact]
    public void It_comes_round_to_the_first_section_after_the_last()
    {
        var readings = Run([4.5f, 5.5f]);

        readings[0][0].ShouldBe(1f, 1e-5);
        readings[1][1].ShouldBe(1f, 1e-5);
    }

    [Fact]
    public void Progress_and_section_say_where_it_is()
    {
        var readings = Run([0.25f, 2.75f]);
        const int progress = NodeCatalog.MaxParts, section = NodeCatalog.MaxParts + 1;

        readings[0][progress].ShouldBe(0.25f, 1e-5);
        readings[0][section].ShouldBe(1f);
        readings[1][progress].ShouldBe(0.75f, 1e-5);
        readings[1][section].ShouldBe(3f);
    }

    [Fact]
    public void A_part_it_does_not_have_holds_at_nought()
    {
        Run([0.5f, 1.5f]).ShouldAllBe(r => r[2] == 0f && r[NodeCatalog.MaxParts - 1] == 0f);
    }

    [Fact]
    public void With_no_parts_everything_holds_still()
    {
        Run([0.5f, 1.5f], parts: []).ShouldAllBe(r => r.Take(NodeCatalog.MaxParts + 2).All(v => v == 0f));
    }

    [Fact]
    public void A_short_part_holds_at_nought_to_the_end()
    {
        var tidy = ArrangementExtra.Tidy([[new PartLevel(1f)], [new(0f), new(0f), new(1f)]]);

        tidy[0].Select(l => l.Value).ShouldBe([1f, 0f, 0f]);
    }

    [Fact]
    public void Tidying_keeps_to_eight_parts_of_thirty_two_sections()
    {
        var many = Enumerable.Repeat(Enumerable.Repeat(new PartLevel(1f), 40), 10);

        var tidy = ArrangementExtra.Tidy(many);

        tidy.Count.ShouldBe(NodeCatalog.MaxParts);
        tidy.ShouldAllBe(part => part.Count == NodeCatalog.MaxSections);
    }

    [Fact]
    public void A_fresh_one_carries_parts_that_come_in_one_after_another()
    {
        var node = NodeInstance.Create(NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId), 0, 0);

        var parts = ArrangementExtra.Of(node);

        parts.Count.ShouldBe(3);
        parts.Select(p => p.Count(l => l.Value > 0f)).ShouldBe([4, 3, 2]);
    }

    /// <summary>A part costs its changes rather than its sections.</summary>
    [Fact]
    public void A_part_that_never_changes_costs_only_its_level()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId);

        int Ops(PartLevel[][] parts)
        {
            var emitter = new Emitter();
            def.Emit(emitter, new EmitContext(Inputs(emitter, emitter.Load(OpCode.LoadX))) { Parts = ArrangementExtra.Tidy(parts) });
            return emitter.ToProgram().Length;
        }

        var one = Ops([[new(1f), new(0f), new(1f), new(0f)]]);
        var two = Ops([[new(1f), new(0f), new(1f), new(0f)], [new(0.5f), new(0.5f), new(0.5f), new(0.5f)]]);

        two.ShouldBeLessThanOrEqualTo(one + 1);
    }
    [Fact]
    public void Before_it_starts_each_part_is_its_input()
    {
        var readings = Run([-3f, -0.5f], before: 0.25f);

        readings.ShouldAllBe(r => r.Take(NodeCatalog.MaxParts).All(v => Math.Abs(v - 0.25f) < 1e-5f));
        readings.ShouldAllBe(r => r[NodeCatalog.MaxParts] == 0f && r[NodeCatalog.MaxParts + 1] == 0f);
    }

    [Fact]
    public void The_first_time_through_its_first_section_comes_from_its_input()
    {
        var readings = Run([0f, 0.25f, 0.5f, 4.25f], parts: [[new(1f, Glides: true), new(0f)]], before: 0.5f);

        readings.Select(r => r[0]).ShouldBe([0.5f, 0.625f, 0.75f, 0.25f], tolerance: 1e-3);
    }

    [Fact]
    public void With_loop_off_it_stays_on_its_last_section()
    {
        var readings = Run([3.5f, 4.5f, 40f], loop: 0f);
        const int progress = NodeCatalog.MaxParts, section = NodeCatalog.MaxParts + 1;

        readings.Select(r => r[0]).ShouldBe([0.5f, 0.5f, 0.5f], tolerance: 1e-5);
        readings.Select(r => r[section]).ShouldBe([4f, 4f, 4f]);
        readings[2][progress].ShouldBe(1f, 1e-3);
    }

    [Fact]
    public void Next_reaches_nought_where_its_last_section_ends()
    {
        var readings = Run([0f, 2f, 4f], rate: 0.5f);
        const int next = NodeCatalog.MaxParts + 2;

        readings.Select(r => r[next]).ShouldBe([-8f, -6f, -4f], tolerance: 1e-4);
    }

    /// <summary>Two Arrangements of four sections, the second's parts and 'in' wired from the first.</summary>
    [Fact]
    public void A_chained_one_picks_up_where_the_one_before_ends()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ArrangementTypeId);
        var emitter = new Emitter();
        PartLevel[][] second = [[new(0.25f), new(0.25f), new(0.75f), new(0.75f)]];

        var first = def.Emit(emitter, new EmitContext(Inputs(emitter, emitter.Load(OpCode.LoadX), loop: 0f)) { Parts = ArrangementExtra.Tidy(Song) });

        Slot[] chained =
        [
            first[NodeCatalog.MaxParts + 2], emitter.Constant(1f), emitter.Constant(0f),
            .. first.Take(NodeCatalog.MaxParts),
            emitter.Constant(1f),
        ];
        var outputs = def.Emit(emitter, new EmitContext(chained) { Parts = ArrangementExtra.Tidy(second) });

        var program = new CompiledPatch(emitter.ToProgram(), emitter.RegisterCount, outputs[0].Base, 1);
        var registers = program.AllocateRegisters();

        float[] At(float x)
        {
            program.Evaluate(x, 0f, 0f, registers, default);
            return
            [
                (float)registers[outputs[0].Base], (float)registers[outputs[1].Base], (float)registers[outputs[NodeCatalog.MaxParts + 1].Base],
                (float)registers[first[NodeCatalog.MaxParts + 1].Base],
            ];
        }

        At(1.5f).ShouldBe([0f, 1f, 0f, 2f], tolerance: 1e-4);
        At(3.5f).ShouldBe([0.5f, 0f, 0f, 4f], tolerance: 1e-4);
        At(4.5f).ShouldBe([0.25f, 0f, 1f, 4f], tolerance: 1e-4);
        At(6.5f).ShouldBe([0.75f, 0f, 3f, 4f], tolerance: 1e-4);
        At(8.5f).ShouldBe([0.25f, 0f, 1f, 4f], tolerance: 1e-4);
    }
}
