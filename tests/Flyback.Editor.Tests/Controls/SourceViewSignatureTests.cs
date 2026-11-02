using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.Editor.Composition;
using Flyback.Editor.Hubs;
using Shouldly;

namespace Flyback.Editor.Tests.Controls;

/// <summary>The sockets of the call the caret is in, above it, and what a word is, over it.</summary>
public class SourceViewSignatureTests : SourceViewTest
{
    private static SourceView View(MainWindow window) => All<SourceView>(window).Single();

    private static TextEditor Empty(MainWindow window)
    {
        var text = ShowCode(window);

        text.Text = string.Empty;
        text.TextArea.Focus();
        Settle(window);

        return text;
    }

    private static void Type(MainWindow window, string said)
    {
        foreach (var letter in said)
        {
            window.KeyTextInput(letter.ToString());
            Dispatcher.UIThread.RunJobs();
        }

        Settle(window);
    }

    [AvaloniaFact]
    public void Inside_a_call_its_sockets_show_with_the_one_being_filled()
    {
        var window = Open();
        Empty(window);

        Type(window, "sine(amp: ");

        var signature = View(window).Signing.Showing.ShouldNotBeNull();
        signature.Sockets[signature.Active].Name.ShouldBe("amp");
        View(window).Signing.Popup.IsOpen.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Leaving_the_call_puts_the_signature_away()
    {
        var window = Open();
        Empty(window);

        Type(window, "sine(amp: 1) ");

        View(window).Signing.Showing.ShouldBeNull();
        View(window).Signing.Popup.IsOpen.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void The_signature_stands_aside_while_the_list_is_open()
    {
        var window = Open();
        Empty(window);

        Type(window, "sine(fr");

        View(window).Completing.Showing.ShouldNotBeNull();
        View(window).Signing.Showing.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Resting_on_a_word_says_what_it_is()
    {
        var window = Open();
        var text = Empty(window);

        text.Document.Insert(0, "sine(freq: 2) |> out.left");
        Settle(window);

        View(window).Hovering.Show(1, 3);
        View(window).Hovering.Showing.ShouldNotBeNull().Title.ShouldBe("sine · Sine (osc.sine)");

        View(window).Hovering.Show(1, 8);
        View(window).Hovering.Showing.ShouldNotBeNull().Title.ShouldStartWith("freq · input of Sine");
    }
}
