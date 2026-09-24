using System.Reflection;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// Every complaint about the text carries a code a program can match on, and
/// the ones with exactly one repair carry it too.
/// </summary>
public class IssueCodeTests
{
    private static readonly string[] Codes = [.. typeof(IssueCode)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (string)field.GetValue(null)!)];

    private static LanguageLoad Build(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    [Fact]
    public void Every_code_is_one_lower_case_word_or_several_joined_by_hyphens()
    {
        Codes.ShouldAllBe(code => System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z]+(-[a-z]+)*$"));
        Codes.ShouldBeUnique();
    }

    [Theory]
    [InlineData("let a = 1\nlet a = 2", IssueCode.BoundTwice)]
    [InlineData("let t = 1", IssueCode.ReservedName)]
    [InlineData("kaleidoscop() |> out.color", IssueCode.UnknownModule)]
    [InlineData("mix() |> out.color", IssueCode.AmbiguousModule)]
    [InlineData("pulse() |> adsr(decay: 1ms) |> out.left", IssueCode.PipeLandsNowhere)]
    [InlineData("saw(freq: t |> fract()) |> out.left", IssueCode.PipelineInArgument)]
    [InlineData("adsr(attack: 0.01) |> out.left", IssueCode.BareDuration)]
    [InlineData("sine(nope: 1) |> out.left", IssueCode.UnknownSocket)]
    [InlineData("x |> out.color\ny |> out.color", IssueCode.WiredTwice)]
    [InlineData("\"never closed", IssueCode.UnclosedText)]
    [InlineData("let = 3", IssueCode.Syntax)]
    [InlineData("notes() [ H3 ] |> out.left", IssueCode.UnknownNote)]
    public void A_mistake_is_known_by_its_code(string source, string code)
    {
        var load = Build(source);

        load.Issues.ShouldContain(issue => issue.Code == code, load.Report);
    }

    [Fact]
    public void Every_complaint_has_one_of_the_codes()
    {
        string[] sources =
        [
            "let a = 1\nlet a = 2\nlet t = 3\nkaleidoscop() |> out.color",
            "pulse() |> adsr(decay: 1ms) |> hsv() |> nope(1) |> out.color",
            "def f(x, x) = x\nf(1, 2, 3) |> out.left\nkeyboard piano\nkeyboard piano",
            "notes() [ H3 (3) ] |> out.left\n\"open",
        ];

        foreach (var source in sources)
            Build(source).Issues.ShouldAllBe(issue => Codes.Contains(issue.Code));
    }

    /// <summary>A misspelling has one repair, and making it is what a repair loop does.</summary>
    [Fact]
    public void A_misspelled_module_is_repaired_by_its_fix()
    {
        const string source = "rotate() |> kaleidoscop(segments: 6) |> clouds() |> color.hsv(hue: _) |> out.color";

        var fixedSource = LanguageFix.Apply(source, Build(source).Issues);

        fixedSource.ShouldBe("rotate() |> kaleidoscope(segments: 6) |> clouds() |> color.hsv(hue: _) |> out.color");
        Build(fixedSource).Issues.ShouldBeEmpty();
    }

    /// <summary>
    /// Where the writer has to choose, there is no fix to apply blindly: which
    /// socket a pipe meant, or whether a bare number was seconds or decades.
    /// </summary>
    [Theory]
    [InlineData("pulse() |> adsr(decay: 1ms) |> out.left")]
    [InlineData("adsr(attack: 0.01) |> out.left")]
    [InlineData("mix() |> out.color")]
    public void A_mistake_with_a_choice_in_it_has_no_fix(string source)
    {
        Build(source).Issues.ShouldAllBe(issue => issue.Fix == null);
    }
}
