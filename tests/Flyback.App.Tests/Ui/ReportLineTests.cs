using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Controls;
using Shouldly;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The status bar's line of prose: that a message too long for the window says
/// so with an ellipsis instead of being sheared off at the edge, and that what
/// it has said is still reachable after it has moved on.
/// </summary>
public class ReportLineTests : UiTest
{
    /// <summary>More messages than are kept, so the popup is as full as it ever gets.</summary>
    private const int Enough = 8;

    /// <summary>
    /// Longer than the bar it is put on, which is the whole point — and a real
    /// one, because the messages that overflow are the ones with a list in them.
    /// </summary>
    private const string TooLong =
        "Sound could not start — no output device would open. Plugins are looked for in "
        + "plugins\\ beside the program, and none of the four found there offers one.";

    private static string TimeOf(TextBlock block) => block.Text ?? string.Empty;

    private static (Window Window, ReportLine Line) Open(double width = 260)
    {
        var line = new ReportLine();
        var window = Show(line, width);

        // The window the shell puts one of these on says this, and the popup is
        // the wrong shape without it — so a test looking at the popup has to say
        // it too, or it is looking at one nobody ever sees.
        window.Styles.Add(ReportLine.Trim());

        return (window, line);
    }

    /// <summary>Every piece of text in the window, the popup over it included.</summary>
    private static List<string> Texts(Window window) =>
        All<TextBlock>(window).Select(TimeOf).ToList();

    /// <summary>
    /// What color a message is drawn in inside the popup. Told apart from the
    /// same words on the bar itself by the wrapping: a row of the log wraps,
    /// where the line it came from trims.
    /// </summary>
    private static Color ColorOf(Window window, string message) =>
        ((SolidColorBrush)All<TextBlock>(window)
            .First(t => t.Text == message && t.TextWrapping == TextWrapping.Wrap)
            .Foreground!).Color;

    /// <summary>
    /// The popup's rows, top to bottom. Told apart from the same words on the
    /// bar itself by the wrapping: a row of the log wraps, where the line it
    /// came from trims.
    /// </summary>
    private static List<string> Rows(Window window) =>
        All<TextBlock>(window).Where(t => t.TextWrapping == TextWrapping.Wrap).Select(TimeOf).ToList();

