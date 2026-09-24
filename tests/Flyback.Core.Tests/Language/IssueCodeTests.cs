using System.Reflection;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>Every complaint about the text carries a code a program can match on.</summary>
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
    [InlineData("// the clock\nsine() |> out.left", IssueCode.SlashComment)]
    [InlineData("let = 3", IssueCode.Syntax)]
    [InlineData("notes() [ H3 ] |> out.left", IssueCode.UnknownNote)]
    [InlineData("group \"K\" {\n  panel level = 0.5\n}", IssueCode.PanelInGroup)]
    [InlineData("group \"K\" {\n  requires flyback.picture\n}", IssueCode.RequiresInGroup)]
    [InlineData("group \"A\" {\n  group \"B\" {\n    let s = sine()\n  }\n}", IssueCode.GroupInGroup)]
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

    /// <summary>
    /// A statement refused is the one complaint: the text is refused whole, and
    /// every line reading what it would have bound says nothing more.
    /// </summary>
    [Theory]
    [InlineData("let base = sinee(freq: 3)\nbase |> out.left\nbase.freq = 2\noff base\nbase * 2 |> out.right")]
    [InlineData("panel rate = 2\nsine(freq: rate(0..4), amp: rate) |> out.left")]
    [InlineData("let (a, b) = sine()\na |> out.left\nb |> out.right")]
    [InlineData("group \"K\" {\n  panel level = 0.5\n}\nsine(amp: level) |> out.left")]
    [InlineData("group \"A\" {\n  group \"B\" {\n    let s = sine()\n  }\n}\ns |> out.left")]
    [InlineData("// --- CLOCK & TIMING / 2 ---\nsine() |> out.left")]
    [InlineData("sine() |> out.left // the tone & its level")]
    public void A_refused_statement_is_the_one_complaint(string source)
    {
        var load = Build(source);

        load.Ok.ShouldBeFalse();
        load.Issues.ShouldHaveSingleItem(load.Report).Line.ShouldBeLessThanOrEqualTo(2);
    }

    /// <summary>A heading written the way other languages write one is told the comment sign, and that a group is where headings go.</summary>
    [Fact]
    public void A_slash_comment_is_told_the_comment_sign_and_where_a_heading_goes()
    {
        var issue = Build("// --- CLOCK & TIMING ---\nlet tempo = tempo(bpm: 100)").Issues.ShouldHaveSingleItem();

        issue.Line.ShouldBe(1);
        issue.Message.ShouldContain("a comment starts with '#'");
        issue.Message.ShouldContain("group");
    }
}
