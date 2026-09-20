using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A module switched off: out of the signal path, handing on whatever is patched
/// into it and nothing where nothing is.
/// </summary>
/// <remarks>
/// The compiler never enters one, so what these are really about is the socket at
/// the far end of its wires — which reads what it would read with the wire pulled
/// out: the signal arriving through the module, or its own knob.
/// </remarks>
public class SwitchedOffTests
{
    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    /// <summary>One sample of what the speakers would play, with the volume open.</summary>
    private static float Heard(Patch patch)
    {
        var result = patch.CompileForAudio(Modules);
        result.HasErrors.ShouldBeFalse();

        var registers = result.Program.AllocateRegisters();
        result.Program.Evaluate(0d, 0d, 0d, registers, default);

        return (float)registers[result.Program.OutputBase];
    }

    private static NodeInstance Sink(PatchBuilder b) =>
        b.Add(NodeCatalog.OutputTypeId, 400, 0, (NodeCatalog.OutputVolumePort, 1f));

    // --- what it hands on ----------------------------------------------------

    /// <summary>
    /// A Multiply between a value and the speakers, switched off: what was on its
    /// first socket arrives at the Output unmultiplied.
    /// </summary>
    [Fact]
    public void A_module_that_is_off_hands_on_what_is_patched_into_it()
    {
        var b = new PatchBuilder(Modules);

        var value = b.Add("value", (0, 0.3f));
        var half = b.Add("math.mul", (1, 0.5f));
        var output = Sink(b);

        b.Wire(value, 0, half, 0);
        b.Wire(half, 0, output, NodeCatalog.OutputLeftPort);

        Heard(b.Patch).ShouldBe(0.15f, 0.0001f);

        half.Off = true;

        Heard(b.Patch).ShouldBe(0.3f, 0.0001f);
    }

    /// <summary>
    /// And on through the next one: a chain of them is followed to whatever is
    /// patched in at the far end.
    /// </summary>
    [Fact]
    public void A_chain_of_modules_that_are_off_is_followed_to_the_end_of_it()
    {
        var b = new PatchBuilder(Modules);

        var value = b.Add("value", (0, 0.3f));
        var half = b.Add("math.mul", (1, 0.5f));
        var twice = b.Add("math.mul", (1, 2f));
        var output = Sink(b);

        b.Wire(value, 0, half, 0);
        b.Wire(half, 0, twice, 0);
        b.Wire(twice, 0, output, NodeCatalog.OutputLeftPort);

        half.Off = true;
        twice.Off = true;

        Heard(b.Patch).ShouldBe(0.3f, 0.0001f);
    }

    /// <summary>
    /// With nothing patched into it, a module that is off hands on nothing at all
    /// — and the socket it fed rests on its own knob, exactly as it would with the
    /// wire pulled out.
    /// </summary>
    [Fact]
    public void A_module_with_nothing_patched_into_it_hands_on_nothing()
    {
        var b = new PatchBuilder(Modules);

        var value = b.Add("value", (0, 0.3f));
        var level = b.Add("math.mul", (0, 0.25f), (1, 1f));
        var output = Sink(b);

        // Into the level rather than the signal, so what is left when the wire
        // stops carrying is a knob with a number on it and not zero.
        b.Wire(value, 0, level, 1);
        b.Wire(level, 0, output, NodeCatalog.OutputLeftPort);

        Heard(b.Patch).ShouldBe(0.075f, 0.0001f);

        value.Off = true;

        Heard(b.Patch).ShouldBe(0.25f, 0.0001f);
    }

    /// <summary>
    /// A normal is not handed on. Every oscillator's <c>in</c> is normalled to
    /// Time, so a voice switched off would otherwise pass a ramp down the patch
    /// instead of falling silent.
    /// </summary>
    [Fact]
    public void A_socket_carrying_only_its_normal_hands_on_nothing()
    {
        var b = new PatchBuilder(Modules);

        var osc = b.Add("osc.sine");
        var level = b.Add("math.mul", (0, 0.25f), (1, 1f));
        var output = Sink(b);

        b.Wire(osc, 0, level, 1);
        b.Wire(level, 0, output, NodeCatalog.OutputLeftPort);

        osc.Off = true;

        Heard(b.Patch).ShouldBe(0.25f, 0.0001f);
    }

