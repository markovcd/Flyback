using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// Defs, groups and loops written inside one another. A nested block builds the same
/// instrument as the text written out flat, and only the outermost group draws a box.
/// </summary>
public class NestedBlockTests
{
    private static LanguageLoad Try(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    private static Patch Built(string source)
    {
        var load = Try(source);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(CompiledPatch program) =>
        program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));

    private static void SameSound(string nested, string flat) =>
        Fingerprint(Built(nested).CompileForAudio(NodeCatalog.BuiltIn).Program)
            .ShouldBe(Fingerprint(Built(flat).CompileForAudio(NodeCatalog.BuiltIn).Program));

    private static string[] Types(Patch patch, NodeGroup group) =>
        [.. group.Members.Select(id => patch.Find(id)!.TypeId).Order()];

    [Fact]
    public void A_def_inside_a_def_is_stamped_out_at_every_call() =>
        SameSound(
            """
            def voice(freq) = {
              def shaped(in) = in |> drive(drive: 2) |> drive(drive: 3)
              sine(freq: freq) |> shaped()
            }
            voice(220) |> out.left
            voice(330) |> out.right
            """,
            """
            def shaped(in) = in |> drive(drive: 2) |> drive(drive: 3)
            def voice(freq) = sine(freq: freq) |> shaped()
            voice(220) |> out.left
            voice(330) |> out.right
            """);

    [Fact]
    public void A_def_inside_a_def_is_out_of_sight_outside_it()
    {
        var load = Try(
            """
            def voice(freq) = {
              def shaped(in) = in |> drive(drive: 2)
              sine(freq: freq) |> shaped()
            }
            voice(220) |> shaped() |> out.left
            """);

        load.Issues.ShouldHaveSingleItem(load.Report).Code.ShouldBe(IssueCode.UnknownModule);
    }

    [Fact]
    public void A_def_inside_a_def_reads_the_outer_parameters_and_the_defs_around_it() =>
        SameSound(
            """
            def warm(in) = in |> drive(drive: 2)
            def voice(freq) = {
              def tone(f) = sine(freq: f * freq) |> warm()
              tone(1) + tone(2)
            }
            voice(220) |> out.left
            """,
            """
            def warm(in) = in |> drive(drive: 2)
            def voice(freq) = {
              def tone(f, base) = sine(freq: f * base) |> warm()
              tone(1, freq) + tone(2, freq)
            }
            voice(220) |> out.left
            """);

    [Fact]
    public void A_def_at_the_top_reads_none_of_the_names_around_it() =>
        Try(
            """
            let base = 220
            def tone(f) = sine(freq: f * base)
            tone(1) |> out.left
            """).Issues.ShouldContain(issue => issue.Code == IssueCode.UnknownName);

    [Fact]
    public void A_def_inside_a_loop_reads_the_pass()
    {
        var patch = Built(
            """
            let v = for pitch in [110, 220] {
              def tone(f) = sine(freq: f * pitch)
              tone(1) + tone(2)
            }
            sum(v) |> out.left
            """);

        patch.Nodes.Where(n => n.TypeId == "osc.sine").Select(n => n.InputValues[1])
            .ShouldBe([110f, 220f, 220f, 440f], ignoreOrder: true);
    }

    [Fact]
    public void A_def_inside_a_group_reads_what_the_group_sees() =>
        Built(
            """
            let base = 110
            group "Voice" {
              let lfo = sine(freq: 0.2)
              def tone(f) = sine(freq: f * base, amp: lfo)
              let pair = tone(1) + tone(2)
            }
            pair |> out.left
            """);

    [Fact]
    public void A_def_inside_a_def_may_not_take_the_name_of_one_in_sight() =>
        Try(
            """
            def tone(f) = sine(freq: f)
            def voice(freq) = {
              def tone(f) = saw(freq: f)
              tone(freq)
            }
            voice(220) |> out.left
            """).Issues.ShouldContain(issue => issue.Code == IssueCode.DefTwice);

