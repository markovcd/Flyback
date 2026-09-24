using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// Where a pipe lands: <c>socket: _</c>, else <c>in</c> or a module's only
/// socket, else a leading <c>x</c> and <c>y</c>, else a module's one color
/// socket for a color, else nowhere. And a pipeline is never an argument.
/// </summary>
public class PlaceholderTests
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

    [Fact]
    public void A_pipe_lands_on_the_socket_written_as_a_placeholder()
    {
        var patch = Built("pulse(freq: 2) |> adsr(gate: _, decay: 240ms) |> out.left");

        var adsr = Only(patch, "env.adsr");

        patch.IncomingTo(adsr.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(Only(patch, "osc.pulse").Id);
    }

    [Fact]
    public void A_placeholder_wins_over_in()
    {
        var patch = Built("let rate = t * 0.1\nrate |> sine(freq: _) |> out.left");

        var sine = Only(patch, "osc.sine");

        patch.IncomingTo(sine.Id, 1).ShouldNotBeNull();
        patch.IncomingTo(sine.Id, 0).ShouldBeNull();
    }

    [Fact]
    public void A_module_with_no_in_and_no_placeholder_is_told_what_to_write()
    {
        var issue = Refused("pulse(freq: 2) |> adsr(decay: 240ms) |> out.left");

        issue.Message.ShouldContain("'adsr(gate: _)'");
    }

    [Fact]
    public void The_suggestion_skips_the_sockets_the_call_named()
    {
        Refused("rings() |> color.hsv(hue: 0.3) |> out.color").Message.ShouldContain("'color.hsv(saturation: _)'");
    }

    [Fact]
    public void One_signal_into_a_position_says_which_half()
    {
        Refused("sine(freq: 2) |> checker(size: 3) |> out.color").Message.ShouldContain("'checker(x: _)'");
    }

    [Fact]
    public void A_position_is_still_carried_whole()
    {
        var patch = Built("rotate(angle: t) |> checker(size: 3) |> out.color");

        var checker = Only(patch, "pattern.checker");

        patch.IncomingTo(checker.Id, 0).ShouldNotBeNull().SourcePort.ShouldBe(0);
        patch.IncomingTo(checker.Id, 1).ShouldNotBeNull().SourcePort.ShouldBe(1);
    }

    [Fact]
    public void A_module_with_one_socket_needs_no_placeholder()
    {
        var patch = Built("rings(freq: 3) |> color.hsv(hue: _) |> split |> out.left");

        var split = Only(patch, "color.split");

        patch.IncomingTo(split.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(Only(patch, "color.hsv").Id);
    }

    [Fact]
    public void A_module_with_one_socket_is_printed_without_one()
    {
        var built = Built("rings(freq: 3) |> color.hsv(hue: _) |> color.split() |> out.left");

        PatchPrinter.Print(built, NodeCatalog.BuiltIn).ShouldContain("|> split()");
    }

    [Fact]
    public void A_color_lands_on_the_one_color_socket_a_module_has()
    {
        var patch = Built("rings() |> color.hsv(hue: _) |> gain(gain: 0.5) |> out.color");

        var gain = Only(patch, "color.gain");

        patch.IncomingTo(gain.Id, 0).ShouldNotBeNull().SourceNode.ShouldBe(Only(patch, "color.hsv").Id);
    }

    [Fact]
    public void A_signal_that_is_not_a_color_still_says_where()
    {
        Refused("rings() |> gain(gain: 0.5) |> out.color").Message.ShouldContain("'gain(color: _)'");
    }

    [Fact]
    public void A_color_into_a_module_with_two_color_sockets_still_says_which()
    {
        Refused("rings() |> color.hsv(hue: _) |> color.mix(t: 0.5) |> out.color").Message.ShouldContain("'color.mix(a: _)'");
    }

    [Fact]
    public void A_color_into_its_one_socket_is_printed_without_a_placeholder()
    {
        var built = Built("rings() |> color.hsv(hue: _) |> gain(gain: 0.5) |> out.color");

        PatchPrinter.Print(built, NodeCatalog.BuiltIn).ShouldContain("|> gain(gain: 0.5)");
    }

    [Fact]
    public void Naming_x_leaves_no_pair_to_land()
    {
        Refused("rotate(angle: t) |> checker(x: 0.5) |> out.color").Message.ShouldContain("'checker(y: _)'");
    }

    /// <summary>
    /// A position lands on a module's own first two sockets, never on the first
    /// two a call happened to leave, so the arguments cannot move it.
    /// </summary>
    [Fact]
    public void A_pair_that_is_not_the_modules_first_takes_no_position()
    {
        var shifted = new NodeDef(
            "test.shifted", "Shifted", "Test",
            [new PortSpec("a"), new PortSpec("x"), new PortSpec("y")],
            [new PortSpec("out")],
            (em, i) => [em.Mul(i[1], 1f)]);

        var catalog = NodeCatalog.BuiltIn.With(new ModuleProvider("test", "Test"), [shifted]).Catalog;
        var load = PatchLanguage.Build("rotate(angle: t) |> test.shifted(a: 1) |> out.left", catalog);

        load.Issues.ShouldHaveSingleItem(load.Report).Message.ShouldContain("'test.shifted(x: _)'");
    }

    [Fact]
    public void A_placeholder_with_nothing_piped_in_is_refused()
    {
        Refused("adsr(gate: _) |> out.left").Message.ShouldContain("nothing is");
    }

    [Fact]
    public void A_pipe_brings_one_signal_so_one_placeholder()
    {
        Refused("t |> math.mix(a: _, b: _) |> out.left").Message.ShouldContain("written twice");
    }

    [Fact]
    public void A_placeholder_says_its_socket_by_name()
    {
        Build("t |> add(_, 1) |> out.left").Report.ShouldContain("'socket: _'");
    }

    [Fact]
    public void A_placeholder_is_not_a_name_to_read_or_bind()
    {
        Build("_ |> out.left").Report.ShouldContain("as a call's argument");
        Refused("let _ = 5").Message.ShouldContain("what a pipe brings in");
    }

    [Fact]
    public void A_placeholder_in_a_def_call_stands_for_its_parameter()
    {
        var patch = Built(
            """
            def tone(level, pitch) = sine(freq: pitch) * level
            let pitch = value(220)
            pitch |> tone(0.5, _) |> out.left
            """);

        var sine = Only(patch, "osc.sine");

        patch.IncomingTo(sine.Id, 1).ShouldNotBeNull().SourceNode.ShouldBe(Only(patch, "value").Id);
    }

    [Fact]
    public void A_pipeline_inside_an_argument_is_refused_where_it_starts()
    {
        var issue = Refused(
            """
            let steps = notes() [ A3 C4 ]
            saw(freq: steps |> note(note: _)) |> out.left
            """);

        issue.Message.ShouldContain("Bind it with 'let'");
        issue.Line.ShouldBe(2);
        issue.Column.ShouldBe(11);
    }

    [Fact]
    public void A_bracketed_pipeline_inside_arithmetic_in_an_argument_is_refused_too()
    {
        Refused("saw(freq: (t |> fract()) * 200) |> out.left").Message.ShouldContain("inside an argument");
    }

    [Fact]
    public void Arithmetic_in_an_argument_stays()
    {
        Built("saw(freq: t * 20 + 110) |> out.left");
    }

    [Fact]
    public void A_refused_argument_still_takes_its_socket()
    {
        // Were 'hue' taken to be free, the suggestion would name it.
        var issues = Build("rings() |> color.hsv(hue: t |> fract(), saturation: 1) |> out.color").Issues;

        issues.ShouldContain(i => i.Message.Contains("inside an argument"));
        issues.ShouldContain(i => i.Message.Contains("'color.hsv(value: _)'"));
    }

    /// <summary>
    /// Sequence as the C# builds it, with no names: its Note feeds a sine's
    /// <c>freq</c>, which a printing can only say through a binding.
    /// </summary>
    [Fact]
    public void A_printing_writes_the_placeholder_and_lifts_what_would_be_an_argument()
    {
        var patch = Presets.All.Single(p => p.Name == "Sequence").Build(NodeCatalog.BuiltIn);

        var printed = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

        printed.ShouldContain("note(note: _)");
        printed.ShouldContain("let note = ");
        Build(printed).Issues.ShouldBeEmpty(printed);
    }
}
