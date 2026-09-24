using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// <c>panel name = 0.5</c>: a knob on the patch's panel, and the sockets that
/// follow it by naming it where a number would go.
/// </summary>
public class PanelTests
{
    private static LanguageLoad Build(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    private static Patch Built(string source)
    {
        var load = Build(source);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static LanguageIssue Refused(string source)
    {
        var load = Build(source);

        return load.Issues.ShouldHaveSingleItem(load.Report);
    }

    private static NodeInstance Only(Patch patch, string typeId) => patch.Nodes.Single(n => n.TypeId == typeId);

    private static string Changed(string source, Change? change) =>
        change is { } edit ? source[..edit.Offset] + edit.Text + source[(edit.Offset + edit.Length)..] : source;

    /// <summary>Where a knob rests is written back into its own line, in text and in a printing alike.</summary>
    [Fact]
    public void Where_a_knob_rests_is_written_where_its_line_says_it()
    {
        const string source = "panel level = 0.5, cc: 7, device: \"midi:test\"\nsine(amp: level) |> out.left\n";

        var load = Build(source);
        var id = load.Patch.Controls.ShouldNotBeNull().Single().Id;

        Changed(source, load.Map.Knob(id, PatchPrinter.PanelKnob, "0.8"))
            .ShouldStartWith("panel level = 0.8, cc: 7,");

        var printing = PatchPrinter.Written(load.Patch, NodeCatalog.BuiltIn);

        Changed(printing.Source, printing.Map.Knob(id, PatchPrinter.PanelKnob, "0.8"))
            .ShouldStartWith("panel level = 0.8, cc: 7,");
    }

    /// <summary>
    /// The panel lines are rewritten where they stand: what is between two of them
    /// stays, one taken away takes its line break, and one added goes after the last.
    /// </summary>
    [Theory]
    [InlineData(new[] { "panel b = 0.2", "panel a = 0.1" }, "panel b = 0.2\nlet w = sine()\npanel a = 0.1\nw |> out.left\n")]
    [InlineData(new[] { "panel a = 0.1" }, "panel a = 0.1\nlet w = sine()\nw |> out.left\n")]
    [InlineData(new[] { "panel a = 0.1", "panel b = 0.2", "panel c = 0.3" }, "panel a = 0.1\nlet w = sine()\npanel b = 0.2\npanel c = 0.3\nw |> out.left\n")]
    [InlineData(new string[0], "let w = sine()\nw |> out.left\n")]
    public void The_panel_lines_are_rewritten_where_they_stand(string[] lines, string expected)
    {
        const string source = "panel a = 0.1\nlet w = sine()\npanel b = 0.2\nw |> out.left\n";

        Changed(source, Build(source).Map.Panel(lines)).ShouldBe(expected);
    }

    [Fact]
    public void A_panel_line_for_a_text_with_none_goes_under_its_requires()
    {
        const string source = "requires flyback.picture\n\nsine() |> out.left\n";

        Changed(source, Build(source).Map.Panel(["panel a = 0.1"]))
            .ShouldBe("requires flyback.picture\n\npanel a = 0.1\n\nsine() |> out.left\n");
    }

    [Fact]
    public void A_panel_knob_is_put_on_the_panel_with_what_it_follows()
    {
        var patch = Built("panel cutoff = 0.4, label: \"Filter cutoff\", cc: 21, channel: 2, device: \"midi:test\"");

        var knob = patch.Controls.ShouldNotBeNull().ShouldHaveSingleItem();

        knob.Name.ShouldBe("Filter cutoff");
        knob.Value.ShouldBe(0.4f);
        knob.Midi.ShouldBe(new MidiBinding("midi:test", 2, 21));
    }

    [Fact]
    public void A_socket_naming_a_knob_follows_it_over_its_own_range()
    {
        var patch = Built("panel speed = 0.5\nsine(freq: speed) |> out.left");

        var sine = Only(patch, "osc.sine");
        var spec = NodeCatalog.BuiltIn.Require("osc.sine").Inputs[1];
        var link = ControlMap.Of(sine, 1).ShouldNotBeNull();

        link.Control.ShouldBe(patch.Controls![0].Id);
        link.Min.ShouldBe(Math.Min(spec.Min, spec.Default));
        link.Max.ShouldBe(Math.Max(spec.Max, spec.Default));
        link.Knee.ShouldBe(spec.Knee);
    }

    [Fact]
    public void A_socket_can_read_a_knob_over_a_range_and_a_knee_of_its_own()
    {
        var patch = Built("panel speed = 0.5\nsine(freq: speed(100..800, knee: 20)) |> out.left");

        var link = ControlMap.Of(Only(patch, "osc.sine"), 1).ShouldNotBeNull();

        (link.Min, link.Max, link.Knee).ShouldBe((100f, 800f, 20f));
    }

    [Fact]
    public void A_range_is_read_the_way_the_socket_reads_a_number()
    {
        var patch = Built("panel snap = 0.5\npulse() |> adsr(gate: _, decay: snap(20ms..400ms)) |> out.left");

        var link = ControlMap.Of(Only(patch, "env.adsr"), 2).ShouldNotBeNull();

        link.Min.ShouldBe((float)Math.Log10(0.02), 1e-6f);
        link.Max.ShouldBe((float)Math.Log10(0.4), 1e-6f);
    }

    [Fact]
    public void The_Output_follows_a_knob_by_a_statement()
    {
        var patch = Built("panel level = 0.8\nsine() |> out.left\nout.volume = level");

        ControlMap.Of(patch.Output, NodeCatalog.OutputVolumePort).ShouldNotBeNull();
    }

    [Fact]
    public void A_knob_is_not_a_signal()
    {
        Refused("panel speed = 0.5\nspeed |> sine() |> out.left").Code.ShouldBe(IssueCode.PanelNotASignal);
    }

    [Fact]
    public void A_knob_rests_between_nought_and_one()
    {
        Refused("panel speed = 2").Code.ShouldBe(IssueCode.OutOfRange);
    }

    [Fact]
    public void A_controller_comes_with_its_device()
    {
        Refused("panel speed = 0.5, cc: 21").Message.ShouldContain("add 'device");
    }

    [Fact]
    public void A_knob_has_only_the_settings_it_has()
    {
        Refused("panel speed = 0.5, colour: \"red\"").Code.ShouldBe(IssueCode.UnknownSetting);
    }

    [Fact]
    public void A_knob_is_not_named_like_a_module()
    {
        Refused("panel drive = 0.5").Message.ShouldContain("'drive_knob'");
    }

    [Fact]
    public void A_knob_is_a_name_bound_once()
    {
        Refused("panel speed = 0.5\nlet speed = 3").Code.ShouldBe(IssueCode.BoundTwice);
    }

    [Fact]
    public void A_def_is_handed_a_knob_rather_than_declaring_one()
    {
        var load = Build(
            """
            def voice(pitch) = {
              panel level = 0.5
              sine(freq: pitch, amp: level)
            }
            voice(220) |> out.left
            """);

        load.Issues.ShouldContain(issue => issue.Code == IssueCode.PanelInDef, load.Report);
    }

    [Fact]
    public void A_knob_passed_into_a_def_is_followed_there()
    {
        var patch = Built(
            """
            panel level = 0.5
            def voice(pitch, loud) = sine(freq: pitch, amp: loud)
            voice(220, level) |> out.left
            """);

        ControlMap.Of(Only(patch, "osc.sine"), 3).ShouldNotBeNull();
    }

    [Fact]
    public void A_socket_set_and_linked_is_set_twice()
    {
        Refused("panel speed = 0.5\nlet s = sine(freq: 2)\ns.freq = speed\ns |> out.left").Code.ShouldBe(IssueCode.KnobSetTwice);
    }

    [Fact]
    public void A_panel_is_written_out_and_read_back_whole()
    {
        var patch = Built(
            """
            panel cutoff = 0.4, label: "Filter cutoff", cc: 21, device: "midi:test"
            panel level = 0.8
            saw(freq: 110) |> filter(cutoff: cutoff(200..4000), resonance: 0.3) |> out.left
            out.volume = level
            """);

        var printed = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);
        var again = Built(printed);

        again.Controls.ShouldNotBeNull().Select(c => (c.Name, c.Value, c.Midi))
            .ShouldBe(patch.Controls!.Select(c => (c.Name, c.Value, c.Midi)));

        ControlMap.Of(Only(again, "audio.filter"), 1).ShouldNotBeNull().Max.ShouldBe(4000f);
        ControlMap.Of(again.Output, NodeCatalog.OutputVolumePort).ShouldNotBeNull();
    }
}