    /// <summary>
    /// A module with two of each hands each output the input it is named after,
    /// so a geometry module switched off passes the position straight through
    /// rather than putting its <c>x</c> on both.
    /// </summary>
    [Fact]
    public void A_geometry_module_that_is_off_hands_x_to_x_and_y_to_y()
    {
        var rotate = Modules.Require("space.rotate");

        rotate.Through(0).ShouldBe(0);
        rotate.Through(1).ShouldBe(1);
    }

    /// <summary>
    /// A socket called <c>in</c> outranks a name that matches, because it is the
    /// signal input wherever it sits — an Echo's <c>left</c> is a delay time, and
    /// both its outputs want what came in.
    /// </summary>
    [Fact]
    public void A_socket_called_in_is_what_is_handed_on_however_the_outputs_are_named()
    {
        var remap = Modules.Require("math.remap");

        remap.Inputs[0].Name.ShouldBe("in");
        remap.Through(0).ShouldBe(0);

        var smoothstep = Modules.Require("math.smoothstep");

        smoothstep.Inputs[2].Name.ShouldBe("in");
        smoothstep.Through(0).ShouldBe(2);
    }

    /// <summary>A module with no inputs has nothing to hand on.</summary>
    [Fact]
    public void A_module_with_no_inputs_hands_on_nothing()
    {
        var coord = Modules.Require(NodeCatalog.CoordTypeId);

        coord.Inputs.ShouldBeEmpty();
        coord.Through(0).ShouldBe(-1);
    }

    // --- what it costs -------------------------------------------------------

    /// <summary>
    /// Nothing upstream of a module that is off is reached, so it costs the
    /// program nothing — the same sweep an unwired branch already gets (ADR-0011).
    /// </summary>
    [Fact]
    public void Nothing_upstream_of_a_module_that_is_off_is_compiled()
    {
        var b = new PatchBuilder(Modules);

        var osc = b.Add("osc.sine");
        var level = b.Add("math.mul");
        var output = Sink(b);

        b.Wire(osc, 0, level, 0);
        b.Wire(level, 0, output, NodeCatalog.OutputLeftPort);

        var before = b.Patch.CompileForAudio(Modules).Program.Ops.Length;

        level.Off = true;
        osc.Off = true;

        b.Patch.CompileForAudio(Modules).Program.Ops.Length.ShouldBeLessThan(before);
    }

    /// <summary>
    /// A Scope switched off does not tap. Its whole use is a side effect, so
    /// nothing else would have declined it: the walk never arrives at one.
    /// </summary>
    [Fact]
    public void A_scope_that_is_off_does_not_tap_what_it_was_watching()
    {
        var b = new PatchBuilder(Modules);

        var osc = b.Add("osc.sine");
        var scope = b.Add(NodeCatalog.ScopeTypeId);
        var output = Sink(b);

        b.Wire(osc, 0, scope, 0);
        b.Wire(osc, 0, output, NodeCatalog.OutputLeftPort);

        b.Patch.CompileForAudio(Modules).Program.Taps.Count.ShouldBe(1);

        scope.Off = true;

        b.Patch.CompileForAudio(Modules).Program.Taps.ShouldBeEmpty();
    }

    /// <summary>
    /// A ring of modules that are all off has nothing at the end of it to arrive
    /// at, and the walk stops rather than going round.
    /// </summary>
    [Fact]
    public void A_ring_of_modules_that_are_all_off_hands_on_nothing()
    {
        var b = new PatchBuilder(Modules);

        var first = b.Add("math.mul");
        var second = b.Add("math.mul");
        var output = Sink(b);

        b.Wire(first, 0, second, 0);
        b.Wire(second, 0, first, 0);
        b.Wire(second, 0, output, NodeCatalog.OutputLeftPort);

        first.Off = true;
        second.Off = true;

        Heard(b.Patch).ShouldBe(0f);
    }

    // --- what it survives ----------------------------------------------------

