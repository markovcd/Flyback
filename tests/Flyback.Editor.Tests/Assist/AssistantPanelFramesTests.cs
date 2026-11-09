using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flyback.Assist;
using Flyback.Editor.Assist;
using Flyback.Engine.Render;
using Shouldly;

namespace Flyback.Editor.Tests.Assist;

public sealed class AssistantPanelFramesTests : AssistantPanelTest
{
    /// <summary>
    /// In the transcript between the caption above it and whatever came next,
    /// rather than off in a column of its own beside the conversation.
    /// </summary>
    [AvaloniaFact]
    public void A_frame_the_assistant_looked_at_sits_in_the_transcript()
    {
        var window = Showing(With(new Looks()), Configured("looks"));

        Instruction(window).Text = "make something";
        Settle(window);

        Press(SendButton(window));
        Settle(window);
        Settle(window);

        var frame = Frame(window);
        var working = (Panel)frame.Parent!;
        var at = working.Children.IndexOf(frame);

        working.Children[at - 1].ShouldBeOfType<SelectableTextBlock>().Text.ShouldBe("1 frame at 0.5s, 2 by 1.");

        var steps = frame.FindAncestorOfType<StepsGroup>()!;
        var flow = (Panel)steps.Parent!;
        var after = flow.Children[flow.Children.IndexOf(steps) + 1];

        Says(after.ShouldBeOfType<SelectableTextBlock>()).ShouldBe("that is a blue field.");
        frame.Child.ShouldBeOfType<Image>().Source.ShouldNotBeNull();
    }
}
