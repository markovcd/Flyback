using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Sockets that are already carrying a signal with nothing patched into them:
/// an oscillator's <c>in</c> from Time, a pattern's <c>x</c> and <c>y</c> from
/// Coordinates.
/// </summary>
/// <remarks>
/// The rack's normalled jack, and the same rules as the one the Output's
/// <c>right</c> already had — a wire overrides it, and unplugging brings it
/// back. What is new is where the signal comes from: a module that is not in the
/// patch, held once for the whole program and drawn nowhere.
/// <para>
/// Everything here runs against a catalogue passed by hand rather than the
/// installed one, so a plugin on this machine cannot change what it means.
/// </para>
/// </remarks>
public class NormalledTests
{
    private static readonly ModuleProvider Extras = new("test.extras", "Extra modules");

    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    /// <summary>An oscillator into the screen, and nothing else.</summary>
    private static Patch Oscillating(out NodeInstance osc)
    {
        var b = new PatchBuilder(Modules);

        var output = b.Add(NodeCatalog.OutputTypeId, 400, 0);
        osc = b.Add("osc.sine", 200, 0);

        b.Wire(osc, 0, output, NodeCatalog.OutputColorPort);
        return b.Patch;
    }

    private static int Count(CompileResult result, OpCode code) =>
        result.Program.Ops.Count(op => op.Code == code);

    [Fact]
    public void An_oscillator_with_nothing_patched_into_it_reads_the_clock()
    {
        var compiled = Oscillating(out _).CompileForVideo(Modules);

        compiled.Issues.ShouldBeEmpty();
        Count(compiled, OpCode.LoadT).ShouldBe(1);
    }

    /// <summary>
    /// The knob under a normalled socket is not read, which is what makes this a
    /// normal rather than a default. It is left in the file rather than erased —
    /// a wire pulled off a socket that was never normalled has to find its knob
    /// where it left it, and one rule for storing values is simpler than two.
    /// </summary>
    [Fact]
    public void The_value_stored_against_a_normalled_socket_is_not_read()
    {
        var patch = Oscillating(out var osc);
        osc.InputValues[0] = 0.25f;

        var compiled = patch.CompileForVideo(Modules);

        Count(compiled, OpCode.LoadT).ShouldBe(1);
        osc.InputValues[0].ShouldBe(0.25f);
    }

    [Fact]
    public void A_wire_overrides_the_normal_and_unplugging_brings_it_back()
    {
        var b = new PatchBuilder(Modules);

        var output = b.Add(NodeCatalog.OutputTypeId, 400, 0);
        var osc = b.Add("osc.sine", 200, 0);
        var coords = b.Add(NodeCatalog.CoordTypeId, 0, 0);

        b.Wire(osc, 0, output, NodeCatalog.OutputColorPort);
        b.Wire(coords, NodeCatalog.CoordXPort, osc, 0);

        Count(b.Patch.CompileForVideo(Modules), OpCode.LoadT).ShouldBe(0);

        b.Patch.Disconnect(osc.Id, 0);

        Count(b.Patch.CompileForVideo(Modules), OpCode.LoadT).ShouldBe(1);
    }

    /// <summary>
    /// One hidden instance however many sockets read it, which is the whole of
    /// what "shared" buys: four oscillators cost one load, not four.
    /// </summary>
    [Fact]
    public void Every_socket_normalled_to_the_same_module_shares_one_instance()
    {
        var b = new PatchBuilder(Modules);

        var output = b.Add(NodeCatalog.OutputTypeId, 600, 0);
        var mix = b.Add("math.add", 400, 0);

        b.Wire(b.Add("osc.sine", 200, 0), 0, mix, 0);
        b.Wire(b.Add("osc.saw", 200, 100), 0, mix, 1);
        b.Wire(mix, 0, output, NodeCatalog.OutputColorPort);

        Count(b.Patch.CompileForVideo(Modules), OpCode.LoadT).ShouldBe(1);
    }

    /// <summary>
    /// A pattern module placed and left alone draws its pattern across the
    /// picture rather than sampling one point of it — the Coordinates half of
    /// the same rule.
    /// </summary>
    [Fact]
    public void A_pattern_with_nothing_patched_into_it_reads_the_pixels_position()
    {
        var b = new PatchBuilder(Modules);

        var output = b.Add(NodeCatalog.OutputTypeId, 400, 0);
        var rings = b.Add("pattern.rings", 200, 0);

        b.Wire(rings, 0, output, NodeCatalog.OutputColorPort);

        var compiled = b.Patch.CompileForVideo(Modules);

        Count(compiled, OpCode.LoadX).ShouldBe(1);
        Count(compiled, OpCode.LoadY).ShouldBe(1);
    }

