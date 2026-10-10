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
    [InlineData("add_module", """{"type_id":"nowhere.at.all"}""")]
    [InlineData("add_module", "{}")]
    [InlineData("set_knobs", """{"handle":"nothing","knobs":[]}""")]
    [InlineData("connect", """{"from":"nothing","to":"nothing","to_port":"x"}""")]
    [InlineData("disconnect", """{"handle":"nothing","port":"x"}""")]
    [InlineData("remove_module", "{}")]
    [InlineData("switch_module", """{"handle":"nothing"}""")]
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
        var bench = Bench();

        await Call(bench, "add_module", """{"type_id":"value","handle":"knob1"}""");
        var set = await Call(bench, "set_knobs", """{"handle":"knob1","knobs":[{"port":"value","value":"loud"}]}""");

        set.Ok.ShouldBeFalse();
    }

    [Fact]
    public async Task A_knob_a_float_cannot_hold_is_refused()
    {
        var bench = await Lit();

        var set = await Call(bench, "set_knobs", """{"handle":"knob1","knobs":[{"port":"value","value":1e39}]}""");

        set.Ok.ShouldBeFalse();
        Should.NotThrow(() => bench.Save());
    }
}
