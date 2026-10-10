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
    // --- a frame the assistant looked at --------------------------------------

    /// <summary>One that renders, so the panel has a picture to place.</summary>
    private sealed class Looks() : Provider(new AssistantSchema(
        "looks",
        [new AssistantModel("looks")],
        "NONE",
        "none needed"))
    {
        public override string Id => "looks";

        public override string Name => "Renders once";

        public override IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => new Look();
    }

    private sealed class Look : IPatchSession
    {
        public async IAsyncEnumerable<PatchEvent> Ask(
            string instruction,
            [EnumeratorCancellation] CancellationToken cancel)
        {
            await Task.Yield();

            yield return new PatchEvent.Saw(Frame(), "1 frame at 0.5s, 2 by 1.");
            yield return new PatchEvent.Said("that is a blue field.");
        }

        private static byte[] Frame()
        {
            var png = new MemoryStream();

            PngWriter.WriteBgra(png, new byte[2 * 1 * 4], 2, 1, 2 * 4);

            return png.ToArray();
        }

        public void Dispose()
        {
        }
    }

    private static Border Frame(Window window) =>
        All<Border>(window).Single(border => border.Name == "frame");

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

        after.ShouldBeOfType<SelectableTextBlock>().Text.ShouldBe("that is a blue field.");
        frame.Child.ShouldBeOfType<Image>().Source.ShouldNotBeNull();
    }
}
