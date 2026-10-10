using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Flyback.Editor.Assist;
using Flyback.Editor.Notices;
using Flyback.Editor.Windows;
using Flyback.Assist;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Secrets;
using Flyback.Plugins.Settings;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Assist;

public partial class AssistantPanelTests
{
    // --- a turn in flight -----------------------------------------------------

    /// <summary>
    /// One that answers when the test says so, which is the only way to look at a
    /// panel with a turn still running in it.
    /// </summary>
    private sealed class Held() : Provider(new AssistantSchema(
        "held",
        [new AssistantModel("held", Vision: false)],
        "NONE",
        "none needed"))
    {
        public TaskCompletionSource Release { get; } = new();

        public override string Id => "held";

        public override string Name => "Answers on cue";

        public override IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
            new Turn(Release.Task);
    }

    private sealed class Turn(Task release) : IPatchSession
    {
        public async IAsyncEnumerable<PatchEvent> Ask(
            string instruction,
            [EnumeratorCancellation] CancellationToken cancel)
        {
            await release;

            yield return new PatchEvent.Said("there you are.");
        }

        public void Dispose()
        {
        }
    }

    private static TextBlock Thinking(Window window) =>
        All<TextBlock>(window).Single(block => block.Name == "thinking");

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
