using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Properties;

/// <summary>
/// Cycles. A patch may hold one as long as a Unit Delay sits somewhere in it, and
/// what the module buys is exactly one evaluation of latency — these pin that, the
/// error the compiler still owes a loop with nothing in it, and the two ways the
/// pair of ops can be got wrong.
/// </summary>
/// <remarks>
/// The graph tests feed their signal through the Coordinates module's x, the way
/// the delay and accumulator tests do: <see cref="CompiledPatch.Evaluate"/> takes
/// x per evaluation, which makes it the one way to hand a program an arbitrary
/// waveform without building an oscillator to produce it.
/// </remarks>
public class CycleInvariants
{
    private const int Rate = 1_000;

    /// <summary>A program built straight from the emitter, with no graph above it.</summary>
    private static CompiledPatch ProgramOf(Emitter emitter, Slot result) =>
        new(emitter.ToProgram(), emitter.RegisterCount, result.Base, 1);

    /// <summary>
    /// Runs a program once per entry in <paramref name="x"/> and hands back what
    /// its output register held each time.
    /// </summary>
    /// <param name="state">
    /// Null builds one to fit. Passing null on purpose is how the video path is
    /// tested — see <see cref="A_loop_with_no_state_behind_it_is_simply_open"/>.
    /// </param>
    private static float[] Run(CompiledPatch program, float[] x, bool stateless = false)
    {
        var memory = stateless
            ? null
            : new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);

        var registers = program.AllocateRegisters();
        var output = new float[x.Length];

        for (var i = 0; i < x.Length; i++)
        {
            program.Evaluate(x[i], 0d, 0d, registers, default, memory);
            output[i] = (float)registers[program.OutputBase];
        }

