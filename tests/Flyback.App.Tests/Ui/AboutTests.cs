using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The About window. Most of what it shows is a constant next to the text that
/// renders it, so what is worth checking is that each fact reaches the window —
/// and, for the one fact nobody can verify by reading it, that nothing is shown
/// where there is nothing to show.
/// </summary>
public class AboutTests : UiTest
{
    private const string SamplePluginReport = "Loaded:\n    Test Plugin  (test.plugin)";

    private static Window Showing(string pluginReport = SamplePluginReport)
    {
        var window = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = About.View(pluginReport) };

        window.Show();
        Settle(window);

        return window;
    }

    private static IEnumerable<string> Words(Window window) =>
        All<TextBlock>(window).Select(t => t.Text ?? string.Empty);

    [AvaloniaFact]
    public void It_says_what_the_program_is_and_who_wrote_it()
    {
        var window = Showing();
        var said = Words(window).ToList();

        said.ShouldContain(GlobalConstants.ApplicationName);
        said.ShouldContain(About.Description);
        said.ShouldContain($"Version {About.Version}");
        said.ShouldContain($"by {About.Author}");
        said.ShouldContain(t => t.Contains(About.Licence) && t.Contains(About.Copyright));
    }

    /// <summary>The site is a click away rather than a URL somebody has to retype.</summary>
    [AvaloniaFact]
    public void It_shows_a_link_to_the_website()
    {
        var window = Showing();

        Words(window).ShouldContain(About.Website);
    }

    /// <summary>Clicks the mark, wherever it has ended up in the window.</summary>
    private static void Click(Window window, Point at, int times)
    {
        for (var click = 0; click < times; click++)
        {
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
        }

        Settle(window);
    }

    /// <summary>The middle of the mark, which is where the clicks go.</summary>
    private static Point Middle(Window window)
    {
        var mark = All<LogoMark>(window).Single();

        return mark.TranslatePoint(mark.Bounds.Center - mark.Bounds.Position, window) ?? default;
    }

    /// <summary>Frames are drawn off the UI thread, so it has to be let go of for one to arrive.</summary>
    private static bool Until(Func<bool> done, double seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        return done();
    }

    private static bool Playing(Window window) => All<Image>(window).Any(picture => picture.Source is not null);

    /// <summary>The mark is drawn rather than loaded, so it is a control like any other.</summary>
    [AvaloniaFact]
    public void It_shows_the_logo()
    {
        var window = Showing();

        All<LogoMark>(window).ShouldHaveSingleItem();
    }

    /// <summary>
    /// An address is a string nobody can check by reading, and one wrong
    /// character sends the money nowhere at all. Until a real one is set, the
    /// window must show no address rather than a plausible-looking stand-in — so
    /// this fails the day somebody puts an example in to see how it looks.
    /// </summary>
    [AvaloniaFact]
    public void No_address_is_shown_while_none_is_set()
    {
        var window = Showing();

        if (About.BitcoinAddress.Length > 0)
        {
            All<TextBox>(window).ShouldHaveSingleItem().Text.ShouldBe(About.BitcoinAddress);
            return;
        }

        All<TextBox>(window).ShouldBeEmpty("nothing that could be mistaken for an address");
        Words(window).ShouldContain(t => t.Contains("no donation address"));
    }

    /// <summary>
    /// What loaded and what did not is built by the host, not this file, so
    /// what is checked is that whatever is handed in reaches the window.
    /// </summary>
    [AvaloniaFact]
    public void It_shows_the_plugin_report_the_host_built()
    {
        var window = Showing();

        Words(window).ShouldContain(SamplePluginReport);
    }

    /// <summary>
    /// Seven clicks on the mark and it stops being a drawing: the patch it is a
    /// picture of plays in its place. Six leave it exactly as it was, which is
    /// what keeps it from going off under somebody double-clicking the dialog.
    /// </summary>
    [AvaloniaFact]
    public void The_mark_comes_alive_on_the_seventh_click()
    {
        var window = Showing();
        var at = Middle(window);

        Click(window, at, 6);
        All<LogoMark>(window).Single().IsVisible.ShouldBeTrue("six clicks are not seven");

        Click(window, at, 1);
        All<LogoMark>(window).Single().IsVisible.ShouldBeFalse();

        Until(() => Playing(window)).ShouldBeTrue("no frame was ever drawn");
    }

    /// <summary>
    /// And nothing puts the drawing back but closing the window, so a click meant
    /// for something else cannot take the picture away again.
    /// </summary>
    [AvaloniaFact]
    public void Nothing_it_is_clicked_with_afterwards_stops_it()
    {
        var window = Showing();
        var at = Middle(window);

        Click(window, at, 7);
        Until(() => Playing(window)).ShouldBeTrue("no frame was ever drawn");

        Click(window, at, 8);

        All<LogoMark>(window).Single().IsVisible.ShouldBeFalse();
        Playing(window).ShouldBeTrue();
    }
}
