using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Flyback.App.Controls;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Writing to the author from the status bar: what goes up is what was typed and
/// what the letter printed, and nothing else (ADR-0136).
/// </summary>
public sealed class LetterTests : UiTest
{
    private static readonly LetterAbout About = new(
        "1.4.0+abc1234", "Microsoft Windows 10.0.26200", "WinIO, Picture; sound: WASAPI");

    /// <summary>What a send was asked to send, or null while nothing has been.</summary>
    private sealed record Sent(string Mood, string Message, string? Contact);

    private (Window Window, Control Page, List<Sent> Sends) Letter(Func<Task>? answer = null)
    {
        var sends = new List<Sent>();

        var page = LetterView.View(About, (mood, message, contact, _) =>
        {
            sends.Add(new Sent(mood, message, contact));

            return answer?.Invoke() ?? Task.CompletedTask;
        });

        return (Show(page, width: 480), page, sends);
    }

    private static Button Button(Control page, string name) => All<Button>(page).Single(b => b.Name == name);

    private static TextBox Box(Control page, string name) => All<TextBox>(page).Single(t => t.Name == name);

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));

    private static void Choose(Control page, string mood) =>
        All<RadioButton>(page).Single(r => (string?)r.Tag == mood).IsChecked = true;

    [AvaloniaFact]
    public void A_letter_carries_what_was_typed_and_what_this_copy_is()
    {
        var (window, page, sends) = Letter();

        Choose(page, "bad");
        Box(page, "message").Text = "  The delay clicks when I turn it.  ";
        Box(page, "contact").Text = "  ada@example.org  ";
        Settle(window);

        Press(Button(page, "send"));

        sends.ShouldHaveSingleItem();
        sends[0].Mood.ShouldBe("bad");
        sends[0].Message.ShouldBe("The delay clicks when I turn it.");
        sends[0].Contact.ShouldBe("ada@example.org");
    }

    [AvaloniaFact]
    public void An_address_left_blank_is_no_address()
    {
        var (window, page, sends) = Letter();

        Choose(page, "good");
        Box(page, "message").Text = "The canvas is lovely.";
        Settle(window);

        Press(Button(page, "send"));

        sends.ShouldHaveSingleItem();
        sends[0].Contact.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Nothing_is_sent_until_there_is_a_mood_and_something_to_say()
    {
        var (window, page, _) = Letter();
        var send = Button(page, "send");

        send.IsEnabled.ShouldBeFalse("an empty letter is not a letter");

        Choose(page, "idea");
        Settle(window);
        send.IsEnabled.ShouldBeFalse("a mood on its own says nothing");

        Box(page, "message").Text = "A module that counts.";
        Settle(window);
        send.IsEnabled.ShouldBeTrue();

        Box(page, "message").Text = "   ";
        Settle(window);
        send.IsEnabled.ShouldBeFalse("whitespace is not something to say");
    }

    [AvaloniaFact]
    public void The_letter_prints_what_the_program_adds_to_it()
    {
        var (_, page, _) = Letter();

        var said = All<TextBlock>(page).Select(t => t.Text).ToList();

        said.ShouldContain($"Version {About.Version}");
        said.ShouldContain(About.Platform);
        said.ShouldContain(About.Plugins);
    }

    [AvaloniaFact]
    public void A_site_that_will_not_take_it_leaves_the_letter_up_with_a_reason()
    {
        var (window, page, sends) = Letter(() => throw new HttpRequestException("the site is not answering"));

        Choose(page, "other");
        Box(page, "message").Text = "Hello.";
        Settle(window);

        var send = Button(page, "send");
        Press(send);
        Settle(window);

        sends.ShouldHaveSingleItem();
        send.IsEnabled.ShouldBeTrue("a letter that did not go is one to try again");

        var status = All<TextBlock>(page).Single(t => t.Name == "letterStatus");

        status.IsVisible.ShouldBeTrue();
        status.Text!.ShouldContain("the site is not answering");
    }

    [AvaloniaFact]
    public void The_status_bar_offers_the_letter_last_of_all()
    {
        var window = NewMainWindow();

        window.Show();
        Settle(window);

        All<Button>(window).ShouldContain(b => b.Name == "letter");
    }

    /// <summary>
    /// The bar reads left to right as what the patch costs and then what is not
    /// about the patch at all, and the rule is what says so.
    /// </summary>
    [AvaloniaFact]
    public void A_rule_sets_the_letter_apart_from_the_count()
    {
        var window = NewMainWindow();

        window.Show();
        Settle(window);

        var letter = All<Button>(window).Single(b => b.Name == "letter");
        var rule = All<TextBlock>(window).Single(b => b.Name == "statusRule");

        rule.Text.ShouldBe("|", "the same bar the count divides itself with");

        Grid.GetColumn(rule).ShouldBe(Grid.GetColumn(letter) - 1);
        rule.Bounds.Right.ShouldBeLessThanOrEqualTo(letter.Bounds.Left);
    }

    /// <summary>
    /// The font Flyback embeds, asked for by the resource it is embedded as.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Typeface.Default"/>, which is whatever the machine puts up:
    /// Segoe UI on Windows and a fontconfig match on Linux, so a question asked of
    /// it is answered by the host rather than by anything this repository ships.
    /// </remarks>
    private static readonly FontFamily Shipped = new("avares://Avalonia.Fonts.Inter/Assets#Inter");

    /// <summary>
    /// Drawn rather than typed, which is not a preference: no font here has an
    /// envelope, and what a platform substitutes for one is its emoji face — a
    /// full-color picture in a bar of thin gray strokes.
    /// </summary>
    [AvaloniaFact]
    public void The_letter_is_drawn_rather_than_left_to_a_font()
    {
        var window = NewMainWindow();

        window.Show();
        Settle(window);

        var letter = All<Button>(window).Single(b => b.Name == "letter");

        letter.Content.ShouldBeOfType<Avalonia.Controls.Shapes.Path>();

        new Typeface(Shipped).GlyphTypeface.CharacterToGlyphMap.TryGetGlyph('✉', out _).ShouldBeFalse(
            "the embedded font has gained an envelope, so this drawing could be a character again");
    }
}
