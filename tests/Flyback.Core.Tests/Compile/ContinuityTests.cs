using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// What a patch remembers across an edit made while it is playing.
/// </summary>
/// <remarks>
/// A cell of memory is found by its position among the ops of its kind, and a
/// position is not an identity: add one oscillator anywhere and every
/// accumulator after it belongs to a different module than it did. Before
/// <see cref="StateOwners"/> the renderer had only the counts to compare, so any
/// change of shape threw the lot away — every tone in the patch restarting
/// together, every delay line emptied.
/// <para>
/// On a knob turn that is invisible. On a patch being edited while it plays it
/// is the difference between an instrument and a toy, so these are written from
/// the sound rather than from the internals: what is asserted is that the left
/// channel does not notice work done on the right.
/// </para>
/// </remarks>
public class ContinuityTests
{
    private const int Frames = 480;

    /// <summary>
    /// A tone on the left, and a builder still holding the patch so the test can
    /// go on editing it the way an editor would — the same nodes, with the same
    /// ids, before and after.
    /// </summary>
    private static (PatchBuilder Builder, NodeInstance Time, NodeInstance Sink) Playing()
    {
        var builder = new PatchBuilder();
        var time = builder.Add("time", 0, 0);
        var tone = builder.Add("osc.sine", 0, 0, (1, 440f));
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));

        builder.Wire(time, 0, tone, 0).Wire(tone, 0, sink, NodeCatalog.OutputLeftPort);

        return (builder, time, sink);
    }

    private static CompiledPatch Audio(PatchBuilder builder) =>
        builder.Patch.CompileForAudio().Program;

    /// <summary>Two buffers, with <paramref name="edit"/> compiled and swapped in between.</summary>
    private static (float[] First, float[] Second) Across(
        CompiledPatch before,
        Func<CompiledPatch> edit)
    {
        var renderer = new AudioRenderer();
        var memory = renderer.DelayMemoryFor(before);

        var first = new float[Frames * 2];
        renderer.Render(before, first, AudioScan.TimeDriven, memory);

        var after = edit();
        var carried = renderer.DelayMemoryFor(after, memory);

        var second = new float[Frames * 2];
        renderer.Render(after, second, AudioScan.TimeDriven, carried);

        return (first, second);
    }

    private static float[] Left(float[] buffer) =>
        [.. Enumerable.Range(0, buffer.Length / 2).Select(i => buffer[i * 2])];

    // --- what it sounds like ------------------------------------------------

    /// <summary>
    /// The one that matters. A tone is playing on the left; a second oscillator
    /// is added to the right while it plays; the left channel comes out exactly
    /// as it would have if nothing had been touched.
    /// </summary>
    /// <remarks>
    /// Compared against the same patch left alone rather than against a
    /// tolerance, because that is the claim: the edit is inaudible on the
    /// channel it did not reach. Both renderers have played the identical first
    /// buffer, so their decimation and DC state agree going into the second and
    /// any difference in it is the swap's doing.
    /// <para>
    /// Before this, adding the oscillator took the phase count from one to two,
    /// which said "different shape" and restarted the tone on the left as well.
    /// </para>
    /// </remarks>
    [Fact]
    public void Adding_a_module_does_not_disturb_a_tone_that_is_already_playing()
    {
        var (edited, time, sink) = Playing();
        var before = Audio(edited);

        var (_, second) = Across(before, () =>
        {
            var added = edited.Add("osc.sine", 0, 0, (1, 300f));

            edited.Wire(time, 0, added, 0).Wire(added, 0, sink, NodeCatalog.OutputRightPort);

            return Audio(edited);
        });

        var (untouched, _, _) = Playing();
        var alone = Audio(untouched);
        var (_, reference) = Across(alone, () => alone);

        Left(second).ShouldBe(Left(reference), tolerance: 1e-6);
    }

    /// <summary>
    /// And the tone itself really was still running, so the test above is not
    /// two silences agreeing with each other.
    /// </summary>
    [Fact]
    public void The_tone_under_that_test_is_actually_sounding()
    {
        var (builder, _, _) = Playing();
        var (_, second) = Across(Audio(builder), () => Audio(builder));

        Left(second).Max(Math.Abs).ShouldBeGreaterThan(0.5f);
    }

    /// <summary>
    /// The other half of it: a module that was not there a moment ago begins at
    /// the beginning. An accumulator takes no step on its first evaluation, so a
    /// sine that has just arrived starts at nought — and one that inherited a
    /// stranger's phase would not.
    /// </summary>
    [Fact]
    public void A_module_that_was_not_there_before_starts_from_the_beginning()
    {
        var builder = new PatchBuilder();
        var time = builder.Add("time", 0, 0);
        var tone = builder.Add("osc.sine", 0, 0, (1, 440f));
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));

        builder.Wire(time, 0, tone, 0).Wire(tone, 0, sink, NodeCatalog.OutputLeftPort);

        var renderer = new AudioRenderer();
        var before = Audio(builder);
        var memory = renderer.DelayMemoryFor(before);

        renderer.Render(before, new float[Frames * 2], AudioScan.TimeDriven, memory);

        var added = builder.Add("osc.sine", 0, 0, (1, 300f));

        builder.Wire(time, 0, added, 0).Wire(added, 0, sink, NodeCatalog.OutputRightPort);

        var after = Audio(builder);
        var carried = renderer.DelayMemoryFor(after, memory).ShouldNotBeNull();

        // Asked of the accumulators rather than of the samples, because a buffer
        // boundary is not where this shows: the decimator carries its own memory
        // across the swap, so the first sample out of a brand new oscillator is
        // mostly the filter still ringing from the tone before it.
        carried.Advance(Cell(after, added), input: 5.25d, frequency: 1d)
            .ShouldBe(0d, "a cell with no previous evaluation to measure against takes no step");

        carried.Advance(Cell(after, tone), input: 5.25d, frequency: 1d)
            .ShouldNotBe(0d, "and one that carried on has something to measure against");
    }

    /// <summary>Which accumulator belongs to <paramref name="node"/>.</summary>
    private static int Cell(CompiledPatch program, NodeInstance node)
    {
        var cell = program.Owners.Phases.ToList().IndexOf(node.Id);

        cell.ShouldBeGreaterThanOrEqualTo(0, "the compiler should have said whose this is");

        return cell;
    }

    /// <summary>
    /// Nothing changed, so nothing is rebuilt: the same object comes back, and a
    /// patch full of delay lines costs no allocation and no copy for a knob.
    /// </summary>
    [Fact]
    public void An_unchanged_patch_keeps_the_very_same_memory()
    {
        var (builder, _, _) = Playing();
        var renderer = new AudioRenderer();

        var memory = renderer.DelayMemoryFor(Audio(builder)).ShouldNotBeNull();

        renderer.DelayMemoryFor(Audio(builder), memory).ShouldBeSameAs(memory);
    }

    // --- what the compiler writes down --------------------------------------

    /// <summary>
    /// The compiler knows whose op it is emitting, and now says so. Without this
    /// there is nothing for a swap to match on.
    /// </summary>
    [Fact]
    public void The_compiler_says_which_module_each_accumulator_belongs_to()
    {
        var builder = new PatchBuilder();
        var time = builder.Add("time", 0, 0);
        var left = builder.Add("osc.sine", 0, 0, (1, 440f));
        var right = builder.Add("osc.saw", 0, 0, (1, 110f));
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));

        builder
            .Wire(time, 0, left, 0).Wire(left, 0, sink, NodeCatalog.OutputLeftPort)
            .Wire(time, 0, right, 0).Wire(right, 0, sink, NodeCatalog.OutputRightPort);

        var owners = Audio(builder).Owners;

        owners.Phases.Count.ShouldBe(2);
        owners.Phases.ShouldBe([left.Id, right.Id], ignoreOrder: true);
    }

    /// <summary>
    /// The two cells the compiler shares between modules belong to nobody, and
    /// have to: attributed to whichever module asked first, the interval would be
    /// thrown away the moment that module was deleted and read as a jump on the
    /// evaluation after.
    /// </summary>
    [Fact]
    public void The_shared_cells_belong_to_nobody_in_particular()
    {
        var builder = new PatchBuilder();
        var time = builder.Add("time", 0, 0);
        var envelope = builder.Add(NodeCatalog.AdsrTypeId, 0, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));

        builder.Wire(time, 0, envelope, 0).Wire(envelope, 0, sink, NodeCatalog.OutputLeftPort);

        Audio(builder).Owners.Units.ShouldContain(StateOwners.Shared);
    }

    /// <summary>A program written by hand claims nothing, and so is never handed anything.</summary>
    [Fact]
    public void A_program_assembled_by_hand_owns_none_of_it()
    {
        var program = new CompiledPatch([new Op(OpCode.Const, 0)], 1, 0, 1);

        program.Owners.ShouldBe(StateOwners.None);
    }

    // --- the matching itself ------------------------------------------------

    /// <summary>
    /// A cell is matched to its owner's cell in the same position, which is what
    /// survives every module around it moving.
    /// </summary>
    [Fact]
    public void A_cell_follows_its_owner_rather_than_its_slot()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        StateOwners.Adopt([b, a], [a, b]).ShouldBe([1, 0]);
    }

    /// <summary>
    /// One module may own several — a supersaw is seven accumulators under one
    /// handle — so within an owner they are still counted in order.
    /// </summary>
    [Fact]
    public void Several_cells_of_one_owner_are_matched_in_order()
    {
        var a = Guid.NewGuid();

        StateOwners.Adopt([a, a, a], [a, a]).ShouldBe([0, 1, -1]);
    }

    /// <summary>
    /// A cell nobody claims matches nothing, including another cell nobody
    /// claims. Not knowing who owns a cell is not the same as knowing two cells
    /// are the same cell, and being wrong here plays a module something that was
    /// never its own.
    /// </summary>
    [Fact]
    public void An_unclaimed_cell_is_never_adopted()
    {
        StateOwners.Adopt([Guid.Empty, Guid.Empty], [Guid.Empty, Guid.Empty])
            .ShouldBe([-1, -1]);
    }

    [Fact]
    public void A_cell_whose_owner_has_gone_starts_again()
    {
        StateOwners.Adopt([Guid.NewGuid()], [Guid.NewGuid()]).ShouldBe([-1]);
    }

    // --- delay lines --------------------------------------------------------

    /// <summary>One line, owned, so a test can change its length and keep its identity.</summary>
    private static CompiledPatch Line(Guid owner, float seconds)
    {
        var emitter = new Emitter { Owner = owner };
        var zero = emitter.Constant(0f);
        var line = emitter.DelayLine(OpCode.Delay, zero, zero, zero, seconds);

        return new CompiledPatch(
            emitter.ToProgram(),
            emitter.RegisterCount,
            line.Base,
            1,
            owners: emitter.Owners);
    }

    /// <summary>
    /// A delay time turned while the line is ringing keeps the tail. The samples
    /// are copied newest first into a ring of the new size, so what was in the
    /// air stays in the air.
    /// </summary>
    [Fact]
    public void A_delay_keeps_its_tail_when_its_length_changes()
    {
        const int rate = 48_000;

        var module = Guid.NewGuid();
        var old = new DelayState(Line(module, 0.05f), rate);

        for (var i = 1; i <= 100; i++) old.Write(0, i * 0.001d);

        var wanted = old.Read(0, 10d / rate, 0.05f);

        wanted.ShouldBe(0.09d, tolerance: 1e-4);

        var longer = new DelayState(Line(module, 0.08f), rate);

        longer.Adopt(old);

        longer.Read(0, 10d / rate, 0.08f).ShouldBe(wanted, tolerance: 1e-4);
    }

    /// <summary>And a different module's line is not handed somebody else's tail.</summary>
    [Fact]
    public void A_line_belonging_to_another_module_is_not_taken_over()
    {
        const int rate = 48_000;

        var old = new DelayState(Line(Guid.NewGuid(), 0.05f), rate);

        for (var i = 1; i <= 100; i++) old.Write(0, i * 0.001d);

        var other = new DelayState(Line(Guid.NewGuid(), 0.05f), rate);

        other.Adopt(old);

        other.Read(0, 10d / rate, 0.05f).ShouldBe(0d);
    }
}
