using Flyback.App.Assist;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>A conversation with the assistant over a patch of two Value modules, the first answering to value1 and the second to value2.</summary>
[Binding]
public sealed class ConversationSteps : IDisposable
{
    private readonly KnobSettingAssistant assistant = new();
    private readonly Patch canvas = new();
    private readonly NodeInstance first = NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0);
    private readonly NodeInstance second = NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0);
    private AssistantRun? run;

    private AssistantRun Run => run.ShouldNotBeNull();

    [Given("a conversation with the assistant about a patch")]
    public async Task GivenAConversation()
    {
        canvas.Nodes.Add(first);
        canvas.Nodes.Add(second);
        canvas.EnsureOutput();

        run = new AssistantRun(assistant, AssistantConfig.Unset, NodeCatalog.BuiltIn, canvas);

        await Turn("make something");
    }

    [When("a knob is turned on the canvas")]
    public void WhenAKnobIsTurned() => first.InputValues[0] = 0.25f;

    [When("a wire is added on the canvas")]
    public void WhenAWireIsAdded() => canvas.Connections.Add(new Connection(first.Id, 0, second.Id, 0));

    [When("the assistant sets one knob while another is turned on the canvas")]
    public Task WhenBothAreSet() => Works(() => first.InputValues[0] = 0.25f);

    [When("the assistant sets a knob that is also turned on the canvas while it works")]
    public Task WhenTheSameKnobIsSet() => Works(() => second.InputValues[0] = 0.1f);

    [Then("the next message carries on the same conversation")]
    public async Task ThenItCarriesOn()
    {
        Run.Reshaped(canvas).ShouldBeFalse();
        Run.CatchUp(canvas);

        await Turn("and again");

        Run.Turns.ShouldBe(2);
    }

    [Then("the assistant is told the knob's new value")]
    public void ThenItIsTold() => assistant.Heard[^1].ShouldContain("value1.value=0.25");

    [Then("the next message starts a new conversation")]
    public void ThenItStartsAgain() => Run.Reshaped(canvas).ShouldBeTrue();

    [Then("the patch it hands back has both knobs as they were set")]
    public void ThenBothKnobsStand()
    {
        Knob(first).ShouldBe(0.25f);
        Knob(second).ShouldBe(0.9f);
    }

    [Then("the patch it hands back has that knob as the assistant set it")]
    public void ThenTheAssistantsKnobStands() => Knob(second).ShouldBe(0.9f);

    /// <summary>A turn in which the assistant turns value2 up while <paramref name="meanwhile"/> happens on the canvas.</summary>
    private async Task Works(Action meanwhile)
    {
        assistant.Knobs = """{"handle":"value2","knobs":[{"port":"value","value":0.9}]}""";

        await Turn("turn the second one up");

        meanwhile();
        Run.Merge(canvas);
    }

    private float Knob(NodeInstance node) =>
        Run.Proposal.ShouldNotBeNull().Find(node.Id).ShouldNotBeNull().InputValues[0];

    private async Task Turn(string message)
    {
        await foreach (var _ in Run.Ask(message))
        {
        }
    }

    public void Dispose() => run?.Dispose();
}
