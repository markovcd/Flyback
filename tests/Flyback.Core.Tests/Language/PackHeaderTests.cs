using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// The lines that open a module pack: <c>pack</c> with its id and version, and
/// <c>language</c> with the version of the text it was written for.
/// </summary>
public class PackHeaderTests
{
    private static LanguageLoad Try(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    private static LanguageIssue Refused(string source)
    {
        var load = Try(source);

        return load.Issues.ShouldHaveSingleItem(load.Report);
    }

    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(CompiledPatch program) =>
        program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));

    [Fact]
    public void A_pack_says_its_id_its_version_and_its_text()
    {
        var load = Try("""
            pack ada.echoes 1.2.0
            language 1

            sine() |> out.left
            """);

        load.Issues.ShouldBeEmpty(load.Report);
        load.Pack.ShouldBe(new PackHeader("ada.echoes", "1.2.0", 1));
    }

    [Fact]
    public void A_pack_with_no_language_line_was_written_for_this_text() =>
        Try("pack ada.echoes 0.1.0").Pack.ShouldBe(new PackHeader("ada.echoes", "0.1.0", PatchLanguage.Version));

    [Fact]
    public void A_patch_is_no_pack() =>
        Try("sine() |> out.left").Pack.ShouldBeNull();

    [Fact]
    public void A_pack_builds_the_same_patch_as_the_text_without_its_header()
    {
        const string body = "sine(freq: 220) |> out.left";

        var packed = Try("pack ada.echoes 1.0.0\nlanguage 1\n" + body).Patch;
        var plain = Try(body).Patch;

        Fingerprint(packed.CompileForAudio(NodeCatalog.BuiltIn).Program)
            .ShouldBe(Fingerprint(plain.CompileForAudio(NodeCatalog.BuiltIn).Program));
    }

    [Fact]
    public void Language_may_come_first() =>
        Try("language 1\npack ada.echoes 1.0.0").Pack!.Id.ShouldBe("ada.echoes");

    [Theory]
    [InlineData("echoes")]
    [InlineData("Ada.echoes")]
    [InlineData("ada.echoes_2")]
    public void A_pack_id_is_two_or_more_lower_case_words_joined_by_points(string id) =>
        Refused($"pack {id} 1.0.0").Code.ShouldBe(IssueCode.BadPackId);

    [Fact]
    public void A_pack_without_a_version_is_refused() =>
        Refused("pack ada.echoes").Code.ShouldBe(IssueCode.Syntax);

    [Fact]
    public void A_version_with_two_parts_is_a_number_not_a_version() =>
        Refused("pack ada.echoes 1.0").Message.ShouldContain("version after its id");

    [Fact]
    public void A_version_anywhere_else_is_a_malformed_number_that_says_where_it_goes() =>
        Try("sine(freq: 1.0.0) |> out.left").Issues.ShouldContain(i => i.Code == IssueCode.MalformedNumber && i.Message.Contains("pack's header"));

    [Fact]
    public void A_version_running_on_into_a_letter_is_a_malformed_number() =>
        Try("pack ada.echoes 1.0.0b").Issues.ShouldContain(i => i.Code == IssueCode.MalformedNumber);

    [Fact]
    public void A_header_after_another_line_is_refused()
    {
        var issue = Refused("sine() |> out.left\npack ada.echoes 1.0.0");

        issue.Code.ShouldBe(IssueCode.HeaderNotFirst);
        issue.Line.ShouldBe(2);
    }

    [Fact]
    public void A_header_in_a_group_is_refused() =>
        Refused("group \"G\" {\n  pack ada.echoes 1.0.0\n  sine() |> out.left\n}").Code.ShouldBe(IssueCode.HeaderNotFirst);

    [Fact]
    public void A_pack_named_twice_is_refused() =>
        Refused("pack ada.echoes 1.0.0\npack ada.other 1.0.0").Code.ShouldBe(IssueCode.SaidTwice);

    [Theory]
    [InlineData("language 0", IssueCode.BadLanguage)]
    [InlineData("language 1.5", IssueCode.BadLanguage)]
    [InlineData("language 1s", IssueCode.BadLanguage)]
    [InlineData("language 2", IssueCode.NewerLanguage)]
    public void A_language_line_names_a_version_this_build_reads(string source, string code) =>
        Refused(source).Code.ShouldBe(code);

    [Fact]
    public void Pack_and_language_are_still_names_where_no_header_follows() =>
        Try("let pack = 0.5\nlet language = pack * 2\nsine(amp: language) |> out.left").Issues.ShouldBeEmpty();
}
