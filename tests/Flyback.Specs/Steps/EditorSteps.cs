using Avalonia.Input;
using Reqnroll;
using Shouldly;
using Flyback.Core.Graph;
using Flyback.Specs.Support;

namespace Flyback.Specs.Steps;

/// <summary>What the editor's panel says about the wires on a module or a box, read off the panel itself.</summary>
[Binding]
public sealed class EditorSteps(PatchContext context, Editor editor)
{
    private IReadOnlyList<Guid> box = [];

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
    public void WhenTimeIsRenamed(string name)
    {
        editor.Select(context.Node("Time").Id);
        editor.Rename(name);
    }

    [Then("the sine's {string} reads {string}")]
    public void ThenTheInputReads(string port, string text) => Reads(context.Node("Sine").Id, port, text);

    [Then("Time's {string} reads {string}")]
    public void ThenTheOutputReads(string port, string text) => Reads(context.Node("Time").Id, port, text);

    [Given("the sine is drawn in one box with a Multiply it feeds")]
    public void GivenTheSineIsBoxed()
    {
        context.Add("Multiply", "math.mul");
        context.Wire("Sine", "out", "Multiply", "a");

        box = [context.Node("Sine").Id, context.Node("Multiply").Id];

        editor.Select([.. box]);
        editor.PressCtrl(PhysicalKey.G);
    }

    [Then("the box's {string} reads {string}")]
    public void ThenTheBoxSocketReads(string label, string text)
    {
        editor.Select([.. box]);
        editor.PanelRow(label).ShouldBe(text);
    }

    [When("the Multiply's {string} is put on the box's edge")]
    public void WhenTheMultiplysSocketIsExposed(string port)
    {
        editor.Select(context.Node("Multiply").Id);
        editor.PressInRow(port, "exposeSocket");
    }

    [Then("the Multiply's {string} cannot be put on the box's edge")]
    public void ThenTheMultiplysSocketIsNotExposable(string port)
    {
        editor.Select(context.Node("Multiply").Id);

        editor.PanelRow(port).ShouldNotBeNull($"the panel has no row for '{port}'");
        editor.RowOffers(port, "exposeSocket").ShouldBeFalse();
    }

    [Then("the box has a {string} socket")]
    public void ThenTheBoxHasASocket(string label)
    {
        editor.Select([.. box]);
        editor.PanelRow(label).ShouldNotBeNull($"the box's panel has no row for '{label}'");
    }

    private void Reads(Guid module, string socket, string text)
    {
        editor.Select(module);
        editor.PanelRow(socket).ShouldBe(text);
    }
}
