using Flyback.Core.Language;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// A mistake inside a step block is pointed at where it stands in the file, and a
/// modifier or a bracket left unfinished is said rather than read as a default.
/// </summary>
public class StepBlockComplaintTests
{
    [Fact]
    public void A_complaint_points_into_the_line_rather_than_into_the_block() =>
        SyntaxComplaintTests.Says("notes() [ C4 D4 ( E4 ] |> out.left", 1, 17, IssueCode.StepSyntax, "'('");

    [Fact]
    public void A_complaint_on_a_later_line_of_the_block_is_on_that_line() =>
        SyntaxComplaintTests.Says("notes() [\n  C4 H4\n] |> out.left", 2, 6, IssueCode.UnknownNote, "'H4'");

    [Fact]
    public void A_euclidean_pattern_left_open_is_said() =>
        SyntaxComplaintTests.Says("notes() [ C4(3,8 ] |> out.left", 1, 13, IssueCode.StepSyntax, "never closed");

    [Fact]
    public void An_alternation_left_open_is_said() =>
        SyntaxComplaintTests.Says("notes() [ <C4 D4 ] |> out.left", 1, 11, IssueCode.StepSyntax, "never closed with '>'");

    [Fact]
    public void A_weight_with_no_number_is_said() =>
        SyntaxComplaintTests.Says("notes() [ C4@ ] |> out.left", 1, 13, IssueCode.StepSyntax, "expected a length");

    [Fact]
    public void A_weight_of_nothing_is_said() =>
        SyntaxComplaintTests.Says("notes() [ C4@0 ] |> out.left", 1, 13, IssueCode.StepSyntax, "above nought");

    [Fact]
    public void A_repeat_of_nothing_is_said() =>
        SyntaxComplaintTests.Says("notes() [ C4!0 ] |> out.left", 1, 13, IssueCode.StepSyntax, "once or more");

    [Fact]
    public void A_scale_complaint_points_at_the_word()
    {
        SyntaxComplaintTests.Says("keyboard scale [C x]\nsine() |> out.left", 1, 19, IssueCode.UnknownNote, "'x'");
    }
}
