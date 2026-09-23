using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>Chance: a coin flipped while the gate is shut, kept while it is open.</summary>
/// <remarks>Stepped in order, as the renderer steps it, because the coin lives in a cell.</remarks>
public class ChanceTests
{
    private const int Rate = GlobalConstants.SampleRate;

    /// <summary>A Chance reading a Value on its gate, with its knobs set from outside.</summary>
    private sealed class Rig
    {
        private readonly DelayState memory;
        private readonly NodeInstance gate;
        private readonly NodeInstance chance;
        private readonly Patch patch;

        public Rig(int port = 0, float seed = 0f)
        {
            var b = new PatchBuilder(NodeCatalog.BuiltIn);

            gate = b.Add("value", 0, 0, (0, 0f));
            chance = b.Add(NodeCatalog.ChanceTypeId, 200, 0, (2, seed));
            var sink = b.Add(NodeCatalog.OutputTypeId, 400, 0, (NodeCatalog.OutputVolumePort, 1f));

            b.Wire(gate, 0, chance, 0)
             .Wire(chance, port, sink, NodeCatalog.OutputLeftPort)
             .Wire(chance, port, sink, NodeCatalog.OutputColorPort);

            patch = b.Patch;

            var program = Audio();
            Cells = program.UnitCount;
            memory = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        }

        public int Cells { get; }

        private int evaluations;

        public double Step(float open, float odds)
        {
            gate.InputValues[0] = open;
            chance.InputValues[1] = odds;

            var program = Audio();
            var registers = program.AllocateRegisters();

            program.Evaluate(0f, 0f, evaluations / (float)Rate, registers, default, memory);
            evaluations++;

            return registers[program.OutputBase];
        }

        /// <summary>A note of <paramref name="length"/> samples after a shut one, and whether it played.</summary>
        public bool Note(float odds, int length = 4)
        {
            Step(0f, odds);

            var played = Step(1f, odds) > 0.5;
            for (var i = 1; i < length; i++) Step(1f, odds);

            return played;
        }

        /// <summary>What the picture makes of an open gate at <paramref name="time"/>, with no memory to keep a coin in.</summary>
        public double Drawn(float time)
        {
            gate.InputValues[0] = 1f;
            chance.InputValues[1] = 0.5f;

            var video = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
            var registers = video.AllocateRegisters();

            video.Evaluate(0f, 0f, time, registers, default);
            return registers[video.OutputBase];
        }

        private CompiledPatch Audio() => patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
    }

    /// <summary>Turning the knob mid-note changes the next note, never this one.</summary>
    [Fact]
    public void A_note_keeps_its_coin_while_the_knob_turns_under_it()
    {
        var rig = new Rig();

        rig.Step(0f, 1f);
        rig.Step(1f, 1f).ShouldBe(1d);
        rig.Step(1f, 0f).ShouldBe(1d);
        rig.Step(0.3f, 0f).ShouldBe(0.3, 1e-6);

        rig.Step(0f, 0f);
        rig.Step(1f, 0f).ShouldBe(0d);
        rig.Step(1f, 1f).ShouldBe(0d);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Gate_and_else_never_both_play_a_note_and_between_them_play_every_one(int port)
    {
        var mine = new Rig(port);
        var other = new Rig(1 - port);

        for (var i = 0; i < 200; i++)
            (mine.Note(0.5f) ^ other.Note(0.5f)).ShouldBeTrue($"note {i}");
    }

    [Fact]
    public void Two_seeds_choose_different_notes()
    {
        var one = new Rig(seed: 0f);
        var two = new Rig(seed: 1f);

        Enumerable.Range(0, 100).Count(_ => one.Note(0.5f) != two.Note(0.5f)).ShouldBeGreaterThan(20);
    }

    [Fact]
    public void On_the_screen_it_flips_a_new_coin_every_frame()
    {
        var rig = new Rig();
        var frames = Enumerable.Range(0, 60).Select(frame => rig.Drawn(frame / 60f)).ToArray();

        frames.ShouldAllBe(v => v == 0d || v == 1d);
        frames.Distinct().Count().ShouldBe(2);
    }

    /// <summary>One cell for the coin, and the one every stateful module shares.</summary>
    [Fact]
    public void It_costs_one_cell()
    {
        new Rig().Cells.ShouldBe(2);
    }
}
