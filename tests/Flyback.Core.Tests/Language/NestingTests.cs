using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// Text nested or repeated past what the program can hold is refused with one
/// complaint. The reader and the binder recurse, and a stack overflow ends the
/// editor with no chance to save; a repeat builds its list, and a large enough one
/// runs out of memory before a word is said.
/// </summary>
public class NestingTests
{
    private static LanguageLoad Build(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    [Fact]
    public void Brackets_nested_past_the_limit_are_refused() =>
        SyntaxComplaintTests.Says(
            "sine(freq: " + new string('(', 20_000) + "1" + new string(')', 20_000) + ") |> out.left",
            1, 11 + Parser.MaxDepth, IssueCode.TooDeep, "nested more than");

    [Fact]
    public void A_long_run_of_minus_signs_is_refused() =>
        Build("sine(freq: " + new string('-', 100_000) + "1) |> out.left").Issues.ShouldHaveSingleItem().Code.ShouldBe(IssueCode.TooDeep);

    [Fact]
    public void A_sum_of_too_many_terms_is_refused() =>
        Build("sine(freq: " + string.Concat(Enumerable.Repeat("1+", 100_000)) + "1) |> out.left")
            .Issues.ShouldHaveSingleItem().Code.ShouldBe(IssueCode.TooDeep);

    [Fact]
    public void A_step_block_nested_past_the_limit_is_refused() =>
        Build("notes() [ " + new string('[', 20_000) + "C4" + new string(']', 20_000) + " ] |> out.left")
            .Issues.ShouldHaveSingleItem().Code.ShouldBe(IssueCode.TooDeep);

    /// <summary>As deep as is allowed, read on a thread with the smallest stack a Windows program starts with.</summary>
    [Fact]
    public void As_deep_as_is_allowed_reads_on_a_small_stack()
    {
        var nested = "sine(freq: " + new string('(', Parser.MaxDepth - 2) + "1" + new string(')', Parser.MaxDepth - 2) + ") |> out.left";

        // Two for the call and the pipe it stands in, so the whole tree is as tall as allowed.
        var summed = "sine(freq: " + string.Concat(Enumerable.Repeat("x+", Parser.MaxDepth - 3)) + "1) |> out.left";

        IReadOnlyList<LanguageIssue>? first = null, second = null;

        var thread = new Thread(() =>
        {
            first = Build(nested).Issues;
            second = Build(summed).Issues;
        }, maxStackSize: 1 << 20);

        thread.Start();
        thread.Join();

        first.ShouldBeEmpty();
        second.ShouldBeEmpty();
    }

    [Fact]
    public void A_repeat_past_any_sequence_is_refused_before_it_is_built() =>
        SyntaxComplaintTests.Says("notes() [ C4 D4 E4!99999999999 ] |> out.left", 1, 9, IssueCode.TooManySteps, "more steps than a sequence holds");

    [Fact]
    public void A_block_one_step_longer_than_a_sequence_says_how_long() =>
        SyntaxComplaintTests.Says("notes() [ C4 D4!32 ] |> out.left", 1, 9, IssueCode.TooManySteps, "33 steps");
}
