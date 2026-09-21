using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A knob on the patch's panel reaches a socket as a live value while it is being
/// played, and as where it rests everywhere else. These are about that seam: what
/// a program asks for, what it reads over a link's range, and what is left behind
/// when a knob or a link goes.
/// </summary>
public class ControlTests
{
    /// <summary>A Value module whose knob is heard in the left channel.</summary>
    private static (Patch Patch, NodeInstance Value) Built(float knob = 0.5f)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var value = builder.Add("value", 0, 0, (0, knob));
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));

        builder.Wire(value, 0, sink, NodeCatalog.OutputLeftPort);

        return (builder.Patch, value);
    }

    private static double Heard(CompiledPatch program, LiveValues? live = null)
    {
        var registers = program.AllocateRegisters();

        program.Evaluate(0d, 0d, 0d, registers, default, live: live ?? new LiveValues(program.LiveInputs));

        return registers[program.OutputBase];
    }

    [Fact]
    public void A_linked_socket_played_reads_its_knob_live()
    {
        var (patch, value) = Built();
        var knob = patch.AddControl(value: 0.25f);
        ControlMap.Link(value, 0, new ControlLink(knob.Id, 2f, 6f));

        var program = patch.CompileForAudio(NodeCatalog.BuiltIn, played: true).Program;
        var live = new LiveValues(program.LiveInputs);

        program.LiveInputs.ShouldBe([knob.Key]);

        live.Set(knob.Key, 0.75f);
        Heard(program, live).ShouldBe(5d, 1e-6);
    }

    [Fact]
    public void A_fresh_block_seeded_from_the_patch_reads_where_the_knob_rests()
    {
        var (patch, value) = Built();
        var knob = patch.AddControl(value: 0.25f);
        ControlMap.Link(value, 0, new ControlLink(knob.Id, 2f, 6f));

        var program = patch.CompileForAudio(NodeCatalog.BuiltIn, played: true).Program;
        var live = new LiveValues(program.LiveInputs);
        patch.Seed(live);

        Heard(program, live).ShouldBe(3d, 1e-6);
    }

    /// <summary>
    /// A file being rendered has nobody turning anything, and a renderer handed no
    /// block would otherwise read every linked socket at the bottom of its range.
    /// </summary>
    [Fact]
    public void Not_played_a_linked_socket_is_baked_in_where_its_knob_rests()
    {
        var (patch, value) = Built();
        var knob = patch.AddControl(value: 0.25f);
        ControlMap.Link(value, 0, new ControlLink(knob.Id, 2f, 6f));

        var program = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        program.LiveInputs.ShouldBeEmpty();
        Heard(program).ShouldBe(3d, 1e-6);
    }

    [Fact]
    public void A_range_upside_down_turns_the_knob_round()
    {
        var (patch, value) = Built();
        var knob = patch.AddControl();
        ControlMap.Link(value, 0, new ControlLink(knob.Id, 1f, 0f));

        var program = patch.CompileForAudio(NodeCatalog.BuiltIn, played: true).Program;
        var live = new LiveValues(program.LiveInputs);

        live.Set(knob.Key, 0.2f);
        Heard(program, live).ShouldBe(0.8d, 1e-6);
    }

    [Fact]
    public void One_knob_drives_several_sockets_through_one_live_input()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var add = builder.Add("math.add", 0, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));
        builder.Wire(add, 0, sink, NodeCatalog.OutputLeftPort);

        var knob = builder.Patch.AddControl();
        ControlMap.Link(add, 0, new ControlLink(knob.Id, 0f, 1f));
        ControlMap.Link(add, 1, new ControlLink(knob.Id, 0f, 10f));

        var program = builder.Patch.CompileForAudio(NodeCatalog.BuiltIn, played: true).Program;
        var live = new LiveValues(program.LiveInputs);

        program.LiveInputs.Count.ShouldBe(1);

        live.Set(knob.Key, 0.5f);
        Heard(program, live).ShouldBe(5.5d, 1e-6);
    }

    [Fact]
    public void A_wire_wins_over_a_link()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var source = builder.Add("value", 0, 0, (0, 7f));
        var add = builder.Add("math.add", 0, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));
        builder.Wire(source, 0, add, 0).Wire(add, 0, sink, NodeCatalog.OutputLeftPort);

        var knob = builder.Patch.AddControl();
        ControlMap.Link(add, 0, new ControlLink(knob.Id, 0f, 100f));

        var program = builder.Patch.CompileForAudio(NodeCatalog.BuiltIn, played: true).Program;

        program.LiveInputs.ShouldBeEmpty();
        Heard(program).ShouldBe(7d, 1e-6);
    }

    [Fact]
    public void A_link_to_a_knob_that_is_gone_rests_on_its_own_knob_and_says_so()
    {
        var (patch, value) = Built(knob: 0.4f);
        ControlMap.Link(value, 0, new ControlLink(Guid.NewGuid(), 0f, 100f));

        var result = patch.CompileForAudio(NodeCatalog.BuiltIn, played: true);

        result.Issues.ShouldContain(issue => issue.NodeId == value.Id && issue.Severity == IssueSeverity.Warning);
        Heard(result.Program).ShouldBe(0.4d, 1e-6);
    }

    [Fact]
    public void A_stepped_socket_is_handed_whole_numbers()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var note = builder.Add("audio.note", 0, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));
        builder.Wire(note, 1, sink, NodeCatalog.OutputLeftPort);

        NodeCatalog.BuiltIn.Get("audio.note")!.Inputs[0].Stepped.ShouldBeTrue();

        var knob = builder.Patch.AddControl(value: 0.5f);
        ControlMap.Link(note, 0, new ControlLink(knob.Id, 60f, 61f));

        var baked = builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var constants = baked.Ops.Where(op => op.Code == OpCode.Const).Select(op => (double)op.K).ToList();

        constants.ShouldContain(61d);
        constants.ShouldNotContain(60.5d);
    }

    [Fact]
    public void Removing_a_knob_leaves_every_socket_where_it_had_put_it()
    {
        var (patch, value) = Built();
        var knob = patch.AddControl(value: 0.5f);
        ControlMap.Link(value, 0, new ControlLink(knob.Id, 2f, 6f));

        patch.RemoveControl(knob.Id).ShouldBeTrue();

        patch.Controls.ShouldBeNull();
        ControlMap.Of(value, 0).ShouldBeNull();
        value.State.ShouldBeNull();
        value.InputValues[0].ShouldBe(4f);
    }

    [Fact]
    public void Knobs_and_links_survive_a_file()
    {
        var (patch, value) = Built();
        var knob = patch.AddControl("Cutoff", 0.3f);
        knob.Midi = new MidiBinding("midi:minilab", 0, 21);
        ControlMap.Link(value, 0, new ControlLink(knob.Id, 0.1f, 8f));

        var read = PatchIO.Read(PatchIO.ToJson(patch, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn).Patch;

        var back = read.Controls.ShouldHaveSingleItem();
        back.Id.ShouldBe(knob.Id);
        back.Name.ShouldBe("Cutoff");
        back.Value.ShouldBe(0.3f);
        back.Midi.ShouldBe(new MidiBinding("midi:minilab", 0, 21));

        ControlMap.Of(read.Find(value.Id)!, 0).ShouldBe(new ControlLink(knob.Id, 0.1f, 8f));
    }

    [Fact]
    public void A_patch_with_no_knobs_writes_nothing_about_them()
    {
        var (patch, _) = Built();

        PatchIO.ToJson(patch, NodeCatalog.BuiltIn).ShouldNotContain("Controls");
    }

    [Theory]
    [InlineData(0, 2, "Knob 2,Knob 3,Knob 1")]
    [InlineData(2, 0, "Knob 3,Knob 1,Knob 2")]
    [InlineData(1, 9, "Knob 1,Knob 3,Knob 2")]
    public void A_knob_moves_to_any_place_on_the_panel(int from, int to, string order)
    {
        var (patch, _) = Built();
        for (var i = 0; i < 3; i++) patch.AddControl();

        patch.MoveControl(patch.Controls![from].Id, to).ShouldBeTrue();

        string.Join(",", patch.Controls.Select(c => c.Name)).ShouldBe(order);
    }

    [Fact]
    public void New_knobs_are_numbered_past_the_ones_already_there()
    {
        var (patch, _) = Built();

        patch.AddControl().Name.ShouldBe("Knob 1");
        patch.AddControl().Name.ShouldBe("Knob 2");
    }

    [Theory]
    [InlineData(0, 1, 21, true)]
    [InlineData(0, 16, 21, true)]
    [InlineData(2, 2, 21, true)]
    [InlineData(2, 3, 21, false)]
    [InlineData(0, 1, 22, false)]
    public void A_binding_hears_its_controller(int bound, int channel, int controller, bool hears)
    {
        new MidiBinding("midi:a", bound, 21).Hears("midi:a", channel, controller).ShouldBe(hears);
    }
}