    private static void Click(Window window, Control target)
    {
        var at = target.TranslatePoint(
            new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("the line is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        Settle(window);
    }

    // --- the line -----------------------------------------------------------

    /// <summary>
    /// Trimmed, and inside its own bounds. Both halves matter: trimming that
    /// leaves the text wider than the control is a control that still overflows,
    /// with an ellipsis somewhere off the edge where nobody can see it.
    /// </summary>
    [AvaloniaFact]
    public void A_message_too_long_for_the_bar_ends_in_an_ellipsis()
    {
        var (window, line) = Open();

        line.Say(TooLong);
        Settle(window);

        var text = All<TextBlock>(line).Single();

        text.TextTrimming.ShouldBe(TextTrimming.CharacterEllipsis);
        text.Bounds.Width.ShouldBeLessThanOrEqualTo(line.Bounds.Width + 0.5);
        text.TextLayout.TextLines[0].HasCollapsed.ShouldBeTrue("it should be showing an ellipsis");
    }

    /// <summary>A message that fits is left alone.</summary>
    [AvaloniaFact]
    public void A_message_that_fits_is_not_trimmed()
    {
        var (window, line) = Open(600);

        line.Say("Wrote 10.0s to patch.mp4.");
        Settle(window);

        All<TextBlock>(line).Single().TextLayout.TextLines[0].HasCollapsed.ShouldBeFalse();
    }

    /// <summary>Nothing to report is reported by saying nothing.</summary>
    [AvaloniaFact]
    public void An_empty_message_clears_the_line_and_is_not_remembered()
    {
        var (window, line) = Open();

        line.Say("Something.");
        line.Say(string.Empty);
        Settle(window);

        All<TextBlock>(line).Single().Text.ShouldBeEmpty();
        line.History.ShouldBe(["Something."]);
    }

    // --- what it remembers --------------------------------------------------

    /// <summary>In the order it happened, which is the order it is shown in.</summary>
    [AvaloniaFact]
    public void What_was_said_is_kept_in_the_order_it_was_said()
    {
        var (_, line) = Open();

        line.Say("First.");
        line.Say("Second.");
        line.Say("Third.");

        line.History.ShouldBe(["First.", "Second.", "Third."]);
    }

    /// <summary>
    /// Recompiling repeats its list of problems after every edit, and a log of
    /// forty identical lines says less than one of them does.
    /// </summary>
    [AvaloniaFact]
    public void The_same_message_still_on_the_line_is_not_remembered_twice()
    {
        var (_, line) = Open();

        line.Say("Nothing is wired into the Output.");
        line.Say("Nothing is wired into the Output.");

        line.History.Count.ShouldBe(1);
    }

    /// <summary>
    /// But the same problem coming back after it was fixed is a new thing to
    /// have happened, and the clearing in between is what says so.
    /// </summary>
    [AvaloniaFact]
    public void The_same_message_after_a_clear_is_remembered_again()
    {
        var (_, line) = Open();

        line.Say("Nothing is wired into the Output.");
        line.Say(string.Empty);
        line.Say("Nothing is wired into the Output.");

        line.History.Count.ShouldBe(2);
    }

    /// <summary>
    /// A handful, and the oldest goes. This is here to catch the line that was
    /// replaced while you were reading it, not to be a record of the session.
    /// </summary>
    [AvaloniaFact]
    public void Only_the_last_few_messages_are_kept()
    {
        var (_, line) = Open();

        foreach (var n in Enumerable.Range(1, Enough)) line.Say($"Message {n}.");

        line.History.ShouldBe(["Message 4.", "Message 5.", "Message 6.", "Message 7.", "Message 8."]);
    }

    // --- several at once ----------------------------------------------------

    /// <summary>
    /// A compile that found three problems has three things to say. They share
    /// the line, which is the only one there is, but not a row in the log.
    /// </summary>
    [AvaloniaFact]
    public void Several_things_said_at_once_are_remembered_one_by_one()
    {
        var (window, line) = Open(900);

        line.Say(["Nothing is wired into the Output.", "The Sine has no frequency.", "The Delay feeds itself."]);
        Settle(window);

        line.History.ShouldBe(
            ["Nothing is wired into the Output.", "The Sine has no frequency.", "The Delay feeds itself."]);

        All<TextBlock>(line).Single().Text.ShouldBe(
            "Nothing is wired into the Output.  •  The Sine has no frequency.  •  The Delay feeds itself.");
    }

    /// <summary>
    /// And they are all still true, so they are all still shown as such — not
    /// merely whichever of them happened to be said last.
    /// </summary>
    [AvaloniaFact]
    public void Everything_still_on_the_bar_is_shown_as_current()
    {
        var (window, line) = Open(900);

        line.Say(["Old news."]);
        line.Say(["Nothing is wired into the Output.", "The Sine has no frequency."]);
        Settle(window);

        Click(window, line);

        ColorOf(window, "Nothing is wired into the Output.").ShouldBe(Colors.Attention);
        ColorOf(window, "The Sine has no frequency.").ShouldBe(Colors.Attention);
        ColorOf(window, "Old news.").ShouldBe(Colors.Muted);
    }

    /// <summary>
    /// Recompiling says the whole list again after every edit. Only what has
    /// joined it since is news — the rest is what is already on the bar.
    /// </summary>
    [AvaloniaFact]
    public void Only_what_has_joined_the_list_is_written_down()
    {
        var (_, line) = Open(900);

        line.Say(["Nothing is wired into the Output."]);
        line.Say(["Nothing is wired into the Output.", "The Sine has no frequency."]);
        line.Say(["Nothing is wired into the Output.", "The Sine has no frequency."]);

        line.History.ShouldBe(["Nothing is wired into the Output.", "The Sine has no frequency."]);
    }

    /// <summary>
    /// One of several being fixed leaves the others alone: still up, still
    /// current, and not written down a second time for having survived.
    /// </summary>
    [AvaloniaFact]
    public void Fixing_one_of_several_leaves_the_rest_where_they_were()
    {
        var (window, line) = Open(900);

        line.Say(["Nothing is wired into the Output.", "The Sine has no frequency."]);
        line.Say(["Nothing is wired into the Output."]);
        Settle(window);

        line.History.ShouldBe(["Nothing is wired into the Output.", "The Sine has no frequency."]);

        Click(window, line);

        ColorOf(window, "Nothing is wired into the Output.").ShouldBe(Colors.Attention);
        ColorOf(window, "The Sine has no frequency.").ShouldBe(Colors.Muted);
    }

    /// <summary>
    /// An export reports itself several times a second. One entry for the run,
    /// or the log holds nothing else.
    /// </summary>
    [AvaloniaFact]
    public void A_run_of_progress_leaves_one_entry()
    {
        var (_, line) = Open();

        line.Say("Exporting 10s — 0%", progress: true);
        line.Say("Exporting 10s — 50%", progress: true);
        line.Say("Exporting 10s — 100%", progress: true);
        line.Say("Wrote 10.0s to patch.mp4.");

        line.History.ShouldBe(["Exporting 10s — 100%", "Wrote 10.0s to patch.mp4."]);
    }

    // --- the other copy -----------------------------------------------------

    /// <summary>
    /// What the terminal gets. Once per thing said, however many times a compile
    /// repeats it — and never the count of an export, which would be a line a
    /// second for a fact nobody reads afterwards.
    /// </summary>
    [AvaloniaFact]
    public void Only_what_is_news_reaches_the_sink()
    {
        var (_, line) = Open(900);
        var heard = new List<string>();

        line.Said += (_, message) => heard.Add(message);

        line.Say(["Nothing is wired into the Output.", "The Sine has no frequency."]);
        line.Say(["Nothing is wired into the Output.", "The Sine has no frequency."]);
        line.Say(["Nothing is wired into the Output.", "The Delay feeds itself."]);
        line.Say("Exporting 10s — 50%", progress: true);
        line.Say("Wrote 10.0s to patch.mp4.");

        heard.ShouldBe([
            "Nothing is wired into the Output.",
            "The Sine has no frequency.",
            "The Delay feeds itself.",
            "Wrote 10.0s to patch.mp4.",
        ]);
    }

    // --- the popup ----------------------------------------------------------

    /// <summary>
    /// The click that the ellipsis is an invitation to. What it opens has the
    /// whole of the line in it, wrapped rather than trimmed a second time.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_the_line_shows_the_whole_of_it()
    {
        var (window, line) = Open();

        line.Say(TooLong);
        Settle(window);

        Click(window, line);

        Texts(window).ShouldContain(TooLong);
    }

    /// <summary>
    /// And everything before it, each with the time it was said at — which is
    /// what makes a line you missed one you can still go back to.
    /// </summary>
    [AvaloniaFact]
    public void The_popup_lists_what_came_before_with_the_time_of_each()
    {
        var (window, line) = Open();

        line.Say("First.");
        line.Say("Second.");
        line.Say("Third.");
        Settle(window);

        Click(window, line);

        var texts = Texts(window);

        texts.ShouldContain("First.");
        texts.ShouldContain("Second.");
        texts.ShouldContain("Third.");
        texts.Count(t => Regex.IsMatch(t, @"^\d\d:\d\d:\d\d$")).ShouldBe(3, "one time per message");
    }

    /// <summary>
    /// What will not fit on the bar is in the popup as well, so it outlives the
    /// hover it used to be the only reward for.
    /// </summary>
    [AvaloniaFact]
    public void The_detail_behind_a_message_is_in_the_popup_too()
    {
        var (window, line) = Open();

        line.Say("Sound could not start.", "plugins\\Wasapi, plugins\\OpenAi");
        line.Say("Something else happened.");
        Settle(window);

        Click(window, line);

        Texts(window).ShouldContain("plugins\\Wasapi, plugins\\OpenAi");
    }

    /// <summary>
    /// The message still on the bar is the one still true, and is the only one
    /// in the popup drawn in the color that says so.
    /// </summary>
    [AvaloniaFact]
    public void The_message_still_on_the_bar_stands_out_in_the_popup()
    {
        var (window, line) = Open();

        line.Say("Old news.");
        line.Say("Nothing is wired into the Output.");
        Settle(window);

        Click(window, line);

        ColorOf(window, "Nothing is wired into the Output.").ShouldBe(Colors.Attention);
        ColorOf(window, "Old news.").ShouldBe(Colors.Muted);
    }

    /// <summary>
    /// And once it is fixed, it is not. A warning that has gone from the bar is
    /// history like the rest of it; left in amber the popup would go on claiming
    /// something is wrong after the thing that was wrong had been put right.
    /// </summary>
    [AvaloniaFact]
    public void A_warning_that_has_been_fixed_is_no_longer_shown_as_current()
    {
        var (window, line) = Open();

        line.Say("Nothing is wired into the Output.");
        line.Say(string.Empty);
        Settle(window);

        Click(window, line);

        ColorOf(window, "Nothing is wired into the Output.").ShouldBe(Colors.Muted);
    }

    /// <summary>
    /// Oldest at the top. The newest ends up at the bottom, nearest the line it
    /// came off and where a run of them was watched arriving.
    /// </summary>
    [AvaloniaFact]
    public void The_popup_reads_downwards_to_the_newest()
    {
        var (window, line) = Open();

        line.Say("First.");
        line.Say("Second.");
        line.Say("Third.");
        Settle(window);

        Click(window, line);

        Rows(window).ShouldBe(["First.", "Second.", "Third."]);
    }

    /// <summary>
    /// And it opens at that end, rather than at a top somebody would have to
    /// scroll away from to reach what they clicked for.
    /// </summary>
    [AvaloniaFact]
    public void A_list_too_tall_to_fit_opens_at_the_bottom()
    {
        var (window, line) = Open(900);

        // Long enough to overflow the list's own height, which five ordinary
        // messages do not — a compile that found a dozen problems at once does.
        foreach (var n in Enumerable.Range(1, Enough))
            line.Say($"{n}. " + string.Join("  •  ", Enumerable.Repeat(
                "Nothing is wired into the Output, so there is nothing to see or hear", 6)));

        Settle(window);

        Click(window, line);
        Settle(window);

        // The list's own. The presenter has one too, and it holds the popup.
        var scroll = All<ScrollViewer>(window).Single(s => s.Content is StackPanel);

        scroll.Extent.Height.ShouldBeGreaterThan(scroll.Viewport.Height, "the list should be scrolling");
        scroll.Offset.Y.ShouldBe(scroll.Extent.Height - scroll.Viewport.Height, 0.5);
    }

    /// <summary>
    /// And nothing to drag along the bottom of it. The presenter the popup sits
    /// in pads what it holds and then measures it against a box that has already
    /// had that padding taken out, so a list as wide as the box overflows by
    /// exactly the padding — a scrollbar for ten pixels of nothing.
    /// </summary>
    [AvaloniaFact]
    public void The_popup_has_no_scrollbar_across_the_bottom()
    {
        var (window, line) = Open(900);

        foreach (var n in Enumerable.Range(1, Enough))
            line.Say($"{n}. Nothing is wired into the Output, so there is nothing "
                + "to see or hear. Patch something into its 'color' or its 'left'.");

        Settle(window);

        Click(window, line);

        All<ScrollBar>(window)
            .Where(bar => bar.Orientation == Orientation.Horizontal)
            .ShouldAllBe(bar => !bar.IsEffectivelyVisible);
    }

    /// <summary>An empty line still opens, and says that it is empty.</summary>
    [AvaloniaFact]
    public void Clicking_an_empty_line_says_there_is_nothing_to_show()
    {
        var (window, line) = Open();

        Click(window, line);

        Texts(window).ShouldContain("Nothing has been reported yet.");
    }

    // --- and where it lives -------------------------------------------------

    /// <summary>
    /// The bar it is on has to leave it room. It used to be the last thing in a
    /// row that handed every earlier child all the width it asked for, so it was
    /// pushed off the end of the window entirely.
    /// </summary>
    [AvaloniaFact]
    public void The_status_bar_keeps_a_share_of_its_width_for_the_report()
    {
        var window = new MainWindow();

        window.Show();
        Settle(window);

        var line = All<ReportLine>(window).Single();

        line.Bounds.Width.ShouldBeGreaterThan(120);
        line.TranslatePoint(new Point(line.Bounds.Width, 0), window)!.Value.X
            .ShouldBeLessThanOrEqualTo(window.Bounds.Width);
    }
}
