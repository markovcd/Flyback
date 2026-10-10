using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Flyback.Ui.Controls;
using Shouldly;

namespace Flyback.Ui.Tests.Controls;

/// <summary>What the Markdown in the changelog and the assistant's replies draws as.</summary>
public class MarkdownTests
{
    [AvaloniaFact]
    public void Double_asterisks_draw_bold_and_backticks_draw_code()
    {
        var runs = Markdown.Spans("set **level** to `0.8` now");

        runs.Select(run => run.Text).ShouldBe(["set ", "level", " to ", "0.8", " now"]);
        runs[1].FontWeight.ShouldBe(FontWeight.SemiBold);
        runs[3].FontFamily.ShouldBe(Markdown.Code);
        runs[0].FontWeight.ShouldBe(FontWeight.Normal);
    }

    [AvaloniaFact]
    public void Asterisks_inside_code_stay_as_typed() =>
        Markdown.Spans("`a**b**c`").Single().Text.ShouldBe("a**b**c");

    [AvaloniaFact]
    public void An_unpaired_marker_is_shown_as_typed() =>
        Markdown.Spans("half **of it").Single().Text.ShouldBe("half **of it");

    [AvaloniaFact]
    public void A_heading_loses_its_hashes_and_draws_bold()
    {
        var runs = Markdown.Block("## Loudness").OfType<Run>().ToList();

        runs.Single().Text.ShouldBe("Loudness");
        runs.Single().FontWeight.ShouldBe(FontWeight.SemiBold);
    }

    [AvaloniaFact]
    public void A_bullet_of_either_kind_is_drawn_as_a_dot() =>
        Text(Markdown.Block("- one\n* two\n  - nested")).ShouldBe("•  one\n•  two\n  •  nested");

    [AvaloniaFact]
    public void A_fenced_block_loses_its_fences_and_is_set_in_code()
    {
        var inlines = Markdown.Block("before\n```\nsine 440 | out\n```");

        Text(inlines).ShouldBe("before\nsine 440 | out");
        inlines.OfType<Run>().Last().FontFamily.ShouldBe(Markdown.Code);
    }

    [AvaloniaFact]
    public void A_numbered_list_and_a_line_of_asterisks_are_left_as_typed() =>
        Text(Markdown.Block("1. first\n2 * 3 = 6")).ShouldBe("1. first\n2 * 3 = 6");

    private static string Text(IReadOnlyList<Inline> inlines) =>
        string.Concat(inlines.Select(inline => inline is Run run ? run.Text : "\n"));
}
