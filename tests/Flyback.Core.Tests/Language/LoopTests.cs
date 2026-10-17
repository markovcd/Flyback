using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// Lists, <c>for</c>, <c>...</c> and <c>sum</c> (ADR-0208): a loop is unrolled as the text is
/// built, so a looped patch is the same instrument as its copies written out.
/// </summary>
public class LoopTests
{
    private static LanguageLoad Try(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    private static Patch Built(string source)
    {
        var load = Try(source);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static LanguageIssue Refused(string source)
    {
        var load = Try(source);

        return load.Issues.ShouldHaveSingleItem(load.Report);
    }

    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(CompiledPatch program) =>
        program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));

    private static void SameInstrument(Patch actual, Patch expected)
    {
        Fingerprint(actual.CompileForVideo(NodeCatalog.BuiltIn).Program)
            .ShouldBe(Fingerprint(expected.CompileForVideo(NodeCatalog.BuiltIn).Program), "the picture");

        Fingerprint(actual.CompileForAudio(NodeCatalog.BuiltIn).Program)
            .ShouldBe(Fingerprint(expected.CompileForAudio(NodeCatalog.BuiltIn).Program), "the sound");
    }

    private static float[] Frequencies(Patch patch) =>
        [.. patch.Nodes.Where(n => n.TypeId == "osc.sine").Select(n => n.InputValues[1])];

    [Fact]
    public void Four_voices_written_as_a_loop_is_the_preset() =>
        SameInstrument(Built("""
            def voice(pitch, bands, hue, rate, phase) = {
              let level = sine(freq: rate, phase: phase, amp: 0.5, bias: 0.5)
              let tone  = sine(freq: note(pitch))
              let tint  = radius |> sine(freq: bands, amp: 0.5, bias: 0.5)
                                 |> hsv(value: _, hue: hue)
              (tone, level, tint)
            }

            let voices = for (pitch, bands, hue, rate, phase) in [
              (A2,  1, 0.00, 0.06, 0.00),
              (E3,  2, 0.33, 0.09, 0.30),
              (A3,  3, 0.58, 0.13, 0.60),
              (C#4, 4, 0.83, 0.17, 0.85),
            ] {
              voice(pitch, bands, hue, rate, phase)
            }

            mixer(...for (tone, level, tint) in voices { (tone, level) }) |> out.left

            mixer(...for (tone, level, tint) in voices { (tint, level) })
              |> gain(color: _, gain: 0.6)
              |> out.color

            out.volume = 0.25
            """), Presets.All.Single(p => p.Name == "Four voices").Build(NodeCatalog.BuiltIn));

    [Fact]
    public void A_sum_adds_a_list_up_as_the_plus_signs_would() =>
        SameInstrument(
            Built("sum([sine(freq: 1), sine(freq: 2), sine(freq: 3), sine(freq: 4), sine(freq: 5), sine(freq: 6)]) |> out.left"),
            Built("sine(freq: 1) + sine(freq: 2) + sine(freq: 3) + sine(freq: 4) + sine(freq: 5) + sine(freq: 6) |> out.left"));

    [Fact]
    public void A_list_piped_into_sum_is_added_up() =>
        SameInstrument(
            Built("for k in 1..3 { sine(freq: k) } |> sum() |> out.left"),
            Built("sine(freq: 1) + sine(freq: 2) + sine(freq: 3) |> out.left"));

    [Fact]
    public void A_sum_of_numbers_is_a_number() =>
        Built("sine(freq: sum([100, 10, 1])) |> out.left").Nodes
            .Single(n => n.TypeId == "osc.sine").InputValues[1].ShouldBe(111f);

    [Fact]
    public void A_count_takes_in_both_ends() =>
        Frequencies(Built("sum(for k in 2..4 { sine(freq: k * 100) }) |> out.left")).ShouldBe([200f, 300f, 400f]);

    [Fact]
    public void A_pass_binds_its_own_names_and_reads_the_ones_outside()
    {
        var patch = Built("""
            let base = 110
            let partials = for k in 1..3 {
              let tone = sine(freq: base * k, amp: 0.2)
              tone
            }
            sum(partials) |> out.left
            """);

        Frequencies(patch).ShouldBe([110f, 220f, 330f]);
        patch.Nodes.Count(n => n.Name == "tone").ShouldBe(3);
    }

