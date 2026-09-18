using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The preset list is in kind order and says which kind is which: a heading over
/// each run of one.
/// </summary>
/// <remarks>
/// A row of the list rather than a line drawn on the first preset of the run,
/// which is what it was at first: a row lights up whole, so a heading carried on
/// one lit with it and read as a label for that one preset.
/// </remarks>
public class PresetListTests : UiTest
{
    private static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    /// <summary>The toolbar's list of patches to start from.</summary>
    private static ComboBox Presets(MainWindow window) =>
        All<ComboBox>(window).Single(box => box.Name == "presets");

    /// <summary>
    /// The words a kind is headed with, which are not the words the enum uses:
    /// nobody opening the list is looking for an Interplay.
    /// </summary>
    private static readonly Dictionary<PresetKind, string> Headings = new()
    {
        [PresetKind.Blank] = "BLANK",
        [PresetKind.Idea] = "ONE IDEA",
        [PresetKind.Interplay] = "SOUND AND PICTURE",
        [PresetKind.Showcase] = "SHOWCASE",
    };

    [AvaloniaFact]
    public void A_heading_stands_over_each_run_of_a_kind()
    {
        var window = Open();
        var presets = Presets(window);
        var rows = presets.ItemsSource!.Cast<object>().ToList();

        var headed = new List<PresetKind>();
        var under = (PresetKind?)null;

        for (var row = 0; row < rows.Count; row++)
        {
            if (rows[row] is PatchPreset preset)
            {
                under.ShouldNotBeNull("a preset stands before the first heading")
                    .ShouldBe(preset.Kind, $"“{preset.Name}” is under the heading for another kind");
                continue;
            }

            // Anything else in the list is a heading, and a heading is a heading
            // for what is below it.
            rows.Count.ShouldBeGreaterThan(row + 1, "a heading with nothing under it");

            var first = rows[row + 1].ShouldBeOfType<PatchPreset>();

            headed.ShouldNotContain(first.Kind, "a kind is headed once, where its presets begin");
            headed.Add(first.Kind);
            under = first.Kind;

            // Built from the list's own template, that being where the words are.
            var drawn = presets.ItemTemplate!.Build(rows[row]).ShouldBeOfType<TextBlock>();

            drawn.Text.ShouldBe(Headings[first.Kind]);
        }

        headed.ShouldBe([PresetKind.Blank, PresetKind.Idea, PresetKind.Interplay, PresetKind.Showcase]);
    }

    /// <summary>
    /// And it is not something to point at. A heading that took the pointer would
    /// light under it like a preset does, saying the one below it is all the
    /// heading is about.
    /// </summary>
    [AvaloniaFact]
    public void A_heading_is_not_a_row_anybody_can_point_at()
    {
        var window = Open();
        var presets = Presets(window);

        presets.IsDropDownOpen = true;
        Settle(window);

        var rows = presets.ItemsSource!.Cast<object>().ToList();
        var checkedHeadings = 0;

        for (var row = 0; row < rows.Count; row++)
        {
            // A list this long is built as it is scrolled, so the rows below the
            // dropdown have no container to ask yet.
            if (presets.ContainerFromIndex(row) is not { } container) continue;

            if (rows[row] is PatchPreset)
            {
                container.IsHitTestVisible.ShouldBeTrue("a preset is what the list is for");
                continue;
            }

            checkedHeadings++;

            container.IsHitTestVisible.ShouldBeFalse("a heading is only read");
            container.Focusable.ShouldBeFalse("a heading is only read");
        }

        checkedHeadings.ShouldBeGreaterThan(0, "the open list showed no heading to check");
    }

    /// <summary>
    /// The blank canvas is the first thing in the list, so that somebody meaning
    /// to build their own patch does not read past thirty of somebody else's —
    /// and it is still not what the window opens on, a program that shipped
    /// patches and showed none of them being one whose presets nobody finds.
    /// </summary>
    [AvaloniaFact]
    public void The_blank_canvas_heads_the_list_and_is_not_what_opens()
    {
        var window = Open();
        var presets = Presets(window);

        presets.ItemsSource!.OfType<PatchPreset>().First().Kind.ShouldBe(PresetKind.Blank);

        (presets.SelectedItem as PatchPreset).ShouldNotBeNull()
            .Kind.ShouldNotBe(PresetKind.Blank, "the window opens on a patch");
    }
}
