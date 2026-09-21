using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core;
using Shouldly;
using Colors = Flyback.App.Controls.Colors;

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

    private Window Showing(string pluginReport = SamplePluginReport)
    {
        var window = Owned(new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = About.View(pluginReport) });

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
    /// character sends the money nowhere at all, so the window never spells one
    /// out. With an address set it is in the code and nowhere else; with none
    /// set there is nothing there that could be taken for one — which fails the
    /// day somebody puts an example in to see how it looks.
    /// </summary>
    [AvaloniaFact]
    public void The_window_never_spells_the_address_out()
    {
        var window = Showing();

        All<TextBox>(window).ShouldNotContain(box => (box.Text ?? string.Empty).Contains("bc1"), "no box spells an address out");

        if (About.BitcoinAddress.Length == 0)
        {
            All<QrCode>(window).ShouldBeEmpty("no code either, with nothing to encode");
            Words(window).ShouldContain(t => t.Contains("no donation address"));

            return;
        }

        Words(window).ShouldNotContain(t => t.Contains(About.BitcoinAddress), "the code is the only place it is");
    }

    /// <summary>
    /// The code is the address encoded, not a picture of one kept beside it, and
    /// it says what clicking it does before anybody clicks it.
    /// </summary>
    [AvaloniaFact]
    public void The_address_is_also_a_code_that_says_it_can_be_copied()
    {
        if (About.BitcoinAddress.Length == 0) return;

        var window = Showing();
        var code = All<QrCode>(window).ShouldHaveSingleItem();

        code.Text.ShouldBe(About.BitcoinAddress);
        ToolTip.GetTip(code).ShouldBe("Click to copy the address");
    }

    /// <summary>
    /// Bech32 carries a checksum over the whole address, so the one fact nobody
    /// can check by reading is checkable after all: a character mistyped into
    /// the constant fails here rather than swallowing somebody's donation.
    /// </summary>
    [AvaloniaFact]
    public void The_donation_address_passes_its_own_checksum()
    {
        if (About.BitcoinAddress.Length == 0)
            return;

        const string alphabet = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
        var address = About.BitcoinAddress;

        address.ShouldBe(address.ToLowerInvariant(), "bech32 is one case throughout, and lower case is the readable one");
        address.ShouldStartWith("bc1q");
        address.Length.ShouldBe(42, "a mainnet pay-to-witness-public-key-hash address");

        // The human-readable part "bc", expanded as bech32 asks, then the data.
        List<int> values = ['b' >> 5, 'c' >> 5, 0, 'b' & 31, 'c' & 31];

        values.AddRange(address[3..].Select(c => alphabet.IndexOf(c)));
        values.ShouldNotContain(-1, "every character is in the bech32 alphabet");

        Polymod(values).ShouldBe(1, "the bech32 checksum");
    }

    /// <summary>BIP-173's checksum, which comes to 1 over a whole valid address.</summary>
    private static int Polymod(IEnumerable<int> values)
    {
        int[] generator = [0x3B6A57B2, 0x26508E6D, 0x1EA119FA, 0x3D4233DD, 0x2A1462B3];
        var checksum = 1;

        foreach (var value in values)
        {
            var top = checksum >> 25;
            checksum = ((checksum & 0x1FFFFFF) << 5) ^ value;

            for (var i = 0; i < 5; i++)
                if (((top >> i) & 1) != 0)
                    checksum ^= generator[i];
        }

        return checksum;
    }

    /// <summary>
    /// What loaded and what did not is built by the host, not this file, so
    /// what is checked is that whatever is handed in reaches the window.
    /// </summary>
    [AvaloniaFact]
    public void It_shows_the_plugin_report_the_host_built()
    {
        var window = Showing();

        All<TextBox>(window).ShouldHaveSingleItem().Text.ShouldBe(SamplePluginReport);
    }

    /// <summary>
    /// The report is read, selected and copied from, never typed into, and it
    /// wears the window's own color rather than a field's.
    /// </summary>
    [AvaloniaFact]
    public void The_plugin_report_is_read_only_on_the_windows_own_color()
    {
        var window = Showing();
        var box = All<TextBox>(window).ShouldHaveSingleItem();

        box.IsReadOnly.ShouldBeTrue();
        box.AcceptsReturn.ShouldBeTrue("a report is several lines");
        ((ISolidColorBrush)box.Background!).Color.ShouldBe(Colors.Panel);
    }

    /// <summary>
    /// However long the report, the window is the same size: it is the report
    /// that scrolls, and the window around it never grows a bar of its own.
    /// </summary>
    [AvaloniaFact]
    public void A_long_report_scrolls_itself_and_leaves_the_window_the_same_height()
    {
        var shortHeight = Showing().Bounds.Height;

        var lines = Enumerable.Range(0, 200).Select(n => $"    Plugin {n}  (plugin.{n})");
        var window = Showing(string.Join(Environment.NewLine, lines));

        window.Bounds.Height.ShouldBe(shortHeight);
        All<TextBox>(window).ShouldHaveSingleItem().Bounds.Height.ShouldBeLessThan(200);
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
