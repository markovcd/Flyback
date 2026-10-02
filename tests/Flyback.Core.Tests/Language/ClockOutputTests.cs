using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary><c>t.length</c> and <c>t.progress</c>: the clock's other two outputs.</summary>
public class ClockOutputTests
{
    [Theory]
    [InlineData("t.length", 1)]
    [InlineData("t.progress", 2)]
    public void A_dotted_t_reads_the_shared_clock(string word, int port)
    {
        var load = PatchLanguage.Build($"sine(amp: t) + {word} |> out.left", NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        var clock = load.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.TimeTypeId);
        load.Patch.Connections.ShouldContain(c => c.SourceNode == clock.Id && c.SourcePort == port);
    }

    [Theory]
    [InlineData("t.length")]
    [InlineData("t.progress")]
    public void A_dotted_t_is_printed_as_written(string word)
    {
        var patch = PatchLanguage.Build($"{word} |> out.left", NodeCatalog.BuiltIn).Patch;

        PatchPrinter.Print(patch).ShouldContain($"{word} |> out.left");
    }

    [Fact]
    public void An_output_the_clock_does_not_have_is_refused_by_name()
    {
        var load = PatchLanguage.Build("t.speed |> out.left", NodeCatalog.BuiltIn);

        var issue = load.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe(IssueCode.UnknownOutput);
        issue.Message.ShouldContain("progress");
    }
}