        return output;
    }

    private static float[] Ramp(int length) =>
        [.. Enumerable.Range(1, length).Select(i => (float)i)];

    // --- the latency itself --------------------------------------------------

    /// <summary>
    /// The whole contract in one test: what comes out of a cell is what went into
    /// it last time, never this time. Everything else about cycles rests on this.
    /// </summary>
    [Fact]
    public void A_cell_hands_back_what_the_previous_evaluation_wrote()
    {
        var emitter = new Emitter();
        var slot = emitter.AllocateUnitSlot();

        var read = emitter.UnitRead(slot);
        emitter.UnitWrite(slot, emitter.Load(OpCode.LoadX));

        var signal = Ramp(6);
        var output = Run(ProgramOf(emitter, read), signal);

        // Nothing has been written when the first evaluation reads, so it reads
        // silence rather than a guess.
        output[0].ShouldBe(0f);

        for (var i = 1; i < signal.Length; i++)
            output[i].ShouldBe(signal[i - 1], 1e-6f);
    }

    /// <summary>
    /// A loop that adds to itself, which is the smallest thing a cycle can be that
    /// a straight-line program could not already do.
    /// </summary>
    [Fact]
    public void A_cell_fed_from_itself_accumulates()
    {
        var emitter = new Emitter();
        var slot = emitter.AllocateUnitSlot();

        var read = emitter.UnitRead(slot);
        emitter.UnitWrite(slot, emitter.Add(read, 1f));

        var output = Run(ProgramOf(emitter, read), new float[5]);

        output.ShouldBe([0f, 1f, 2f, 3f, 4f]);
    }

    /// <summary>
    /// Every read must be emitted before any write, or a loop would see its own
    /// value arrive within one evaluation and the latency would be nothing. This
    /// is the ordering the compiler's deferred drain exists to produce, checked
    /// structurally rather than through what it sounds like.
    /// </summary>
    [Fact]
    public void Every_read_is_emitted_before_the_write_that_fills_it()
    {
        var patch = FeedbackFm();
        var ops = patch.CompileForAudio().Program.Ops;

        var reads = ops
            .Index()
            .Where(o => o.Item.Code == OpCode.UnitRead)
            .ToDictionary(o => (int)o.Item.K, o => o.Index);

        var writes = ops
            .Index()
            .Where(o => o.Item.Code == OpCode.UnitWrite)
            .ToDictionary(o => (int)o.Item.K, o => o.Index);

        reads.ShouldNotBeEmpty();
        writes.Keys.ShouldBe(reads.Keys, ignoreOrder: true);

        // Not merely each read before its own write: before *every* write, which
        // is what keeps two cells in one loop from collapsing into one.
        var lastRead = reads.Values.Max();
        foreach (var (slot, at) in writes)
            at.ShouldBeGreaterThan(lastRead, $"the write for cell {slot} runs too early");
    }

    // --- what the compiler accepts and refuses ------------------------------

    /// <summary>
    /// An oscillator patched into its own phase — the patch this whole mechanism
    /// exists for, and the one a rack of one-sample modules would let you draw
    /// without asking.
    /// </summary>
    /// <param name="index">
    /// How hard the previous evaluation bends the next phase. Zero is the same
    /// patch with the loop carrying nothing, which is what the timbre is compared
    /// against.
    /// </param>
    private static Patch FeedbackFm(float index = 0.5f)
    {
        var b = new PatchBuilder();

        var time = b.Add("time", 0, 0);
        var sine = b.Add("osc.sine", 200, 0);
        var depth = b.Add("value", 200, 200, (0, index));
        var gain = b.Add("math.mul", 400, 100);
        var unit = b.Add("feedback.unit", 600, 100);
        var sink = b.Add(NodeCatalog.OutputTypeId, 800, 0, (NodeCatalog.OutputGainPort, 1f));

        b.Wire(time, 0, sine, 0)
         .Wire(sine, 0, gain, 0)
         .Wire(depth, 0, gain, 1)
         .Wire(gain, 0, unit, 0)
         .Wire(unit, 0, sine, 2)
         .Wire(sine, 0, sink, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }

    [Fact]
    public void A_cycle_through_a_unit_delay_compiles()
    {
        var result = FeedbackFm().CompileForAudio();

        result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));
        result.Program.UnitCount.ShouldBe(1);
    }

    /// <summary>
    /// The refusal has to survive, or the check has simply been removed. A loop of
    /// plain maths has nothing in it that remembers and no evaluation to be
    /// resolved across.
    /// </summary>
    [Fact]
    public void A_cycle_with_nothing_in_it_that_remembers_is_still_refused()
    {
        var b = new PatchBuilder();

        var add = b.Add("math.add", 200, 0);
        var twice = b.Add("math.mul", 400, 0, (1, 2f));
        var sink = b.Add(NodeCatalog.OutputTypeId, 600, 0);

        b.Wire(add, 0, twice, 0)
         .Wire(twice, 0, add, 0)
         .Wire(add, 0, sink, NodeCatalog.OutputLeftPort);

        var result = b.Patch.CompileForAudio();

        result.HasErrors.ShouldBeTrue();
        result.Issues.ShouldContain(i => i.Message.Contains("feeds back into itself"));

        // And the message has to name the way out, not just the problem.
        result.Issues.ShouldContain(i => i.Message.Contains("Unit Delay"));
    }

    /// <summary>
    /// A cycle upstream of a breaker's own input is still a cycle. The drain
    /// resolves that side after the main walk, so it needs the same guard.
    /// </summary>
    [Fact]
    public void A_cycle_behind_a_unit_delays_input_is_refused_too()
    {
        var b = new PatchBuilder();

        var add = b.Add("math.add", 0, 0);
        var twice = b.Add("math.mul", 200, 0, (1, 2f));
        var unit = b.Add("feedback.unit", 400, 0);
        var sink = b.Add(NodeCatalog.OutputTypeId, 600, 0);

        b.Wire(add, 0, twice, 0)
         .Wire(twice, 0, add, 0)
         .Wire(twice, 0, unit, 0)
         .Wire(unit, 0, sink, NodeCatalog.OutputLeftPort);

        b.Patch.CompileForAudio().HasErrors.ShouldBeTrue();
    }

    // --- how the pieces are counted and shared -----------------------------

    /// <summary>
    /// Two breakers in one loop are two cells, and each costs its own evaluation.
    /// Chaining them is the only way to ask for more latency than one.
    /// </summary>
    [Fact]
    public void Two_unit_delays_in_a_row_cost_two_evaluations()
    {
        var b = new PatchBuilder();

        var coord = b.Add("coord", 0, 0);
        var first = b.Add("feedback.unit", 200, 0);
        var second = b.Add("feedback.unit", 400, 0);
        var sink = b.Add(NodeCatalog.OutputTypeId, 600, 0, (NodeCatalog.OutputGainPort, 1f));

        b.Wire(coord, 0, first, 0)
         .Wire(first, 0, second, 0)
         .Wire(second, 0, sink, NodeCatalog.OutputLeftPort);

        var program = b.Patch.CompileForAudio().Program;
        program.UnitCount.ShouldBe(2);

        var signal = Ramp(6);
        var heard = Run(program, signal);

        heard[0].ShouldBe(0f);
        heard[1].ShouldBe(0f);

        for (var i = 2; i < signal.Length; i++)
            heard[i].ShouldBe(signal[i - 2], 1e-6f);
    }

    /// <summary>
    /// One module is one cell however many things read from it — otherwise a
    /// breaker whose output fanned out would be written twice and read as two
    /// different signals.
    /// </summary>
    [Fact]
    public void A_unit_delay_read_twice_is_still_one_cell()
    {
        var b = new PatchBuilder();

        var coord = b.Add("coord", 0, 0);
        var unit = b.Add("feedback.unit", 200, 0);
        var sum = b.Add("math.add", 400, 0);
        var sink = b.Add(NodeCatalog.OutputTypeId, 600, 0, (NodeCatalog.OutputGainPort, 1f));

        b.Wire(coord, 0, unit, 0)
         .Wire(unit, 0, sum, 0)
         .Wire(unit, 0, sum, 1)
         .Wire(sum, 0, sink, NodeCatalog.OutputLeftPort);

        var program = b.Patch.CompileForAudio().Program;

        program.UnitCount.ShouldBe(1);
        program.Ops.Count(o => o.Code == OpCode.UnitRead).ShouldBe(1);
        program.Ops.Count(o => o.Code == OpCode.UnitWrite).ShouldBe(1);

        // Both sides of the Add see the same previous value, so it doubles it.
        var signal = Ramp(4);
        var heard = Run(program, signal);

        for (var i = 1; i < signal.Length; i++)
            heard[i].ShouldBe(signal[i - 1] * 2f, 1e-6f);
    }

    /// <summary>Cells are not lines and not accumulators, and are counted apart from both.</summary>
    [Fact]
    public void A_program_declares_the_cells_it_needs()
    {
        var emitter = new Emitter();

        var a = emitter.AllocateUnitSlot();
        var b = emitter.AllocateUnitSlot();

        emitter.UnitRead(a);
        emitter.UnitRead(b);
        emitter.UnitWrite(a, emitter.Constant(1f));
        emitter.UnitWrite(b, emitter.Constant(2f));

        var program = ProgramOf(emitter, Slot.Scalar(0));

        program.UnitCount.ShouldBe(2);
        program.DelayLengths.ShouldBeEmpty();
        program.PhaseCount.ShouldBe(0);
    }

    // --- the edges ----------------------------------------------------------

    /// <summary>
    /// The video path passes no state, and there a loop has to read as nothing
    /// rather than as anything made up — the same fallback the delay lines take,
    /// and for the same reason: a pixel has no previous evaluation.
    /// </summary>
    [Fact]
    public void A_loop_with_no_state_behind_it_is_simply_open()
    {
        var emitter = new Emitter();
        var slot = emitter.AllocateUnitSlot();

        var read = emitter.UnitRead(slot);
        emitter.UnitWrite(slot, emitter.Add(read, 1f));

        var output = Run(ProgramOf(emitter, read), new float[4], stateless: true);

        output.ShouldAllBe(v => v == 0f);
    }

    /// <summary>
    /// A cycle drawn as wires has no feedback coefficient anywhere in it, so a
    /// loop with a gain above one is easy to build. It must saturate rather than
    /// reach infinity — a patch pinned at the rails is audible and recoverable,
    /// and one full of NaN is neither.
    /// </summary>
    [Fact]
    public void A_runaway_loop_saturates_instead_of_exploding()
    {
        var emitter = new Emitter();
        var slot = emitter.AllocateUnitSlot();

        var read = emitter.UnitRead(slot);
        emitter.UnitWrite(slot, emitter.Add(emitter.Mul(read, 4f), 1f));

        var output = Run(ProgramOf(emitter, read), new float[64]);

        output.ShouldAllBe(v => float.IsFinite(v));
        output[^1].ShouldBe(16f);
    }

    /// <summary>
    /// A breaker with nothing patched in is a cell nothing ever writes but its own
    /// knob, which is a constant one evaluation late rather than an error.
    /// </summary>
    [Fact]
    public void A_unit_delay_with_nothing_patched_in_carries_its_knob()
    {
        var b = new PatchBuilder();

        var unit = b.Add("feedback.unit", 200, 0, (0, 0.25f));
        var sink = b.Add(NodeCatalog.OutputTypeId, 400, 0, (NodeCatalog.OutputGainPort, 1f));

        b.Wire(unit, 0, sink, NodeCatalog.OutputLeftPort);

        var result = b.Patch.CompileForAudio();
        result.HasErrors.ShouldBeFalse();

        var heard = Run(result.Program, new float[3]);

        heard[0].ShouldBe(0f);
        heard[1].ShouldBe(0.25f, 1e-6f);
        heard[2].ShouldBe(0.25f, 1e-6f);
    }

    /// <summary>
    /// A loop only the ear reaches must cost the eye nothing, which is the same
    /// dead-code elimination every other module gets — and worth pinning here
    /// because a breaker is resolved by a path of its own.
    /// </summary>
    [Fact]
    public void A_loop_wired_only_for_the_ear_emits_nothing_for_the_screen()
    {
        var video = FeedbackFm().CompileForVideo();

        video.Program.UnitCount.ShouldBe(0);
        video.Program.Ops.ShouldNotContain(o => o.Code == OpCode.UnitRead || o.Code == OpCode.UnitWrite);
    }

    /// <summary>
    /// The whole stack, through the renderer that actually plays it: a sine whose
    /// output bends its own phase must come out a different and richer shape than
    /// one whose does not. Everything above tests a piece — this is the only test
    /// that says the pieces add up to feedback.
    /// </summary>
    [Fact]
    public void A_loop_through_the_renderer_changes_the_sound()
    {
        var plain = Heard(FeedbackFm(index: 0f));
        var bent = Heard(FeedbackFm(index: 0.8f));

        // A cycle has no coefficient anywhere in it, so staying bounded is the
        // first thing worth knowing about one.
        bent.ShouldAllBe(v => float.IsFinite(v));
        bent.Max().ShouldBeInRange(0.1f, 1.1f);

        // It is doing something at all.
        plain.Zip(bent, (a, b) => Math.Abs(a - b)).Max().ShouldBeGreaterThan(0.05f);

        // And what it does is add harmonics, which a plain sine has none of. Sign
        // changes stand in for a spectrum: cheap, and monotone in what is being
        // asked about.
        Crossings(bent).ShouldBeGreaterThan(Crossings(plain));
    }

    /// <summary>A second of the left channel, rendered the way the speakers get it.</summary>
    private static float[] Heard(Patch patch)
    {
        var program = patch.CompileForAudio().Program;
        var renderer = new AudioRenderer();
        renderer.Prepare(program);

        var frames = AudioRenderer.DefaultSampleRate;
        var buffer = new float[frames * 2];
        renderer.Render(program, buffer, AudioScan.TimeDriven);

        return [.. Enumerable.Range(0, frames).Select(i => buffer[i * 2])];
    }

    /// <summary>Sign changes, which rise with the harmonics a self-modulated sine gains.</summary>
    private static int Crossings(float[] signal)
    {
        var count = 0;

        for (var i = 1; i < signal.Length; i++)
            if ((signal[i - 1] < 0f) != (signal[i] < 0f))
                count++;

        return count;
    }
}