    /// <summary>
    /// The x and y of one hidden Coordinates, not two of them, and not one each
    /// per module that asks.
    /// </summary>
    [Fact]
    public void Two_modules_normalled_to_Coordinates_share_one_of_it()
    {
        var b = new PatchBuilder(Modules);

        var output = b.Add(NodeCatalog.OutputTypeId, 600, 0);
        var mix = b.Add("math.add", 400, 0);

        b.Wire(b.Add("pattern.rings", 200, 0), 0, mix, 0);
        b.Wire(b.Add("pattern.checker", 200, 100), 0, mix, 1);
        b.Wire(mix, 0, output, NodeCatalog.OutputColorPort);

        var compiled = b.Patch.CompileForVideo(Modules);

        Count(compiled, OpCode.LoadX).ShouldBe(1);
        Count(compiled, OpCode.LoadY).ShouldBe(1);
    }

    /// <summary>
    /// A socket normalled to a module the catalogue does not hold falls back to
    /// its knob rather than to silence — and the complaint about a domain that
    /// never moves, which every built-in has outgrown, means what it always did
    /// there.
    /// </summary>
    [Fact]
    public void A_normal_naming_a_module_that_is_not_installed_falls_back_to_the_knob()
    {
        var absent = new NodeDef(
            "test.extras.orbit", "Orbit", "Test",
            [new PortSpec("in", NormalledTo: new PortNormal("test.extras.nowhere"), Domain: true)],
            [new PortSpec("out")],
            (em, i) => [em.Mul(i[0], 2f)]);

        var catalog = NodeCatalog.BuiltIn.With(Extras, [absent]).Catalog;

        var b = new PatchBuilder(catalog);
        var output = b.Add(NodeCatalog.OutputTypeId, 400, 0);
        var orbit = b.Add("test.extras.orbit", 200, 0);

        orbit.InputValues[0] = 0.5f;
        b.Wire(orbit, 0, output, NodeCatalog.OutputColorPort);

        var compiled = b.Patch.CompileForVideo(catalog);

        Count(compiled, OpCode.LoadT).ShouldBe(0);
        compiled.Issues.ShouldContain(i => i.Message.Contains("never moves"));
        compiled.Issues.ShouldAllBe(i => i.Severity == IssueSeverity.Warning);
    }

    /// <summary>
    /// A plugin may normal one of its own sockets to a module the engine ships,
    /// which is why a normal names a type id rather than holding a definition.
    /// </summary>
    [Fact]
    public void A_plugins_socket_may_be_normalled_to_a_module_the_engine_ships()
    {
        var borrowing = new NodeDef(
            "test.extras.orbit", "Orbit", "Test",
            [new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true)],
            [new PortSpec("out")],
            (em, i) => [em.Mul(i[0], 2f)]);

        var catalog = NodeCatalog.BuiltIn.With(Extras, [borrowing]).Catalog;

        var b = new PatchBuilder(catalog);
        var output = b.Add(NodeCatalog.OutputTypeId, 400, 0);

        b.Wire(b.Add("test.extras.orbit", 200, 0), 0, output, NodeCatalog.OutputColorPort);

        var compiled = b.Patch.CompileForVideo(catalog);

        compiled.Issues.ShouldBeEmpty();
        Count(compiled, OpCode.LoadT).ShouldBe(1);
    }

    [Fact]
    public void A_normalled_socket_is_named_by_the_module_and_the_output_it_reads()
    {
        var osc = Modules.Require("osc.sine");
        var rings = Modules.Require("pattern.rings");

        Modules.Normalled(osc.Inputs[0]).ShouldBe("Time");
        Modules.Normalled(rings.Inputs[0]).ShouldBe("Coordinates x");
        Modules.Normalled(rings.Inputs[1]).ShouldBe("Coordinates y");

        // A socket on its own knob has nothing driving it and is named nothing.
        Modules.Normalled(osc.Inputs[1]).ShouldBeNull();
    }

    /// <summary>
    /// Named by the catalogue that is being compiled against rather than by the
    /// installed one: the same socket is on its knob wherever the module it
    /// names is not loaded, and the editor has to say so.
    /// </summary>
    [Fact]
    public void A_normal_that_is_not_installed_is_named_nothing()
    {
        var absent = new PortSpec("in", NormalledTo: new PortNormal("test.extras.nowhere"));

        NodeCatalog.BuiltIn.Normalled(absent).ShouldBeNull();
    }
}
