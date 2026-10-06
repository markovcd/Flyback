using Flyback.Assist;
using Flyback.Core.Graph;
using Flyback.Editor.Assist;
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
    public Task GivenAConversation() => Start(AssistantRun.ContextLimit);

    [Given("a conversation with the assistant limited to {int} tokens of context")]
    public Task GivenALimitedConversation(int limit) => Start(limit);

    private async Task Start(int maxContext)
    {
        canvas.Nodes.Add(first);
        canvas.Nodes.Add(second);
        canvas.EnsureOutput();

        run = new AssistantRun(assistant, AssistantConfig.Unset, NodeCatalog.BuiltIn, canvas, maxContext);

        await Turn("make something");
    }

    [Given("each turn it takes costs {int} tokens in, {int} of them cached, and {int} out")]
    public void GivenEachTurnCosts(int input, int cached, int output) =>
        assistant.Costs = new PatchEvent.Cost(input, cached, output);

    [When("it is asked twice more")]
    public async Task WhenAskedTwiceMore()
    {
        await Turn("once more");
        await Turn("and again");
    }

    [When("it is asked once more")]
    public Task WhenAskedOnceMore() => Turn("once more");

    [When("it is asked {int} times more")]
    public async Task WhenAskedTimesMore(int times)
    {
        for (var time = 0; time < times; time++) await Turn("and again");
    }

    [Then("it still takes the next message")]
    public async Task ThenItStillTakes()
    {
        var turns = Run.Turns;

        Run.Exhausted.ShouldBeFalse();
        await Turn("and again");

        Run.Turns.ShouldBe(turns + 1);
    }

    [Then("it takes no more messages, saying it has grown past its limit")]
    public async Task ThenItTakesNoMore()
    {
        Run.Exhausted.ShouldBeTrue();

        var events = new List<PatchEvent>();

        await foreach (var happened in Run.Ask("and again")) events.Add(happened);

        events.ShouldHaveSingleItem().ShouldBeOfType<PatchEvent.Failed>().Message.ShouldContain("past its limit");
    }

    [Then("the conversation has cost {int} tokens in, {int} of them cached, and {int} out")]
    public void ThenItHasCost(int input, int cached, int output) =>
        (Run.Tokens.Requests, Run.Tokens.Input, Run.Tokens.CacheRead, Run.Tokens.Output).ShouldBe((2, input, cached, output));

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