    /// <summary>A module that is off is still off after the patch has been through a file.</summary>
    [Fact]
    public void Being_off_survives_a_saved_patch()
    {
        var b = new PatchBuilder(Modules);

        var osc = b.Add("osc.sine");
        Sink(b);

        osc.Off = true;

        var read = PatchIO.Read(PatchIO.ToJson(b.Patch, Modules), Modules);

        read.Patch.Find(osc.Id).ShouldNotBeNull().Off.ShouldBeTrue();
    }

    /// <summary>
    /// And a patch with nothing off writes the file it always wrote, rather than
    /// a line saying so against every module in it.
    /// </summary>
    [Fact]
    public void A_patch_with_nothing_off_says_nothing_about_it()
    {
        var b = new PatchBuilder(Modules);

        b.Add("osc.sine");
        Sink(b);

        PatchIO.ToJson(b.Patch, Modules).ShouldNotContain("Off");
    }

    /// <summary>A copy is off where the module it was taken from is.</summary>
    [Fact]
    public void A_copy_of_a_module_that_is_off_is_off()
    {
        var node = NodeInstance.Create(Modules.Require("osc.sine"), 0, 0);

        node.Off = true;

        node.Clone(Guid.NewGuid()).Off.ShouldBeTrue();
    }

    /// <summary>
    /// A loop with a module switched off in it is still a loop: the wire that
    /// carries the evaluation before is cut wherever along the chain it runs, so
    /// what comes round is still a frame behind rather than the walk arriving at
    /// itself.
    /// </summary>
    [Fact]
    public void A_cycle_through_a_module_that_is_off_is_still_a_cycle()
    {
        var b = new PatchBuilder(Modules);

        var value = b.Add("value", (0, 0.1f));
        var sum = b.Add("math.add");
        var damp = b.Add("math.mul", (1, 0.5f));
        var output = Sink(b);

        b.Wire(value, 0, sum, 0);
        b.Wire(sum, 0, damp, 0);
        b.Wire(damp, 0, sum, 1);
        b.Wire(sum, 0, output, NodeCatalog.OutputLeftPort);

        damp.Off = true;

        var result = b.Patch.CompileForAudio(Modules);

        result.Issues.ShouldBeEmpty();

        // Undamped now, so each evaluation adds the whole of what it held before.
        var registers = result.Program.AllocateRegisters();
        var planes = new float[Math.Max(1, result.Program.PlaneCount)];

        result.Program.Evaluate(0d, 0d, 0d, registers, default, planes: planes);
        var first = (float)registers[result.Program.OutputBase];

        result.Program.Evaluate(0d, 0d, 0d, registers, default, planes: planes);
        var second = (float)registers[result.Program.OutputBase];

        first.ShouldBe(0.1f, 0.0001f);
        second.ShouldBe(0.2f, 0.0001f);
    }

    /// <summary>
    /// The same loop the other way round, so that the wire the cut falls on is
    /// the one leaving the module that is off. Following it through would lose
    /// the cut, and the walk would arrive back at a module it was already
    /// lowering.
    /// </summary>
    [Fact]
    public void A_cycle_cut_at_a_module_that_is_off_keeps_its_delay()
    {
        var b = new PatchBuilder(Modules);

        var value = b.Add("value", (0, 0.1f));
        var sum = b.Add("math.add");
        var damp = b.Add("math.mul", (1, 0.5f));
        var output = Sink(b);

        b.Wire(value, 0, sum, 0);
        b.Wire(sum, 0, damp, 0);
        b.Wire(damp, 0, sum, 1);

        // The Output hangs off the far side of the loop this time.
        b.Wire(damp, 0, output, NodeCatalog.OutputLeftPort);

        damp.Off = true;

        var result = b.Patch.CompileForAudio(Modules);

        result.Issues.ShouldBeEmpty();

        var registers = result.Program.AllocateRegisters();
        var planes = new float[Math.Max(1, result.Program.PlaneCount)];

        result.Program.Evaluate(0d, 0d, 0d, registers, default, planes: planes);
        ((float)registers[result.Program.OutputBase]).ShouldBe(0.1f, 0.0001f);

        result.Program.Evaluate(0d, 0d, 0d, registers, default, planes: planes);
        ((float)registers[result.Program.OutputBase]).ShouldBe(0.2f, 0.0001f);
    }
}
