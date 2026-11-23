using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>Counter: rises on a trigger counted, coming round every 'length'.</summary>
/// <remarks>Stepped in order, as the renderer steps it, because the count lives in a cell.</remarks>
public class CounterTests
{
    private const int Rate = GlobalConstants.SampleRate;

    private const int Count = 0;
    private const int Phase = 1;
    private const int Wrap = 2;

    /// <summary>A Counter reading Values on its trigger and reset, one output on the speakers.</summary>
    private sealed class Rig
    {
        private readonly DelayState memory;
        private readonly NodeInstance trigger;
        private readonly NodeInstance reset;
        private readonly NodeInstance counter;
        private readonly Patch patch;
        private int evaluations;

        public Rig(int port = Count, float length = 4f)
        {
            var b = new PatchBuilder(NodeCatalog.BuiltIn);

            trigger = b.Add("value", 0, 0, (0, 0f));
            reset = b.Add("value", 0, 100, (0, 0f));
            counter = b.Add(CounterModule.TypeId, 200, 0, (1, length));
            var sink = b.Add(OutputModule.TypeId, 400, 0, (OutputModule.VolumePort, 1f));

            b.Wire(trigger, 0, counter, 0)
             .Wire(reset, 0, counter, 2)
             .Wire(counter, port, sink, OutputModule.LeftPort)
             .Wire(counter, port, sink, OutputModule.ColorPort);

            patch = b.Patch;

            var program = Audio();
            memory = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);

            // The first sample has no memory, so nothing rises on it.
            Step(0f);
        }

        public double Step(float level, float cleared = 0f, float? length = null)
        {
            trigger.InputValues[0] = level;
            reset.InputValues[0] = cleared;
            if (length is { } l) counter.InputValues[1] = l;

            var program = Audio();
            var registers = program.AllocateRegisters();

            program.Evaluate(0f, 0f, evaluations / (float)Rate, registers, default, memory);
            evaluations++;

            return registers[program.OutputBase];
        }

        /// <summary>One trigger, open for a few samples then shut, and what it read while open.</summary>
        public double Hit()
        {
            var open = Step(1f);
            Step(1f);
            Step(0f);
            Step(0f);

            return open;
        }

        public double Drawn(float level)
        {
            trigger.InputValues[0] = level;

            var video = patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
            var registers = video.AllocateRegisters();

            video.Evaluate(0f, 0f, 1f, registers, default);
            return registers[video.OutputBase];
        }

        private CompiledPatch Audio() => patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
    }

    [Fact]
    public void Each_rise_counts_one_and_a_held_trigger_counts_once()
    {
        var rig = new Rig();

        rig.Step(0f).ShouldBe(0d);
        rig.Step(1f).ShouldBe(1d);
        for (var i = 0; i < 20; i++) rig.Step(1f).ShouldBe(1d);
        rig.Step(0f).ShouldBe(1d);
        rig.Step(0.7f).ShouldBe(2d);
    }

    [Fact]
    public void It_comes_round_to_nought_every_length()
    {
        var rig = new Rig(length: 4f);

        Enumerable.Range(0, 9).Select(_ => rig.Hit()).ShouldBe([1d, 2d, 3d, 0d, 1d, 2d, 3d, 0d, 1d]);
    }

    [Fact]
    public void At_nought_length_it_never_comes_round()
    {
        var rig = new Rig(length: 0f);

        for (var i = 1; i <= 100; i++) rig.Hit().ShouldBe(i);
    }

    [Fact]
    public void It_counts_past_where_a_cell_is_clamped()
    {
        var rig = new Rig(length: 1000f);

        for (var i = 1; i < 999; i++) rig.Hit();
        rig.Hit().ShouldBe(999d);
        rig.Hit().ShouldBe(0d);
    }

    [Fact]
    public void Reset_holds_the_count_at_nought_while_it_is_open()
    {
        var rig = new Rig();

        rig.Hit();
        rig.Hit();
        rig.Step(0f, cleared: 1f).ShouldBe(0d);
        rig.Step(1f, cleared: 1f).ShouldBe(0d);
        rig.Step(0f).ShouldBe(0d);
        rig.Hit().ShouldBe(1d);
    }

    [Fact]
    public void A_shorter_length_brings_the_count_inside_it_at_once()
    {
        var rig = new Rig(length: 8f);

        for (var i = 0; i < 5; i++) rig.Hit();
        rig.Step(0f, length: 4f).ShouldBe(1d);
    }

    [Fact]
    public void Phase_is_how_far_round_it_is()
    {
        var rig = new Rig(Phase, length: 4f);

        Enumerable.Range(0, 5).Select(_ => rig.Hit()).ShouldBe([0.25, 0.5, 0.75, 0d, 0.25]);
    }

    [Fact]
    public void Wrap_passes_every_length_th_trigger_while_it_is_open()
    {
        var rig = new Rig(Wrap, length: 3f);

        var passed = new List<double>();
        for (var hit = 0; hit < 6; hit++)
        {
            passed.Add(rig.Step(1f));
            rig.Step(1f).ShouldBe(passed[^1]);
            rig.Step(0f).ShouldBe(0d);
        }

        passed.ShouldBe([0d, 0d, 1d, 0d, 0d, 1d]);
    }

    [Fact]
    public void At_a_length_of_one_wrap_passes_every_trigger()
    {
        var rig = new Rig(Wrap, length: 1f);

        for (var hit = 0; hit < 4; hit++) rig.Hit().ShouldBe(1d);
    }

    [Fact]
    public void Reset_does_not_open_wrap()
    {
        var rig = new Rig(Wrap, length: 2f);

        rig.Hit();
        rig.Step(1f, cleared: 1f).ShouldBe(0d);
    }

    [Theory]
    [InlineData(Count)]
    [InlineData(Phase)]
    [InlineData(Wrap)]
    public void On_the_picture_it_reads_nought(int port) =>
        new Rig(port).Drawn(1f).ShouldBe(0d);
}
