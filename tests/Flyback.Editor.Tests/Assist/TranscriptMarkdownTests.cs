using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Flyback.Assist;
using Flyback.Editor.Assist;
using Shouldly;

namespace Flyback.Editor.Tests.Assist;

/// <summary>The assistant's words in the transcript, drawn as their Markdown says.</summary>
public class TranscriptMarkdownTests
{
    [AvaloniaFact]
    public void Bold_split_across_two_streamed_pieces_draws_once_both_have_arrived()
    {
        var transcript = new TranscriptView();

        transcript.Put(Voice.Said, "**Loud");

        Runs(transcript).Single().Text.ShouldBe("**Loud");

        transcript.Put(Voice.Said, "ness:** fine");

        var runs = Runs(transcript);

        runs.Select(run => run.Text).ShouldBe(["Loudness:", " fine"]);
        runs[0].FontWeight.ShouldBe(FontWeight.SemiBold);
    }

    [AvaloniaFact]
    public void What_is_kept_to_be_saved_is_the_markdown_as_it_arrived()
    {
        var transcript = new TranscriptView();

        transcript.Put(Voice.Said, "- `level` at 0.8");

        transcript.Lines.Single().Text.ShouldBe("- `level` at 0.8");
    }

    [AvaloniaFact]
    public void A_proposed_patch_draws_its_markdown_too()
    {
        var transcript = new TranscriptView();

        transcript.Put(Voice.Proposed, "Proposed: a **slow** field");

        Runs(transcript).ShouldContain(run => run.Text == "slow" && run.FontWeight == FontWeight.SemiBold);
    }

    private static List<Run> Runs(TranscriptView transcript) =>
        [.. transcript.GetLogicalDescendants().OfType<SelectableTextBlock>()
            .Single(block => block.Inlines is { Count: > 0 }).Inlines!.OfType<Run>()];
}