    [Fact]
    public void Two_defs_may_each_keep_a_helper_of_the_same_name()
    {
        var patch = Built(
            """
            def low(freq) = {
              def tone(f) = sine(freq: f)
              tone(freq)
            }
            def high(freq) = {
              def tone(f) = saw(freq: f)
              low(freq) + tone(freq * 2)
            }
            high(220) |> out.left
            """);

        patch.Nodes.Select(n => n.TypeId).ShouldContain("osc.sine");
        patch.Nodes.Select(n => n.TypeId).ShouldContain("osc.saw");
    }

    [Fact]
    public void A_def_reached_again_through_one_inside_it_still_calls_itself() =>
        Try(
            """
            def voice(freq) = {
              def again(f) = voice(f)
              again(freq)
            }
            voice(220) |> out.left
            """).Issues.ShouldContain(issue => issue.Code == IssueCode.DefCallsItself);

    [Fact]
    public void A_def_inside_a_loop_is_the_one_written_outside_it() =>
        SameSound(
            """
            let v = for k in 1..3 {
              def f(a) = sine(freq: a) |> drive(drive: 2)
              f(k)
            }
            sum(v) |> out.left
            """,
            """
            def f(a) = sine(freq: a) |> drive(drive: 2)
            let v = for k in 1..3 { f(k) }
            sum(v) |> out.left
            """);

    [Fact]
    public void A_named_group_in_loops_inside_loops_is_numbered_for_every_pass()
    {
        var patch = Built(
            """
            let v = for k in 1..2 {
              let w = for j in 1..2 {
                group "Cell" {
                  let s = sine(freq: k * j) |> drive(drive: 2)
                }
                s
              }
              sum(w)
            }
            sum(v) |> out.left
            """);

        patch.Groups.ShouldNotBeNull().Select(g => g.Name).ShouldBe(["Cell 1.1", "Cell 1.2", "Cell 2.1", "Cell 2.2"], ignoreOrder: true);
    }

    [Fact]
    public void A_group_inside_a_group_is_in_the_outer_box_and_its_names_stay_in_sight()
    {
        var patch = Built(
            """
            group "A" {
              let a = sine(freq: 2) |> drive(drive: 2)
              group "B" {
                let b = saw(freq: 3) |> drive(drive: 2)
              }
            }
            a + b |> out.left
            """);

        var group = patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();
        group.Name.ShouldBe("A");
        Types(patch, group).ShouldBe(["audio.drive", "audio.drive", "osc.saw", "osc.sine"]);
    }

    [Fact]
    public void A_def_written_in_a_group_is_in_sight_inside_it_and_nowhere_else()
    {
        Built(
            """
            group "A" {
              def tone(f) = sine(freq: f)
              group "B" {
                let low = tone(110) |> drive(drive: 2)
              }
              let high = tone(220) |> drive(drive: 3)
            }
            low + high |> out.left
            """);

        Try(
            """
            group "A" {
              def tone(f) = sine(freq: f)
              let low = tone(110) |> drive(drive: 2)
            }
            tone(220) + low |> out.left
            """).Issues.ShouldContain(issue => issue.Code == IssueCode.UnknownModule);
    }

    [Fact]
    public void A_group_inside_a_loop_draws_a_box_a_pass_and_a_named_one_is_numbered_for_its_pass()
    {
        var unnamed = Built(
            """
            let v = for k in 1..3 {
              group {
                let s = sine(freq: k) |> drive(drive: 2)
              }
              s
            }
            sum(v) |> out.left
            """);

        unnamed.Groups.ShouldNotBeNull().Count.ShouldBe(3);

        var named = Built(
            """
            let v = for k in 1..3 {
              group "Voices" {
                let s = sine(freq: k) |> drive(drive: 2)
              }
              s
            }
            sum(v) |> out.left
            """);

        named.Groups.ShouldNotBeNull().Select(g => g.Name).ShouldBe(["Voices 1", "Voices 2", "Voices 3"], ignoreOrder: true);
        named.Groups.ShouldAllBe(g => g.Members.Count == 2);
    }
}
