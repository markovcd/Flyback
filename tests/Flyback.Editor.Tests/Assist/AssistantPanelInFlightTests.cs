using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Assist;
using Flyback.Engine.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Assist;

public sealed class AssistantPanelInFlightTests : AssistantPanelTest
{
    [AvaloniaFact]
    public void A_turn_in_flight_says_so_at_the_end_of_the_transcript()
    {
        var held = new Held();
        var window = Showing(With(held), Configured("held"));

        Thinking(window).IsVisible.ShouldBeFalse("nothing has been asked yet");

        Instruction(window).Text = "make something";
        Settle(window);

        Press(SendButton(window));
        Settle(window);

        Thinking(window).IsVisible.ShouldBeTrue();
        Thinking(window).Text.ShouldStartWith("Thinking");

        held.Release.SetResult();
        Settle(window);
        Settle(window);

        Thinking(window).IsVisible.ShouldBeFalse("the turn ended");
    }

    [AvaloniaFact]
    public void The_conversation_is_working_for_as_long_as_a_turn_is_in_flight()
    {
        var held = new Held();
        var conversation = new AssistantConversation(() => Presets.Plasma(NodeCatalog.BuiltIn));
        var window = Showing(With(held), Configured("held"), conversation: conversation);

        conversation.Working.ShouldBeFalse();

        Instruction(window).Text = "make something";
        Settle(window);

        Press(SendButton(window));
        Settle(window);

        conversation.Working.ShouldBeTrue();

        held.Release.SetResult();
        Settle(window);
        Settle(window);

        conversation.Working.ShouldBeFalse();
    }
}
