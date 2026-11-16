using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// <c>fold</c>: a loop whose passes each take what the pass before ended with, unrolled as the
/// text is built, so a folded patch is the same instrument as its chain written out.
/// </summary>
public class FoldTests
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

    private static float Frequency(string source) =>
        Built(source).Nodes.Single(n => n.TypeId == "osc.sine").InputValues[1];

    [Fact]
    public void A_fold_of_numbers_is_the_number_its_last_pass_ends_with() =>
        Frequency("sine(freq: fold 1..3 from 1 with n { n * 2 }) |> out.left").ShouldBe(8f);

    [Fact]
    public void A_fold_names_its_items_when_it_wants_them() =>
        Frequency("sine(freq: fold v in [3, 4, 100] from 0 with total { total + v }) |> out.left").ShouldBe(107f);

    [Fact]
    public void A_fold_carries_a_tuple_and_comes_apart_as_one() =>
        Frequency("""
            let (a, b) = fold 1..3 from (1, 1) with (a, b) { (b, a + b) }
            sine(freq: a * 10 + b) |> out.left
            """).ShouldBe(35f);

    [Fact]
    public void A_fold_of_signals_is_the_chain_written_out() =>
        SameInstrument(Built("""
            fold k in 1..3 from sine(freq: 100) with sound {
              sound * 0.5 + sine(freq: k * 200)
            } |> out.left
            """), Built("""
            let first = sine(freq: 100) * 0.5 + sine(freq: 200)
            let second = first * 0.5 + sine(freq: 400)
            second * 0.5 + sine(freq: 600) |> out.left
            """));

    [Fact]
    public void A_fold_inside_a_def_counts_as_far_as_an_argument_says() =>
        Frequency("""
            def doubled(times) = fold 1..times from 1 with n { n * 2 }
            sine(freq: doubled(5)) |> out.left
            """).ShouldBe(32f);

    [Fact]
    public void Fold_is_still_a_name_where_no_fold_follows() =>
        Frequency("""
            let fold = 7
            sine(freq: fold) |> out.left
            """).ShouldBe(7f);

    [Fact]
    public void A_fold_whose_passes_end_with_nothing_is_refused() =>
        Refused("""
            let n = fold 1..3 from 1 with m { let k = m }
            sine(freq: 1) |> out.left
            """).Message.ShouldContain("ends with nothing");

    [Fact]
    public void A_fold_whose_pass_ends_with_the_wrong_number_of_things_is_refused() =>
        Refused("""
            let (a, b) = fold 1..3 from (1, 1) with (a, b) { a }
            sine(freq: a) |> out.left
            """).Code.ShouldBe(IssueCode.TupleMismatch);

    [Fact]
    public void A_fold_that_names_its_item_and_its_carry_alike_is_refused() =>
        Refused("""
            sine(freq: fold k in 1..3 from 1 with k { k * 2 }) |> out.left
            """).Code.ShouldBe(IssueCode.BoundTwice);

    [Fact]
    public void A_fold_is_a_level_headed_by_what_it_carries()
    {
        var load = Try("sine(freq: 1) * fold 1..2 from sine(freq: 3) with v { v * sine(freq: 5) } |> out.left");

        load.Issues.ShouldBeEmpty(load.Report);
        load.Levels.ShouldContain(level => level.Kind == LevelKind.Loop && level.Name == "fold v");
    }
}
