using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// A mistake in the text is said once, as what it is, where it is: a number that is
/// not one, a character that only looks like one the language has, a bracket nothing
/// opened, and what came where something else was wanted.
/// </summary>
public class SyntaxComplaintTests
{
    private static LanguageLoad Build(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    /// <summary>The one complaint the text gets, at the place given, saying the words given.</summary>
    internal static void Says(string source, int line, int column, string code, string words)
    {
        var load = Build(source);

        var issue = load.Issues.ShouldHaveSingleItem(load.Report);
        issue.Code.ShouldBe(code, load.Report);
        (issue.Line, issue.Column).ShouldBe((line, column), load.Report);
        issue.Message.ShouldContain(words, Case.Sensitive, load.Report);
    }

    // --- numbers -------------------------------------------------------------

    [Theory]
    [InlineData("sine(freq: 1e3) |> out.left", "'1e3' is not a number")]
    [InlineData("sine(freq: 0x10) |> out.left", "'0x10' is not a number")]
    [InlineData("sine(freq: 1.5.5) |> out.left", "'1.5.5' is not a number")]
    [InlineData("sine(freq: 5.) |> out.left", "'5.' is not a number")]
    [InlineData("sine(freq: .5) |> out.left", "'.5' is not a number")]
    [InlineData("sine(freq: 220Hz) |> out.left", "'220Hz' is not a number")]
    public void Digits_that_run_on_into_letters_or_a_second_point_are_said_whole(string source, string words) =>
        Says(source, 1, 12, IssueCode.MalformedNumber, words);

    [Fact]
    public void A_range_straight_after_a_number_is_still_a_range() =>
        Build("remap(0..1, 0..10) |> out.left").Issues.ShouldBeEmpty();

    [Fact]
    public void A_note_past_any_named_octave_is_said_to_be_one() =>
        Says("note(note: C99999999999) |> out.left", 1, 12, IssueCode.MalformedNumber, "too far from middle C");

    // --- characters that only look right -------------------------------------

    [Fact]
    public void A_typographic_minus_is_named_and_read_as_a_minus() =>
        Says("sine(freq: −220) |> out.left", 1, 12, IssueCode.LookalikeCharacter, "Write '-'");

    [Fact]
    public void Curly_quotes_are_named_and_the_text_read() =>
        Says("sine() |> out.left\ndescription “A tone”", 2, 13, IssueCode.LookalikeCharacter, "straight");

    [Theory]
    [InlineData("sine(freq: 220) → out.left")]
    [InlineData("sine(freq: 220) -> out.left")]
    [InlineData("sine(freq: 220) | out.left")]
    public void An_arrow_or_a_bar_for_a_pipe_is_named_and_read_as_one(string source) =>
        Says(source, 1, 17, IssueCode.NotAPipe, "'|>'");

    [Fact]
    public void A_semicolon_is_said_to_be_needless() =>
        Says("sine(freq: 220) |> out.left;", 1, 28, IssueCode.Semicolon, "ends with its line");

    [Fact]
    public void A_space_pasted_from_a_document_is_a_space() =>
        Build("sine(freq: 220) |> out.left").Issues.ShouldBeEmpty();

    [Fact]
    public void A_stray_character_is_one_complaint_and_not_the_rest_of_its_line_too() =>
        Says("sine(freq: 220) @ out.left", 1, 17, IssueCode.StrayCharacter, "'@'");

    // --- brackets ------------------------------------------------------------

    [Theory]
    [InlineData("sine(freq: 220)) |> out.left", 1, 16, "')' closes nothing")]
    [InlineData("sine(freq: 220) |> out.left\n)", 2, 1, "')' closes nothing")]
    [InlineData("sine(freq: 220) |> out.left }", 1, 29, "'}' closes nothing")]
    [InlineData("sine() ] |> out.left", 1, 8, "']' closes nothing")]
    public void A_closer_nothing_opened_is_said_to_close_nothing(string source, int line, int column, string words) =>
        Says(source, line, column, IssueCode.UnmatchedCloser, words);

    [Theory]
    [InlineData("sine(freq: ) |> out.left", 1, 12)]
    [InlineData("sine(freq: 1..) |> out.left", 1, 15)]
    public void A_closer_that_does_close_something_is_what_was_found(string source, int line, int column) =>
        Says(source, line, column, IssueCode.Syntax, "found ')'");

    [Theory]
    [InlineData("sine(freq: 220\n |> out.left", 2, 13)]
    [InlineData("(sine(freq: 220) |> out.left", 1, 29)]
    public void An_unclosed_bracket_says_where_it_was_opened(string source, int line, int column) =>
        Says(source, line, column, IssueCode.Syntax, "to close the '(' on line 1");

    [Fact]
    public void An_argument_missing_its_colon_is_named() =>
        Says("sine(freq 220) |> out.left", 1, 11, IssueCode.Syntax, "':' after 'freq'");

    [Fact]
    public void Arguments_missing_their_comma_are_named() =>
        Says("sine(freq: 220 amp: 1) |> out.left", 1, 16, IssueCode.Syntax, "',' between");

    // --- what came instead ---------------------------------------------------

    [Theory]
    [InlineData("sine(freq: 220,, amp: 1) |> out.left", 1, 16, "found ','")]
    [InlineData("let x =", 1, 8, "the end of the text")]
    public void A_value_missing_says_what_came_instead(string source, int line, int column, string words) =>
        Says(source, line, column, IssueCode.Syntax, words);

    [Fact]
    public void A_pipe_with_nothing_before_it_says_so() =>
        Says("|> out.left", 1, 1, IssueCode.Syntax, "nothing before it");

    [Fact]
    public void What_a_statement_left_unread_is_named() =>
        Says("sine(freq: 220) |> out.left, out.right", 1, 28, IssueCode.UnreadTail, "','");

    [Fact]
    public void Tags_with_commas_between_them_say_how_tags_are_written() =>
        Says("tags \"a\", \"b\"", 1, 9, IssueCode.Syntax, "without commas");

    // --- names ---------------------------------------------------------------

    [Fact]
    public void A_dot_with_no_socket_after_it_is_named() =>
        Says("sine() |> out.left\nout. = 3", 2, 6, IssueCode.Syntax, "after 'out.'");

    [Fact]
    public void A_plugin_name_ending_in_a_dot_is_named() =>
        Says("requires flyback.", 1, 18, IssueCode.Syntax, "after 'flyback.'");

    [Fact]
    public void A_plugin_name_with_two_dots_is_named() =>
        Says("requires flyback..picture", 1, 17, IssueCode.Syntax, "one '.'");

    [Theory]
    [InlineData("requires")]
    [InlineData("keyboard")]
    [InlineData("off")]
    [InlineData("description")]
    [InlineData("tags")]
    [InlineData("author")]
    public void A_statement_word_on_its_own_says_how_its_statement_is_written(string word) =>
        Says($"sine() |> out.left\n{word}", 2, 1, IssueCode.UnknownName, $"'{word}' starts a statement");

    [Theory]
    [InlineData("let let = 3")]
    [InlineData("let group = 3")]
    [InlineData("let def = 3")]
    public void A_word_of_the_language_cannot_be_bound(string source) =>
        Says(source, 1, 1, IssueCode.ReservedName, "word of the language");

    [Fact]
    public void A_name_read_above_its_let_says_where_it_is_bound() =>
        Says("let a = b\nlet b = 3\na |> out.left", 1, 9, IssueCode.UsedBeforeBound, "line 2");

    // --- arguments -----------------------------------------------------------

    [Fact]
    public void Text_where_a_number_goes_names_the_socket_at_the_text() =>
        Says("sine(freq: \"220\") |> out.left", 1, 12, IssueCode.NotASignal, "'freq'");

    [Theory]
    [InlineData("sine(freq: C#4) |> out.left")]
    [InlineData("sine(freq: 20ms) |> out.left")]
    public void A_literal_a_socket_does_not_read_is_pointed_at(string source) =>
        Says(source, 1, 12, IssueCode.WrongLiteral, "'freq'");

    // --- lines that carry on -------------------------------------------------

    [Fact]
    public void A_groups_brace_may_stand_on_a_line_of_its_own() =>
        Build("group \"A\"\n{\n  let s = sine(freq: 220)\n  let d = s |> drive(drive: 2)\n}\nd |> out.left").Issues.ShouldBeEmpty();

    [Fact]
    public void An_output_may_be_taken_on_the_next_line() =>
        Build("sine(freq: 220)\n  .out |> out.left").Issues.ShouldBeEmpty();
}
