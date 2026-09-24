using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// <c>off name</c>, which switches a module off — the one statement about a
/// module that is neither a socket nor a wire.
/// </summary>
public class OffTests
{
    private static ModuleCatalog Modules => NodeCatalog.BuiltIn;

    private static LanguageLoad Build(string source) => PatchLanguage.Build(source, Modules);

    private static NodeInstance Only(Patch patch, string typeId) =>
        patch.Nodes.Single(n => n.TypeId == typeId);

    [Fact]
    public void A_module_named_after_the_word_is_switched_off()
    {
        var load = Build(
            """
            let wobble = sine(freq: 3)

            x |> rotate(x: _, angle: wobble) |> out.color
            off wobble
            """);

        load.Issues.ShouldBeEmpty(load.Report);
        Only(load.Patch, "osc.sine").Off.ShouldBeTrue();
        Only(load.Patch, "space.rotate").Off.ShouldBeFalse();
    }

    [Fact]
    public void The_Output_cannot_be_switched_off()
    {
        var load = Build("off out");

        load.Issues.ShouldHaveSingleItem().Message.ShouldContain("cannot be switched off");
        load.Patch.Output.Off.ShouldBeFalse();
    }

    [Fact]
    public void A_name_nothing_is_called_is_a_complaint()
    {
        Build("off nothing").Issues.ShouldHaveSingleItem().Message.ShouldContain("nothing here is called");
    }

    [Fact]
    public void A_name_that_is_not_a_module_is_a_complaint()
    {
        var load = Build(
            """
            let beats = tempo(bpm: 104).beats
            off beats
            """);

        load.Issues.ShouldHaveSingleItem().Message.ShouldContain("is not a module");
    }

    /// <summary>
    /// The word only begins the statement where a name follows it, so a binding
    /// somebody called <c>off</c> still reads as one.
    /// </summary>
    [Fact]
    public void A_binding_called_off_is_still_a_binding()
    {
        var load = Build(
            """
            let off = sine(freq: 3)

            off |> out.color
            """);

        load.Issues.ShouldBeEmpty(load.Report);
        Only(load.Patch, "osc.sine").Off.ShouldBeFalse();
    }

    [Fact]
    public void A_module_that_is_off_is_printed_as_off_and_reads_back_the_same_way()
    {
        var b = new PatchBuilder(Modules);

        var osc = b.Add("osc.sine");
        var turn = b.Add("space.rotate");
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(osc, 0, turn, 2);
        b.Wire(turn, 0, output, NodeCatalog.OutputColorPort);

        turn.Off = true;

        var source = PatchPrinter.Print(b.Patch, Modules);

        source.ShouldContain("off ");

        var again = Build(source);

        again.Issues.ShouldBeEmpty(again.Report);
        Only(again.Patch, "space.rotate").Off.ShouldBeTrue();
        Only(again.Patch, "osc.sine").Off.ShouldBeFalse();
    }

    /// <summary>
    /// A Coordinates or a Time that is off is written as a module of its own
    /// rather than as the bare word, which has nowhere to say it.
    /// </summary>
    [Fact]
    public void A_clock_that_is_off_keeps_a_name_to_be_said_by()
    {
        var b = new PatchBuilder(Modules);

        var clock = b.Add(NodeCatalog.TimeTypeId);
        var osc = b.Add("osc.sine");
        var output = b.Add(NodeCatalog.OutputTypeId);

        b.Wire(clock, 0, osc, 0);
        b.Wire(osc, 0, output, NodeCatalog.OutputColorPort);

        clock.Off = true;

        var again = Build(PatchPrinter.Print(b.Patch, Modules));

        again.Issues.ShouldBeEmpty(again.Report);
        Only(again.Patch, NodeCatalog.TimeTypeId).Off.ShouldBeTrue();
    }

    /// <summary>
    /// Arithmetic that is switched off is not folded into the sum around it. An
    /// Expression is a module like any other here: folding across one would fold
    /// away the fact that it is off.
    /// </summary>
    [Fact]
    public void An_expression_that_is_off_is_not_folded_into_the_sum_around_it()
    {
        var load = Build(
            """
            let wobble = t * 0.2

            x * wobble |> out.color
            off wobble
            """);

        load.Issues.ShouldBeEmpty(load.Report);

        var expressions = load.Patch.Nodes.Where(n => n.TypeId == NodeCatalog.ExpressionTypeId).ToList();

        expressions.Count.ShouldBe(2);
        expressions.Count(n => n.Off).ShouldBe(1);
    }
}
