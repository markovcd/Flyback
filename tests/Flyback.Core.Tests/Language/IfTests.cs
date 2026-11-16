using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// <c>if</c> and <c>else</c> (ADR-0234): a branch is chosen as the text is built, so a patch
/// with an <c>if</c> is the same instrument as the branch it took written out.
/// </summary>
public class IfTests
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

    private static void SameInstrument(Patch actual, Patch expected)
    {
        Fingerprint(actual.CompileForVideo(NodeCatalog.BuiltIn).Program)
            .ShouldBe(Fingerprint(expected.CompileForVideo(NodeCatalog.BuiltIn).Program), "the picture");

        Fingerprint(actual.CompileForAudio(NodeCatalog.BuiltIn).Program)
            .ShouldBe(Fingerprint(expected.CompileForAudio(NodeCatalog.BuiltIn).Program), "the sound");
    }

    private static int Count(Patch patch, string typeId) => patch.Nodes.Count(n => n.TypeId == typeId);

    private static float Frequency(Patch patch) => patch.Nodes.Single(n => n.TypeId == "osc.sine").InputValues[1];

    [Fact]
    public void A_def_option_builds_only_the_branch_it_picks() =>
        SameInstrument(Built("""
            def voice(pitch, dark = 0) = {
              let tone = saw(freq: note(pitch))
              if dark { tone |> filter(cutoff: 800) } else { tone }
            }

            voice(A2, dark: 1) + voice(E3) |> out.left
            """), Built("(saw(freq: note(A2)) |> filter(cutoff: 800)) + saw(freq: note(E3)) |> out.left"));

    [Fact]
    public void The_language_guides_example_builds() =>
        Count(Built("""
            def voice(pitch, dark = 0) = {
              let tone = saw(freq: note(pitch), amp: 0.3)
              if dark { tone |> filter(cutoff: 600) } else { tone }
            }

            let voices = for pitch in [A2, C3, E3, A3] {
              if pitch < E3 { voice(pitch) } else { voice(pitch, dark: 1) }
            }

            sum(voices) |> out.left
            """), "osc.saw").ShouldBe(4);

    [Fact]
    public void A_loop_pass_can_differ_from_the_others()
    {
        var patch = Built("""
            for k in 1..8 {
              if k % 4 == 0 { sine(freq: k) } else { saw(freq: k) }
            }
            """);

        Count(patch, "osc.sine").ShouldBe(2);
        Count(patch, "osc.saw").ShouldBe(6);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(2, 200)]
    [InlineData(3, 300)]
    public void Else_if_tries_each_condition_in_turn(int pick, float frequency) =>
        Frequency(Built($$"""
            let pick = {{pick}}
            let f = if pick == 1 { 100 }
            else if pick == 2 { 200 }
            else { 300 }
            sine(freq: f) |> out.left
            """)).ShouldBe(frequency);

    [Theory]
    [InlineData("1 < 2 and 2 < 3", true)]
    [InlineData("1 < 2 and 3 < 2", false)]
    [InlineData("1 > 2 or 2 >= 2", true)]
    [InlineData("not 1 > 2", true)]
    [InlineData("not (1 < 2 or 1 > 2)", false)]
    [InlineData("1 != 1", false)]
    [InlineData("2 <= 1", false)]
    [InlineData("0", false)]
    [InlineData("0.5", true)]
    [InlineData("A2 < C4", true)]
    [InlineData("20ms < 1s", true)]
    [InlineData("\"kick.wav\" == \"kick.wav\"", true)]
    [InlineData("\"kick.wav\" != \"kick.wav\"", false)]
    [InlineData("(1 + 2) * 2 == 6", true)]
    public void A_condition_reads_numbers_notes_durations_and_texts(string condition, bool holds) =>
        Count(Built($"if {condition} {{ sine() |> out.left }} else {{ saw() |> out.left }}"), "osc.sine")
            .ShouldBe(holds ? 1 : 0);

    [Fact]
    public void An_if_with_nothing_taken_places_nothing() =>
        Built("if 1 > 2 { sine() |> out.left }").Nodes.Count.ShouldBe(Built("").Nodes.Count);

    [Fact]
    public void Names_bound_in_a_branch_stay_in_it() =>
        Try("""
            if 1 { let inside = sine() }
            inside |> out.left
            """).Issues.ShouldContain(issue => issue.Code == IssueCode.UnknownName);

    [Fact]
    public void A_rebuild_keeps_the_modules_a_branch_placed()
    {
        const string source = """
            for k in 1..3 {
              if k == 2 { sine(freq: k) } else { saw(freq: k) }
            }
            """;

        var once = Built(source).Nodes.Select(n => n.Id).ToList();

        Built(source).Nodes.Select(n => n.Id).ShouldBe(once);
        once.ShouldBeUnique();
    }

    [Fact]
    public void Turning_a_knob_never_changes_which_branch_is_built()
    {
        const string source = "let v = 1\nif v > 0 { sine(amp: v) |> out.left }";

        var load = Try(source);
        var sine = load.Patch.Nodes.Single(n => n.TypeId == "osc.sine");
        var edited = load.Map.Knob(sine.Id, "amp", "0") is { } change
            ? source.Remove(change.Offset, change.Length).Insert(change.Offset, change.Text)
            : source;

        Count(Built(edited), "osc.sine").ShouldBe(1, edited);
    }

    [Theory]
    [InlineData("if sine() > 0 { saw() }", IssueCode.ConditionNotAValue)]
    [InlineData("let s = sine()\nif s { saw() }", IssueCode.ConditionNotAValue)]
    [InlineData("panel level = 0.5\nif level > 0.5 { saw() }", IssueCode.ConditionNotAValue)]
    [InlineData("if [1, 2] { saw() }", IssueCode.ConditionNotAValue)]
    [InlineData("if \"a\" { saw() }", IssueCode.ConditionNotAValue)]
    [InlineData("if \"a\" < \"b\" { saw() }", IssueCode.ConditionNotAValue)]
    [InlineData("if 1 == \"a\" { saw() }", IssueCode.ConditionNotAValue)]
    [InlineData("let big = 3 > 2", IssueCode.ComparisonOutsideIf)]
    [InlineData("sine(freq: (3 > 2)) |> out.left", IssueCode.ComparisonOutsideIf)]
    [InlineData("if 1 {\n  panel level = 0.5\n}", IssueCode.NotInIf)]
    [InlineData("if 1 {\n  length 4s\n}", IssueCode.NotInIf)]
    [InlineData("let if = 1", IssueCode.ReservedName)]
    [InlineData("let and = 1", IssueCode.ReservedName)]
    [InlineData("if 1 sine()", IssueCode.Syntax)]
    [InlineData("if 1 < 2 < 3 { saw() }", IssueCode.Syntax)]
    [InlineData("if 1 { saw() } else saw()", IssueCode.Syntax)]
    public void A_misused_if_is_known_by_its_code(string source, string code) =>
        Try(source).Issues.ShouldContain(issue => issue.Code == code, Try(source).Report);

    [Fact]
    public void A_signal_in_a_condition_points_at_step_and_mix() =>
        Try("if sine() > 0 { saw() }").Report.ShouldContain("mix(a, b, step(");
}
