using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// <c>module</c>: a def a pack offers by id, with the <c>///</c> lines above it as its card.
/// </summary>
public class PackModuleTests
{
    private const string Header = "pack ada.echoes 1.0.0\n";

    private static LanguageLoad Try(string source) => PatchLanguage.Build(source, NodeCatalog.BuiltIn);

    private static LanguageLoad Built(string source)
    {
        var load = Try(source);

        load.Issues.ShouldBeEmpty(load.Report);

        return load;
    }

    private static LanguageIssue Refused(string source)
    {
        var load = Try(source);

        return load.Issues.ShouldHaveSingleItem(load.Report);
    }

    [Fact]
    public void A_pack_offers_its_modules_by_its_id_and_their_names()
    {
        var modules = Built(Header + """
            module tape(in = 0, time = 0.25, feedback = 0.5) = in * feedback

            module wobble(rate = 3) = sine(freq: rate)
            """).Modules;

        modules.Select(m => m.TypeId).ShouldBe(["ada.echoes.tape", "ada.echoes.wobble"]);
        modules[0].Sockets.ShouldBe([new PackSocket("in", 0), new PackSocket("time", 0.25f), new PackSocket("feedback", 0.5f)]);
        modules[0].Choices.ShouldBeEmpty();
        modules[1].Line.ShouldBe(4);
    }

    [Fact]
    public void A_module_is_called_in_its_pack_as_a_def_is()
    {
        var load = Built(Header + """
            module wobble(rate = 3) = sine(freq: rate)
            wobble(rate: 5) |> out.left
            """);

        load.Patch.Nodes.Single(n => n.TypeId == "osc.sine").InputValues[1].ShouldBe(5f);
    }

    [Fact]
    public void The_lines_above_a_module_are_its_card()
    {
        var card = Built(Header + """
            // Not part of the card: a blank line follows.

            /// A tape echo: what goes in comes back
            /// a little later, quieter each time.
            /// category: Effects · words: delay, tape, repeat
            /// glyph: M3,3 L27,27
            module tape(in = 0) = in
            """).Modules.ShouldHaveSingleItem().Card;

        card.ShouldBe(new ModuleCard(
            "A tape echo: what goes in comes back a little later, quieter each time.",
            "Effects", "delay, tape, repeat", "M3,3 L27,27"));
    }

    [Fact]
    public void A_module_with_no_lines_above_has_an_empty_card() =>
        Built(Header + "module tape(in = 0) = in").Modules.ShouldHaveSingleItem().Card.ShouldBe(new ModuleCard("", null, "", null));

    [Fact]
    public void A_sentence_with_a_colon_in_it_stays_help() =>
        Built(Header + "/// Two parts: dry and wet.\nmodule tape(in = 0) = in")
            .Modules.ShouldHaveSingleItem().Card.Help.ShouldBe("Two parts: dry and wet.");

    [Fact]
    public void A_def_is_not_offered() =>
        Built(Header + "def helper(a = 1) = a * 2\nmodule tape(in = 0) = helper(in)")
            .Modules.Select(m => m.Name).ShouldBe(["tape"]);

    [Fact]
    public void A_module_outside_a_pack_is_refused() =>
        Refused("module tape(in = 0) = in").Code.ShouldBe(IssueCode.ModuleOutsidePack);

    [Fact]
    public void A_module_inside_a_block_is_refused() =>
        Refused(Header + "group \"G\" {\n  module tape(in = 0) = in\n}").Code.ShouldBe(IssueCode.ModuleNotAtTop);

    [Theory]
    [InlineData("Tape")]
    [InlineData("tapeEcho")]
    public void A_module_name_is_lower_case(string name) =>
        Refused(Header + $"module {name}(in = 0) = in").Code.ShouldBe(IssueCode.BadModuleName);

    [Fact]
    public void Every_input_of_a_module_has_a_default()
    {
        var issue = Refused(Header + "module tape(in, time = 0.25) = in");

        issue.Code.ShouldBe(IssueCode.ModuleInputWithoutDefault);
        issue.Message.ShouldContain("'in'");
    }

    [Fact]
    public void A_module_named_twice_is_offered_once()
    {
        var load = Try(Header + "module tape(in = 0) = in\nmodule tape(in = 1) = in");

        load.Issues.ShouldHaveSingleItem().Code.ShouldBe(IssueCode.DefTwice);
        load.Modules.ShouldHaveSingleItem();
    }

    [Fact]
    public void Module_is_still_a_name_where_no_module_follows() =>
        Built("let module = 0.5\nsine(amp: module) |> out.left").Modules.ShouldBeEmpty();
}
