using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// A name means one thing and a socket takes one wire. Text that says otherwise
/// is refused where it says it, because what it would build reads correctly and
/// means something else.
/// </summary>
public class BoundOnceTests
{
    private static LanguageLoad Build(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    private static LanguageIssue Refused(string source)
    {
        var load = Build(source);

        return load.Issues.ShouldHaveSingleItem(load.Report);
    }

    [Fact]
    public void A_name_bound_twice_points_at_the_first()
    {
        var issue = Refused(
            """
            let a = x |> sine(freq: 2)
            let a = y |> sine(freq: 3)
            a |> color.hsv(hue: _) |> out.color
            """);

        issue.Line.ShouldBe(2);
        issue.Message.ShouldContain("already bound on line 1");
    }

    [Theory]
    [InlineData("t", "the clock")]
    [InlineData("x", "coordinates")]
    [InlineData("aspect", "coordinates")]
    [InlineData("out", "the Output")]
    public void A_word_every_patch_has_cannot_be_bound(string word, string what)
    {
        Refused($"let {word} = 5").Message.ShouldContain(what);
    }

    [Fact]
    public void A_def_parameter_cannot_be_a_word_every_patch_has()
    {
        Refused("def f(x) = x |> sine()").Message.ShouldContain("coordinates");
    }

    [Fact]
    public void A_def_parameter_is_named_once()
    {
        Refused("def f(a, a) = a").Message.ShouldContain("two parameters called 'a'");
    }

    [Fact]
    public void A_name_taken_apart_twice_is_refused()
    {
        var issue = Refused(
            """
            def f(a) = { (a, a) }
            let (p, p) = f(x)
            """);

        issue.Message.ShouldContain("'p' is written twice");
    }

    [Fact]
    public void A_group_does_not_shadow_the_names_around_it()
    {
        var issue = Refused(
            """
            let a = x |> sine()
            group "Inner" {
              let a = y |> sine()
            }
            """);

        issue.Line.ShouldBe(3);
        issue.Message.ShouldContain("already bound on line 1");
    }

    [Fact]
    public void Two_groups_do_not_share_a_name()
    {
        Refused(
            """
            group "One" {
              let a = y |> sine()
              let b = a |> sine()
            }
            group "Two" {
              let a = x |> sine()
              let c = a |> sine()
            }
            """).Line.ShouldBe(6);
    }

    [Fact]
    public void A_def_body_is_stamped_out_more_than_once_under_its_own_names()
    {
        var load = Build(
            """
            def voice(pitch) = {
              let tone = sine(freq: pitch)
              tone
            }
            let low = voice(110)
            let high = voice(220)
            low + high |> out.left
            """);

        load.Issues.ShouldBeEmpty(load.Report);
    }

    [Fact]
    public void The_Output_wired_twice_points_at_the_first()
    {
        var issue = Refused(
            """
            x |> color.hsv(hue: _) |> out.color
            y |> color.hsv(hue: _) |> out.color
            """);

        issue.Line.ShouldBe(2);
        issue.Message.ShouldContain("'out.color' is already wired on line 1");
    }

    [Fact]
    public void A_socket_wired_twice_by_arrows_points_at_the_first()
    {
        var issue = Refused(
            """
            let a = x |> sine()
            a.freq <- y |> add(a: _, b: 1)
            a.freq <- y |> add(a: _, b: 2)
            a |> color.hsv(hue: _) |> out.color
            """);

        issue.Line.ShouldBe(3);
        issue.Message.ShouldContain("'a.freq' is already wired on line 2");
    }

    [Fact]
    public void A_knob_set_twice_points_at_the_first()
    {
        var issue = Refused(
            """
            let a = sine(freq: 2)
            a.freq = 3
            a |> out.left
            """);

        issue.Line.ShouldBe(2);
        issue.Code.ShouldBe(IssueCode.KnobSetTwice);
        issue.Message.ShouldContain("'a.freq' is already set on line 1");
    }

    [Fact]
    public void A_knob_turned_by_two_statements_is_refused()
    {
        Refused(
            """
            out.volume = 0.5
            out.volume = 0.6
            """).Message.ShouldContain("'out.volume' is already set on line 1");
    }

    [Fact]
    public void A_socket_a_call_wired_takes_no_second_wire()
    {
        Refused(
            """
            let a = sine(freq: y)
            a.freq <- x
            a |> color.hsv(hue: _) |> out.color
            """).Message.ShouldContain("'a.freq' is already wired on line 1");
    }
}
