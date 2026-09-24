using Reqnroll;
using Shouldly;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>What the editor's panel says about the wires on a module or a box.</summary>
[Binding]
public sealed class EditorSteps(PatchContext context)
{
    private NodeGroup? box;

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

    [Given("the sine is drawn in one box with a Multiply it feeds")]
    public void GivenTheSineIsBoxed()
    {
        context.Add("Multiply", "math.mul");
        context.Wire("Sine", "out", "Multiply", "a");

        box = context.Patch.Group([context.Node("Sine").Id, context.Node("Multiply").Id]);
    }

    [Then("the box's {string} reads {string}")]
    public void ThenTheBoxSocketReads(string label, string text)
    {
        var sockets = context.Patch.SocketsOf(box.ShouldNotBeNull());
        var scene = new CanvasScene(context.Patch);

        var socket = sockets.Inputs.Concat(sockets.Outputs).Single(s => scene.Named(s)?.Label == label);

        (socket.IsOutput
            ? WireEnds.OutOf(context.Patch, socket.Node, socket.Port)
            : WireEnds.Into(context.Patch, socket.Node, socket.Port)).ShouldBe(text);
    }

    [When("the Multiply's {string} is put on the box's edge")]
    public void WhenTheMultiplysSocketIsExposed(string port)
    {
        var socket = new GroupSocket(context.Node("Multiply").Id, Port("math.mul", port, output: false), IsOutput: false);

        context.Patch.Exposable(box.ShouldNotBeNull(), socket).ShouldBeTrue();
        box.Expose(socket);
    }

    [Then("the Multiply's {string} cannot be put on the box's edge")]
    public void ThenTheMultiplysSocketIsNotExposable(string port) =>
        context.Patch.Exposable(
            box.ShouldNotBeNull(),
            new GroupSocket(context.Node("Multiply").Id, Port("math.mul", port, output: false), IsOutput: false))
            .ShouldBeFalse();

    [Then("the box has a {string} socket")]
    public void ThenTheBoxHasASocket(string label)
    {
        var sockets = context.Patch.SocketsOf(box.ShouldNotBeNull());
        var scene = new CanvasScene(context.Patch);

        sockets.Inputs.Concat(sockets.Outputs).ShouldContain(s => scene.Named(s)!.Value.Label == label);
    }

    private static int Port(string typeId, string name, bool output)
    {
        var def = NodeCatalog.Require(typeId);

        return (output ? def.Outputs : def.Inputs).ToList().FindIndex(p => p.Name == name);
    }
}
