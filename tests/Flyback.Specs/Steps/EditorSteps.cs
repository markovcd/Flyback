using Reqnroll;
using Shouldly;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>What the editor's module panel says about a module's wires.</summary>
[Binding]
public sealed class EditorSteps(PatchContext context)
{
    [Given("a sine driven by Time")]
    public void GivenASineDrivenByTime()
    {
        context.Add("Sine", NodeCatalog.SineTypeId);
        context.Add("Time", NodeCatalog.TimeTypeId);
        context.Wire("Time", "t", "Sine", "freq");
    }

    [Given("Time also drives the sine's {string}")]
    public void GivenTimeAlsoDrives(string port) => context.Wire("Time", "t", "Sine", port);

    [When("Time is renamed {string}")]
    public void WhenTimeIsRenamed(string name) => context.Node("Time").Name = name;

    [Then("the sine's {string} reads {string}")]
    public void ThenTheInputReads(string port, string text) =>
        WireEnds.Into(context.Patch, context.Node("Sine").Id, Port(NodeCatalog.SineTypeId, port, output: false))
            .ShouldBe(text);

    [Then("Time's {string} reads {string}")]
    public void ThenTheOutputReads(string port, string text) =>
        WireEnds.OutOf(context.Patch, context.Node("Time").Id, Port(NodeCatalog.TimeTypeId, port, output: true))
            .ShouldBe(text);

    private static int Port(string typeId, string name, bool output)
    {
        var def = NodeCatalog.Require(typeId);

        return (output ? def.Outputs : def.Inputs).ToList().FindIndex(p => p.Name == name);
    }
}
