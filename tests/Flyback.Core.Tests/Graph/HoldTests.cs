using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Sample &amp; Hold: what was on its input the moment its trigger rose, held
/// until the next rise.
/// </summary>
/// <remarks>
/// A module with memory, so nothing here can sample it at scattered moments —
/// it has to be stepped in order, the way the renderer steps it, or the cells
/// answer with whatever the last evaluation happened to leave in them.
/// </remarks>
public class HoldTests
{
    private const int Rate = 48_000;

    /// <summary>
    /// A Hold reading two Values, so both its sockets can be moved from outside
    /// without a signal generator in the way.
    /// </summary>
    private sealed class Rig
    {
        private readonly DelayState memory;
        private readonly NodeInstance signal;
        private readonly NodeInstance trigger;
        private readonly Patch patch;

        public Rig()
        {
            var b = new PatchBuilder(NodeCatalog.BuiltIn);

            signal = b.Add("value", 0, 0);
            trigger = b.Add("value", 0, 100, (0, 0f));

            var hold = b.Add(NodeCatalog.HoldTypeId, 200, 0);
            var sink = b.Add(NodeCatalog.OutputTypeId, 400, 0, (NodeCatalog.OutputGainPort, 1f));

            // Both sinks, because the two answers differ and both are the point:
            // the speakers read 'left' and the screen reads 'color', and a node
            // wired only to one of them is never visited by the other.
            b.Wire(signal, 0, hold, 0)
             .Wire(trigger, 0, hold, 1)
             .Wire(hold, 0, sink, NodeCatalog.OutputLeftPort)
             .Wire(hold, 0, sink, NodeCatalog.OutputColorPort);

            patch = b.Patch;

            var program = Audio();
            Cells = program.UnitCount;
            memory = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        }

        public int Evaluations { get; private set; }

        public int Cells { get; }

        /// <summary>One evaluation with the two sockets set, and what came out of it.</summary>
        /// <remarks>
        /// Recompiled per step rather than poked, because a knob is folded into
        /// the program as a literal and there is no other way to move one. The
        /// registers go with the program — two knobs that happen to be equal
        /// share a literal, so the bank is not even the same size from one step
        /// to the next — and the cells do not, which is what makes this one
        /// continuing run rather than a series of first evaluations.
        /// </remarks>
        public double Step(float value, float gate)
        {
            signal.InputValues[0] = value;
            trigger.InputValues[0] = gate;

            var program = Audio();
            var registers = program.AllocateRegisters();

            program.Evaluate(0f, 0f, Evaluations / (float)Rate, registers, default, memory);
            Evaluations++;

            return registers[program.OutputBase];
        }

        /// <summary>What the picture makes of the same patch, which has no memory at all.</summary>
        public double Drawn(float value, float gate)
        {
            signal.InputValues[0] = value;
            trigger.InputValues[0] = gate;

            var video = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
            var registers = video.AllocateRegisters();

            video.Evaluate(0f, 0f, 0f, registers, default);
            return registers[video.OutputBase];
        }

        private CompiledPatch Audio() => patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
    }

    [Fact]
    public void It_holds_what_arrived_on_the_rising_edge()
    {
        var rig = new Rig();

        rig.Step(0.1f, 0f);
        rig.Step(0.2f, 1f).ShouldBe(0.2, 1e-6);

        // The input moves on and the output does not.
        rig.Step(0.7f, 1f).ShouldBe(0.2, 1e-6);
        rig.Step(0.9f, 1f).ShouldBe(0.2, 1e-6);
        rig.Step(0.9f, 0f).ShouldBe(0.2, 1e-6);
        rig.Step(0.5f, 0f).ShouldBe(0.2, 1e-6);

        // Until the trigger rises again.
        rig.Step(0.5f, 1f).ShouldBe(0.5, 1e-6);
    }

    /// <summary>
    /// A trigger left up holds one sample rather than tracking, which is what
    /// makes this a sample and hold and not a gate.
    /// </summary>
    [Fact]
    public void A_trigger_that_stays_up_takes_one_sample_and_no_more()
    {
        var rig = new Rig();

        rig.Step(0f, 0f);
        rig.Step(0.3f, 1f).ShouldBe(0.3, 1e-6);

        for (var i = 0; i < 100; i++) rig.Step(i / 100f, 1f).ShouldBe(0.3, 1e-6);
    }

    /// <summary>
    /// Halfway is the threshold everything here calls a gate open — the same one
    /// the ADSR uses, so one gate drives both and means the same thing to each.
    /// </summary>
    [Fact]
    public void The_trigger_counts_from_halfway_up()
    {
        var rig = new Rig();

        rig.Step(0f, 0f);
        rig.Step(0.6f, 0.49f).ShouldBe(0d, 1e-6);
        rig.Step(0.6f, 0.5f).ShouldBe(0.6, 1e-6);
    }

    /// <summary>
    /// The new sample is out on the evaluation the trigger rose, not the one
    /// after. The gate that fires this is usually the gate that opens the
    /// envelope, so one evaluation late would be the previous note's pitch heard
    /// at the start of this one.
    /// </summary>
    [Fact]
    public void The_sample_is_out_on_the_evaluation_it_was_taken()
    {
        var rig = new Rig();

        rig.Step(0.25f, 0f);
        rig.Step(0.8f, 0f);

        // The rise and the reading are the same evaluation.
        rig.Step(0.8f, 1f).ShouldBe(0.8, 1e-6);
    }

    /// <summary>
    /// The very first evaluation primes the cell, so a patch does not open on
    /// whatever nothing means to what is downstream — a note number of zero is
    /// a pitch nobody asked for.
    /// </summary>
    [Fact]
    public void It_opens_on_its_input_rather_than_on_nothing()
    {
        var rig = new Rig();

        rig.Step(57f, 0f).ShouldBe(57d, 1e-6);

        // And then holds it, because nothing has triggered yet.
        rig.Step(64f, 0f).ShouldBe(57d, 1e-6);
    }

    /// <summary>
    /// Two cells and no more: what is held, and the trigger as it was. The
    /// second is what makes an edge an edge, and there is no third thing to
    /// remember.
    /// </summary>
    [Fact]
    public void It_costs_two_cells_and_the_one_every_stateful_module_shares()
    {
        new Rig().Cells.ShouldBe(3);
    }

    /// <summary>
    /// The two things anybody most wants to hold are a note number and a
    /// frequency, and a cell is clamped to ±16 — so a Hold that stored what it
    /// was given would come back pinned at sixteen and play a wrong note with
    /// nothing anywhere to say why. It keeps them on a scale of its own.
    /// </summary>
    [Theory]
    [InlineData(57f)]
    [InlineData(127f)]
    [InlineData(440f)]
    [InlineData(-2000f)]
    [InlineData(4000f)]
    public void It_holds_a_pitch_rather_than_pinning_it_at_a_cell_s_bound(float value)
    {
        var rig = new Rig();

        rig.Step(0f, 0f);
        rig.Step(value, 1f).ShouldBe(value, 1e-6);
        rig.Step(0f, 0f).ShouldBe(value, 1e-6);
    }

    /// <summary>
    /// Audio only. A picture is one evaluation with nothing before it, so there
    /// is nothing to have held and the module is a wire — the same answer a
    /// Delay gives there.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void On_the_screen_it_is_a_wire(float gate)
    {
        new Rig().Drawn(0.42f, gate).ShouldBe(0.42, 1e-6);
    }
}
