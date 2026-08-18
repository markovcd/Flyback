using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.App.Controls;
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
    private static Window Showing()
    {
        var window = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = About.View() };

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

        said.ShouldContain(About.Name);
        said.ShouldContain(About.Description);
        said.ShouldContain($"Version {About.Version}");
        said.ShouldContain($"by {About.Author}");
        said.ShouldContain(t => t.Contains(About.Licence) && t.Contains(About.Copyright));
    }

    /// <summary>The mark is drawn rather than loaded, so it is a control like any other.</summary>
    [AvaloniaFact]
    public void It_shows_the_logo()
    {
        var window = Showing();

        All<LogoMark>(window).ShouldHaveSingleItem();
    }

    /// <summary>
    /// The version comes off the assembly rather than out of a string, so that it
    /// can only ever say what was actually built.
    /// </summary>
    [AvaloniaFact]
    public void The_version_is_a_version_and_carries_no_commit()
    {
        About.Version.ShouldNotBeNullOrWhiteSpace();
        About.Version.ShouldNotContain("+");
        About.Version[0].ShouldBeInRange('0', '9');
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
}
