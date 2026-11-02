using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using Flyback.Editor.Composition;
using Flyback.Editor.Hubs;
using Shouldly;

namespace Flyback.Editor.Tests.Controls;

/// <summary>The list of what may be typed at the caret, popped up as the text is typed.</summary>
public class SourceViewCompletionTests : SourceViewTest
{
    private static SourceCompletion Completing(MainWindow window) => All<SourceView>(window).Single().Completing;

    /// <summary>An empty text with the caret in it, ready to be typed into.</summary>
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

    private static string[] Labels(MainWindow window) =>
        [.. Completing(window).Showing.ShouldNotBeNull("the list should be open").Select(c => c.Label)];

    [AvaloniaFact]
    public void A_bracket_after_a_module_lists_its_sockets()
    {
        var window = Open();
        Empty(window);

        Type(window, "sine(");

        Labels(window).ShouldContain("freq");
        Labels(window).ShouldContain("amp");
    }

    [AvaloniaFact]
    public void Choosing_a_socket_writes_it_with_its_colon()
    {
        var window = Open();
        var text = Empty(window);

        Type(window, "sine(fr");

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(window);

        text.Text.ShouldBe("sine(freq: ");
        Completing(window).Showing.ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_new_name_or_a_number_brings_nothing_up()
    {
        var window = Open();
        Empty(window);

        Type(window, "let wob");
        Completing(window).Showing.ShouldBeNull();

        Type(window, "ble = sine(freq: 2");
        Completing(window).Showing.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Control_space_lists_what_may_start_a_line()
    {
        var window = Open();
        var text = Empty(window);

        text.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Space,
            KeyModifiers = KeyModifiers.Control,
        });

        Settle(window);

        Labels(window).ShouldContain("let");
        Labels(window).ShouldContain("sine");
    }

    [AvaloniaFact]
    public void A_dot_after_out_lists_the_Outputs_sockets()
    {
        var window = Open();
        var text = Empty(window);

        text.Document.Insert(0, "sine(freq: 2) |> out");
        text.CaretOffset = text.Document.TextLength;
        Settle(window);

        Type(window, ".");

        Labels(window).ShouldContain("left");
        Labels(window).ShouldContain("color");
    }
}
