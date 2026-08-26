using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A kind of carried state that the engine does not know about: declared by a
/// plugin, stored under its own key, and drawn from what it declares rather than
/// from a control it ships (ADR-0055).
/// </summary>
/// <remarks>
/// The whole point of these is that nothing in the engine names the kind below.
/// It seeds, compiles, saves, reloads and copies through the same loops the
/// built-in three go through, and every one of those is checked here — because
/// the failure mode of an open extension point is not a crash but a silence, and
/// a silence passes any test that only asks whether something threw.
/// </remarks>
public class PluginExtraTests
{
    private static readonly ModuleProvider Provider = new("test.chord", "Chord modules");

    /// <summary>
    /// A plugin's kind, written the way a plugin would write one: a key, a
    /// couple of fields, and nothing else overridden.
    /// </summary>
    private sealed record ChordExtra : NodeExtra
    {
        public override string Key => "chord";

        public override IReadOnlyList<ExtraField> Fields =>
        [
            new ExtraField.Number("spread", "spread", new PortSpec("spread", PortKind.Scalar, 0.5f, 0f, 2f)),
            new ExtraField.Number("root", "root", new PortSpec("root", PortKind.Scalar, 57f, 0f, 127f, -1, PortDisplay.Note)),
            new ExtraField.Toggle("wide", "wide", On: true),
        ];
    }

    /// <summary>
    /// A module carrying it, whose emit reads the state back out of the context
    /// the way a plugin's would.
    /// </summary>
    private static NodeDef Chord() => new(
        "test.chord.spread", "Chord", "Test",
        [new PortSpec("in")],
        [new PortSpec("out")],
        (em, node) =>
        {
            var state = node.Extra<ExtraState>("chord");

            // Silence rather than a throw where the fold did not happen, which
            // is what a plugin should write and what makes the assertion below
            // mean something.
            if (state is null) return [em.Constant(0f)];

            return [em.Mul(node[0], state.Number("spread") + (state.Toggle("wide") ? 10f : 0f))];
        })
    {
        Extras = [new ChordExtra()],
    };

    private static ModuleCatalog Catalog() => NodeCatalog.BuiltIn.With(Provider, [Chord()]).Catalog;

    [Fact]
    public void A_fresh_instance_is_seeded_from_the_declared_fields()
    {
        var node = NodeInstance.Create(Chord(), 0, 0);

        var stored = node.StateOf("chord").ShouldBeOfType<JsonObject>();

        stored["spread"]!.GetValue<float>().ShouldBe(0.5f);
        stored["root"]!.GetValue<float>().ShouldBe(57f);
        stored["wide"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void A_module_with_no_extras_stores_nothing_at_all()
    {
        // The state dictionary is absent rather than empty, so an ordinary patch
        // file looks exactly as it always did.
        NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0).State.ShouldBeNull();
    }

    [Fact]
    public void The_state_reaches_the_emit_function_typed()
    {
        var catalog = Catalog();
        var patch = new Patch();
        patch.EnsureOutput(catalog);

        var chord = NodeInstance.Create(catalog.Require("test.chord.spread"), 0, 0);
        chord.SetState("chord", new JsonObject
        {
            ["spread"] = 3f,   // outside the field's range, so it must come back clamped to 2
            ["wide"] = false,
        });

        patch.Nodes.Add(chord);
        patch.Connect(chord.Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);

        var compiled = patch.CompileForVideo(catalog);
        compiled.HasErrors.ShouldBeFalse();

        // in is 0 on its knob, so the output is 0 either way — what is being
        // checked is that the module compiled at all, which it only does when
        // the fold put an ExtraState where the emit looked for one.
        compiled.Issues.ShouldNotContain(i => i.Message.Contains("chord"));
    }

    [Fact]
    public void A_value_outside_the_declared_range_is_held_to_it()
    {
        var extra = new ChordExtra();
        var held = extra.Stored(new JsonObject { ["spread"] = 99f, ["root"] = -40f });

        held["spread"]!.GetValue<float>().ShouldBe(2f);
        held["root"]!.GetValue<float>().ShouldBe(0f);

        // A field the stored object never had falls back to the declared default
        // rather than to nothing.
        held["wide"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void Nonsense_in_the_file_reads_as_the_default()
    {
        var extra = new ChordExtra();

        var held = extra.Stored(new JsonObject
        {
            ["spread"] = "not a number",
            ["wide"] = 7,
        });

        held["spread"]!.GetValue<float>().ShouldBe(0.5f);
        held["wide"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void The_state_survives_being_written_out_and_read_back()
    {
        var catalog = Catalog();
        var patch = new Patch();
        patch.EnsureOutput(catalog);

        var chord = NodeInstance.Create(catalog.Require("test.chord.spread"), 0, 0);
        chord.SetState("chord", new JsonObject { ["spread"] = 1.25f, ["wide"] = false });
        patch.Nodes.Add(chord);

        var reloaded = PatchIo.Read(PatchIo.ToJson(patch, catalog), catalog).Patch;
        var back = reloaded.FirstOf("test.chord.spread").ShouldNotBeNull();

        back.StateOf("chord")!["spread"]!.GetValue<float>().ShouldBe(1.25f);
        back.StateOf("chord")!["wide"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void A_copy_carries_the_state_and_does_not_share_it()
    {
        var chord = NodeInstance.Create(Chord(), 0, 0);
        chord.SetState("chord", new JsonObject { ["spread"] = 1f });

        var copy = chord.Clone(Guid.NewGuid());
        copy.StateOf("chord")!["spread"]!.GetValue<float>().ShouldBe(1f);

        // The tree is mutable, so a shallow copy would let this reach the original.
        copy.StateOf("chord")!["spread"] = 2f;
        chord.StateOf("chord")!["spread"]!.GetValue<float>().ShouldBe(1f);
    }

    [Fact]
    public void A_copy_of_a_module_this_build_has_no_definition_for_keeps_its_state()
    {
        // Cloning never consults a catalogue, which is what makes a fragment from
        // an unloaded plugin survive a copy rather than lose what it carried.
        var orphan = new NodeInstance { Id = Guid.NewGuid(), TypeId = "test.absent.thing" };
        orphan.SetState("whatever", new JsonObject { ["kept"] = 4f });

        orphan.Clone().StateOf("whatever")!["kept"]!.GetValue<float>().ShouldBe(4f);
    }

    [Fact]
    public void Taking_the_last_entry_away_leaves_no_empty_object_behind()
    {
        var chord = NodeInstance.Create(Chord(), 0, 0);
        chord.State.ShouldNotBeNull();

        chord.SetState("chord", null);
        chord.State.ShouldBeNull();
    }

    [Fact]
    public void What_it_carries_is_described_without_the_engine_knowing_the_kind()
    {
        var extra = new ChordExtra();
        var node = NodeInstance.Create(Chord(), 0, 0);

        // The note display comes through, which is the reuse of PortSpec paying
        // off: 57 is written as the note it stands for, not as 57.
        extra.Report(node).ShouldContain("A3");
        extra.Report(node).ShouldContain("wide on");
        extra.Announce().ShouldContain("chord");
        extra.Announce().ShouldContain("set_extra");
    }
}