    [Fact]
    public void A_name_bound_inside_a_pass_is_not_in_sight_after_the_loop() =>
        Try("""
            let tones = for k in 1..2 {
              let tone = sine(freq: k)
              tone
            }
            tone |> out.left
            """).Issues.ShouldContain(issue => issue.Code == IssueCode.UnknownName);

    [Fact]
    public void A_loop_with_nothing_at_the_end_places_its_modules_all_the_same()
    {
        var patch = Built("""
            let echo = delay(time: 0.25)
            for k in 1..2 {
              let ping = sine(freq: k * 440)
            }
            echo |> out.left
            """);

        Frequencies(patch).ShouldBe([440f, 880f]);
    }

    [Fact]
    public void A_text_in_a_list_names_the_file_a_module_plays()
    {
        var patch = Built("""
            sum(for file in ["kick.wav", "snare.wav"] { sample(file) }) |> out.left
            """);

        patch.Nodes.Where(n => n.TypeId == NodeCatalog.SampleTypeId).Select(SampleExtra.Of)
            .ShouldBe(["kick.wav", "snare.wav"]);
    }

    [Fact]
    public void A_text_passed_to_a_def_names_the_file_a_module_plays() =>
        SampleExtra.Of(Built("""
            def hit(file) = sample(file)
            hit("kick.wav") |> out.left
            """).Nodes.Single(n => n.TypeId == NodeCatalog.SampleTypeId)).ShouldBe("kick.wav");

    [Fact]
    public void A_rebuild_gives_every_pass_the_same_modules_and_each_pass_its_own()
    {
        const string source = "sum(for k in 1..3 { sine(freq: k) }) |> out.left";

        var once = Built(source).Nodes.Select(n => n.Id).ToList();
        var again = Built(source).Nodes.Select(n => n.Id).ToList();

        again.ShouldBe(once);
        once.ShouldBeUnique();
    }

    [Fact]
    public void A_mistake_inside_the_body_is_said_once_rather_than_once_a_pass() =>
        Refused("for k in 1..8 { sine(nope: k) }").Code.ShouldBe(IssueCode.UnknownSocket);

    [Theory]
    [InlineData("for k in 3 { sine(freq: k) }", IssueCode.NotAList)]
    [InlineData("for k in 1.5..3 { sine(freq: k) }", IssueCode.BadCount)]
    [InlineData("for k in 4..1 { sine(freq: k) }", IssueCode.BadCount)]
    [InlineData("for k in 1..100000 { sine(freq: k) }", IssueCode.TooManyPasses)]
    [InlineData("for (a, b) in [1, 2] { sine(freq: a) }", IssueCode.TupleMismatch)]
    [InlineData("for k in 1..2 {\n  panel level = 0.5\n}", IssueCode.NotInLoop)]
    [InlineData("for k in 1..2 {\n  def f(a) = a\n}", IssueCode.NotInLoop)]
    [InlineData("for t in 1..2 { sine(freq: t) }", IssueCode.ReservedName)]
    [InlineData("mixer(...sine()) |> out.left", IssueCode.NotAList)]
    [InlineData("sum(sine()) |> out.left", IssueCode.NotAList)]
    [InlineData("sum([]) |> out.left", IssueCode.NotAList)]
    [InlineData("def f(a, b) = a * b\nf(...[1, 2]) |> out.left", IssueCode.DefArity)]
    [InlineData("mixer(...[1, 2, 3, 4, 5, 6, 7, 8, 9]) |> out.left", IssueCode.TooManyArguments)]
    [InlineData("let for = 1", IssueCode.ReservedName)]
    public void A_misused_loop_is_known_by_its_code(string source, string code) =>
        Try(source).Issues.ShouldContain(issue => issue.Code == code, Try(source).Report);

    [Fact]
    public void The_loops_of_one_patch_stamp_out_no_more_than_the_limit_between_them()
    {
        var half = Binder.MostPasses / 2 + 1;

        Try($$"""
            let a = for k in 1..{{half}} { k }
            let b = for k in 1..{{half}} { k }
            """).Issues.ShouldContain(issue => issue.Code == IssueCode.TooManyPasses);
    }

    [Fact]
    public void A_list_item_that_is_not_closed_by_a_comma_says_so() =>
        Try("sum([sine() sine()]) |> out.left").Report.ShouldContain("','");

    [Fact]
    public void A_block_after_a_call_is_still_the_modules_block() =>
        StepsExtra.Of(Built("notes() [ A3 C4 ] |> note(note: _) |> sine(freq: _) |> out.left").Nodes
            .Single(n => n.TypeId == "seq.notes")).Count.ShouldBe(2);
}
