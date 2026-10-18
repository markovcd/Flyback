using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Assist;

public partial class PatchWorkbenchTests
{
    // --- refusing rather than throwing --------------------------------------

    [Theory]
    [InlineData("write_patch", "{}")]
    [InlineData("write_patch", """{"source":"nonesuch() |> out.color"}""")]
    [InlineData("edit_patch", "{}")]
    [InlineData("edit_patch", """{"edits":"nope"}""")]
    [InlineData("edit_patch", """{"edits":[{"find":"nothing","replace":"x"}]}""")]
    [InlineData("edit_patch", """{"edits":[{"replace":"x"}]}""")]
    [InlineData("edit_patch", """{"edits":[{"find":"x"}]}""")]
    [InlineData("measure", """{"handles":["nothing"]}""")]
    [InlineData("propose", "{}")]
    [InlineData("nonsense", "{}")]
    public async Task Anything_it_cannot_do_is_refused_rather_than_thrown(string tool, string arguments)
    {
        var outcome = await Call(Bench(), tool, arguments);

        outcome.Ok.ShouldBeFalse();
        outcome.Text.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_knob_that_is_not_a_number_is_refused()
    {
        var bench = await Lit(0.5f);

        var set = await Edit(bench, "value: 0.5", "value: \"loud\"");

        set.Ok.ShouldBeFalse();
    }

    [Fact]
    public async Task A_knob_a_float_cannot_hold_is_refused()
    {
        var bench = await Lit(0.5f);

        var set = await Edit(bench, "value: 0.5", "value: 1e39");

        set.Ok.ShouldBeFalse();
        Should.NotThrow(() => bench.Save());
    }
}
