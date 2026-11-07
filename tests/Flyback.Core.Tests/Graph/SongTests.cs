using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Song: each part's level as a function of where the input has got to, every part
/// looping at its own length, read off one compiled program swept through the sections on x.
/// </summary>
public class SongTests
{
    private static readonly SongPart[] Parts =
    [
        new("kick", [new(1f), new(0f)]),
        new("bass", [new(0f), new(1f), new(0.5f)]),
    ];

    private static float[][] Run(IEnumerable<float> domain, IEnumerable<SongPart>? parts = null, float fade = 0f)
    {
        var def = NodeCatalog.BuiltIn.Require(SongModule.TypeId);
        var emitter = new Emitter();

        Slot[] inputs = [emitter.Load(OpCode.LoadX), emitter.Constant(1f), emitter.Constant(fade)];

        var tidy = SongExtra.Tidy(parts ?? Parts).Select(part => part.Levels).ToList();
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

    private const int Progress = NodeCatalog.MaxParts;
    private const int Section = NodeCatalog.MaxParts + 1;
    private const int Played = NodeCatalog.MaxParts + 2;

    [Fact]
    public void Each_part_comes_round_after_its_own_last_section()
    {
        var readings = Run([0.5f, 1.5f, 2.5f, 3.5f, 4.5f, 5.5f]);

        readings.Select(r => r[0]).ShouldBe([1f, 0f, 1f, 0f, 1f, 0f], tolerance: 1e-5);
        readings.Select(r => r[1]).ShouldBe([0f, 1f, 0.5f, 0f, 1f, 0.5f], tolerance: 1e-5);
    }

    [Fact]
    public void The_section_counts_the_longest_part_and_progress_runs_through_each()
    {
        var readings = Run([0.25f, 1.5f, 2.75f, 3.25f]);

        readings.Select(r => r[Section]).ShouldBe([1f, 2f, 3f, 1f], tolerance: 1e-5);
        readings.Select(r => r[Progress]).ShouldBe([0.25f, 0.5f, 0.75f, 0.25f], tolerance: 1e-5);
        readings.Select(r => r[Played]).ShouldBe([0.25f, 1.5f, 2.75f, 3.25f], tolerance: 1e-5);
    }

    [Fact]
    public void A_change_on_the_way_round_fades_from_the_part_s_own_last_section()
    {
        // The bass comes round from 0.5 to 0 at x = 3, over half the section.
        var readings = Run([3f, 3.25f, 3.5f], fade: 0.5f);

        readings.Select(r => r[1]).ShouldBe([0.5f, 0.25f, 0f], tolerance: 1e-3);
    }

    [Fact]
    public void The_first_section_fades_in_from_nought()
    {
        var readings = Run([0f, 0.25f, 0.5f], fade: 0.5f);

        readings.Select(r => r[0]).ShouldBe([0f, 0.5f, 1f], tolerance: 1e-3);
    }

    [Fact]
    public void Sockets_with_no_part_hold_nought()
    {
        var readings = Run([0.5f, 1.5f]);

        readings.ShouldAllBe(r => r[2] == 0f && r[NodeCatalog.MaxParts - 1] == 0f);
    }

    [Fact]
    public void Names_are_made_writable_and_unique()
    {
        var tidy = SongExtra.Tidy([
            new("kick drum", [new(1f)]),
            new("kick_drum", [new(1f)]),
            new("9lives", [new(1f)]),
            new("progress", [new(1f)]),
            new("", []),
            new("part_2", [new(1f)]),
        ]);

        tidy.Select(part => part.Name).ShouldBe(["kick_drum", "kick_drum2", "_9lives", "progress2", "part5", "part_22"]);
        tidy[4].Levels.Count.ShouldBe(1);
        tidy.ShouldAllBe(part => SongExtra.Writable(part.Name));
    }

    [Fact]
    public void A_part_is_cut_at_the_most_sections_a_part_holds()
    {
        var tidy = SongExtra.Tidy([new("long", [.. Enumerable.Repeat(new PartLevel(1f), SongModule.MaxSections + 5)])]);

        tidy[0].Levels.Count.ShouldBe(SongModule.MaxSections);
    }

    [Fact]
    public void A_patch_takes_one_Song()
    {
        var patch = new Patch();
        patch.CanAdd(SongModule.TypeId).ShouldBeTrue();

        patch.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require(SongModule.TypeId), 0, 0));

        patch.CanAdd(SongModule.TypeId).ShouldBeFalse();
        patch.CanAdd(NodeCatalog.ArrangementTypeId).ShouldBeTrue();
    }

    [Fact]
    public void A_part_may_not_be_called_what_one_of_the_Song_s_sockets_is()
    {
        var tidy = SongExtra.Tidy([new("part_1", [new(1f)]), new("section", [new(1f)]), new("played", [new(1f)]), new("part_9", [new(1f)])]);

        tidy.Select(part => part.Name).ShouldBe(["part_12", "section2", "played2", "part_9"]);
    }
}
