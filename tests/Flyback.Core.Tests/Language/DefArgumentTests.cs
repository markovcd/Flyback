using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// What a def's parameters get at a call: named arguments claim theirs, the pipe
/// lands on <c>in</c> or else the first parameter left, the rest go in order,
/// and a default fills what nothing gave.
/// </summary>
public class DefArgumentTests
{
    private const int Freq = 1;
    private const int Amp = 3;

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
    public void A_named_argument_goes_to_the_parameter_it_names()
    {
        var patch = Built("""
            def tone(pitch, level) = sine(freq: pitch) * level
            tone(level: 0.5, pitch: 330) |> out.left
            """);

        Only(patch, "osc.sine").InputValues[Freq].ShouldBe(330f);
    }

    [Fact]
    public void Positional_arguments_fill_what_the_named_ones_left()
    {
        var patch = Built("""
            def tone(level, pitch) = sine(freq: pitch) * level
            tone(330, level: 0.5) |> out.left
            """);

        Only(patch, "osc.sine").InputValues[Freq].ShouldBe(330f);
    }

    [Fact]
    public void A_name_the_def_does_not_have_is_refused() =>
        Refused("""
            def tone(pitch) = sine(freq: pitch)
            tone(pich: 330) |> out.left
            """).Code.ShouldBe(IssueCode.UnknownParameter);

    [Fact]
    public void A_parameter_named_twice_is_refused() =>
        Refused("""
            def tone(pitch) = sine(freq: pitch)
            tone(pitch: 330, pitch: 220) |> out.left
            """).Code.ShouldBe(IssueCode.GivenTwice);

    [Fact]
    public void A_parameter_left_out_takes_its_default()
    {
        var patch = Built("""
            def tone(pitch = 330) = sine(freq: pitch)
            tone() |> out.left
            """);

        Only(patch, "osc.sine").InputValues[Freq].ShouldBe(330f);
    }

    [Fact]
    public void A_default_may_be_a_note()
    {
        var patch = Built("""
            def key(root = A4) = note(note: root)
            key() |> sine() |> out.left
            """);

        Only(patch, "audio.note").InputValues[0].ShouldBe(69f);
    }

    [Fact]
    public void A_default_may_be_literal_arithmetic()
    {
        var patch = Built("""
            def tone(pitch = 440 / 2) = sine(freq: pitch)
            tone() |> out.left
            """);

        Only(patch, "osc.sine").InputValues[Freq].ShouldBe(220f);
    }

    [Fact]
    public void An_argument_given_overrides_the_default()
    {
        var patch = Built("""
            def tone(level, pitch = 330) = sine(freq: pitch) * level
            tone(0.5, pitch: 220) |> out.left
            """);

        Only(patch, "osc.sine").InputValues[Freq].ShouldBe(220f);
    }

    [Fact]
    public void A_parameter_with_no_default_left_out_is_refused_by_name()
    {
        var issue = Refused("""
            def tone(pitch, level = 0.5) = sine(freq: pitch) * level
            tone(level: 1) |> out.left
            """);

        issue.Code.ShouldBe(IssueCode.DefArity);
        issue.Message.ShouldContain("'pitch'");
    }

    [Fact]
    public void Too_many_arguments_are_refused() =>
        Refused("""
            def tone(pitch) = sine(freq: pitch)
            tone(330, 220) |> out.left
            """).Code.ShouldBe(IssueCode.DefArity);

    [Fact]
    public void A_parameter_with_no_default_after_one_with_a_default_is_refused() =>
        Refused("def tone(level = 0.5, pitch) = sine(freq: pitch) * level")
            .Code.ShouldBe(IssueCode.DefaultBeforeRequired);

    [Theory]
    [InlineData("sine()")]
    [InlineData("x")]
    [InlineData("t * 2")]
    public void A_default_that_would_be_a_signal_is_refused(string fallback) =>
        Refused($"def tone(pitch = {fallback}) = sine(freq: pitch)").Code.ShouldBe(IssueCode.DefaultNotAValue);

    [Fact]
    public void A_pipe_lands_on_the_parameter_called_in()
    {
        var patch = Built("""
            def tone(level, in) = sine(freq: in) * level
            value(220) |> tone(0.5) |> out.left
            """);

        patch.IncomingTo(Only(patch, "osc.sine").Id, Freq).ShouldNotBeNull().SourceNode.ShouldBe(Only(patch, "value").Id);
    }

    [Fact]
    public void A_pipe_lands_on_the_first_parameter_no_argument_names()
    {
        var patch = Built("""
            def tone(level, pitch) = sine(freq: pitch) * level
            value(220) |> tone(level: 0.5) |> out.left
            """);

        patch.IncomingTo(Only(patch, "osc.sine").Id, Freq).ShouldNotBeNull().SourceNode.ShouldBe(Only(patch, "value").Id);
    }

    [Fact]
    public void A_pipe_lands_on_the_parameter_named_as_the_placeholder()
    {
        var patch = Built("""
            def tone(level, pitch) = sine(freq: pitch) * level
            value(220) |> tone(pitch: _, level: 0.5) |> out.left
            """);

        patch.IncomingTo(Only(patch, "osc.sine").Id, Freq).ShouldNotBeNull().SourceNode.ShouldBe(Only(patch, "value").Id);
    }

    [Fact]
    public void A_pipe_into_a_def_whose_in_is_already_given_is_refused() =>
        Refused("""
            def tone(in, level) = sine(freq: in) * level
            value(220) |> tone(in: 330, level: 0.5) |> out.left
            """).Code.ShouldBe(IssueCode.PipeLandsNowhere);

    [Fact]
    public void A_pipe_into_a_def_with_every_parameter_given_is_refused() =>
        Refused("""
            def tone(pitch) = sine(freq: pitch)
            value(220) |> tone(pitch: 330) |> out.left
            """).Code.ShouldBe(IssueCode.NoSocketFree);

    [Fact]
    public void A_pipe_with_every_socket_given_is_told_to_write_an_underscore_for_one() =>
        Refused("""
            rings() |> color.hsv(hue: 0.6, saturation: 1, value: 0.1) |> out.color
            """).Message.ShouldContain("'color.hsv(value: _)'");

    [Fact]
    public void A_knob_statement_in_a_def_body_sets_the_knob()
    {
        var patch = Built("""
            def tone(hz) = {
              let s = sine(freq: hz)
              s.amp = 0.25
              s
            }
            tone(110) |> out.left
            """);

        Only(patch, "osc.sine").InputValues[Amp].ShouldBe(0.25f);
    }

    [Fact]
    public void A_back_wire_in_a_def_body_is_wired()
    {
        var with = Built("""
            def acc(v) = {
              let sum = v |> add(a: _)
              sum.b <- sum * 0.9
              sum
            }
            acc(square(freq: 110)) |> out.left
            """);

        var without = Built("""
            def acc(v) = {
              let sum = v |> add(a: _)
              sum
            }
            acc(square(freq: 110)) |> out.left
            """);

        with.Connections.Count.ShouldBeGreaterThan(without.Connections.Count);
    }
}
