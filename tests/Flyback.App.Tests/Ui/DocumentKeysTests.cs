using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Ctrl+O and Ctrl+S, which the toolbar's two buttons had to themselves.
/// </summary>
/// <remarks>
/// No picker opens headless, so what is checked is this side of one: that the
/// window claims the key rather than letting it fall through to whatever else
/// wanted it, and that opening still asks about work it would lose — which is the
/// whole reason the key goes through the same call the button does.
/// </remarks>
public class DocumentKeysTests : UiTest
{

    /// <summary>Whether the window dealt with the keystroke itself.</summary>
    private static bool Claims(MainWindow window, PhysicalKey key)
    {
        var handled = false;

        // Class handlers run before instance ones on the same element, so the
        // window's own OnKeyDown has already had this by the time it arrives.
        window.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) => handled = e.Handled,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        window.KeyPressQwerty(key, RawInputModifiers.Control);
        Settle(window);

        return handled;
    }

    [AvaloniaFact]
    public void Saving_is_on_the_keyboard()
    {
        Claims(Open(), PhysicalKey.S).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Opening_is_on_the_keyboard()
    {
        Claims(Open(), PhysicalKey.O).ShouldBeTrue();
    }

    /// <summary>
    /// The question the Open button asks is the key's too. Reaching the picker
    /// without it would be a patch lost to a keystroke.
    /// </summary>
    [AvaloniaFact]
    public void Opening_asks_about_a_patch_it_would_replace()
    {
        var window = Open();

        // A real edit: the history compares snapshots, so announcing a change is
        // not enough to make there be one.
        All<NodeEditor>(window).Single().Edits.AddNode("value").ShouldNotBeNull();
        Settle(window);

        window.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.Control);

        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);

        var asking = All<ModalOverlay>(window).ShouldHaveSingleItem();

        // Answered rather than left standing, so the window is not disposed with
        // a question on it that nothing ever replied to.
        All<Button>(asking).Single(b => b.Content as string == "Cancel")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Dispatcher.UIThread.RunJobs();
    }
}
