using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The one button on the instruction box, which sends a message and stops the
/// run it started.
/// </summary>
/// <remarks>
/// Built with no plugins, which is the state every machine is in until one is
/// installed and the only one a test can put this panel in: the catalogue's
/// constructor is internal to the plugin assembly, so a fake assistant cannot be
/// handed to it from here. That still covers the claim worth making about a
/// button — that it is dead when pressing it could do nothing — and leaves the
/// asking itself to the plugin tests, where a real run can be driven.
/// </remarks>
public class AssistantPanelTests : UiTest
{
    /// <summary>What the button shows when pressing it would ask.</summary>
    private const string Send = "⏎";

    private static Window Showing()
    {
        var panel = new AssistantPanel(
            PluginCatalog.Empty,
            () => Presets.Plasma(NodeCatalog.BuiltIn),
            _ => { },
            (_, _) => { });

        var window = Show(panel, 760);
        Settle(window);

        return window;
    }

    private static Button SendButton(Window window) =>
        All<Button>(window).Single(b => b.Content as string == Send);

    /// <summary>The message box, told apart from the key field by taking newlines.</summary>
    private static TextBox Instruction(Window window) =>
        All<TextBox>(window).Single(b => b.AcceptsReturn);

    private static Rect On(Window window, Visual control) =>
        new(
            control.TranslatePoint(default, window) ?? throw new InvalidOperationException("not in this window"),
            control.Bounds.Size);

    /// <summary>
    /// The settings window, opened and closed three times. These are the panel's
    /// own controls lent to a window rather than a set built for it, which is
    /// what makes opening it twice worth a test: a control has one parent, and
    /// the second window has to be able to take one the first had.
    /// </summary>
    [AvaloniaFact]
    public void The_settings_section_survives_being_shown_more_than_once()
    {
        var window = Showing();
        var panel = All<AssistantPanel>(window).Single();

        Control? first = null;

        for (var opening = 0; opening < 3; opening++)
        {
            var section = panel.SettingsSection();

            first ??= section;
            section.ShouldBeSameAs(first, "the same controls each time, not a fresh set");

            var dialog = new Window { Content = section };
            dialog.Show();
            Settle(dialog);
            dialog.Close();
        }
    }

    /// <summary>
    /// The rows come from the enum, so the box cannot offer a level that does
    /// not exist. What it can still do is offer the wrong ones: the setting is
    /// stored as the index of the row somebody picked, so the levels are named
    /// here, in order, rather than compared against the enum they came from.
    /// </summary>
    /// <remarks>
    /// Order is load-bearing and membership is not enough to pin. Nothing refers
    /// to these members by name anywhere else, so dropping one as unused — or
    /// swapping two — shifts every value below it and relabels what was already
    /// saved, without anything failing to compile.
    /// </remarks>
    [AvaloniaFact]
    public void The_effort_box_offers_exactly_the_levels_there_are()
    {
        var window = Showing();
        var panel = All<AssistantPanel>(window).Single();

        var host = new Window { Content = panel.SettingsSection() };
        host.Show();
        Settle(host);

        var box = All<ComboBox>(host).Single(c => c.Name == "effort");
        var offered = ((IEnumerable<string>)box.ItemsSource!).ToArray();

        offered.ShouldBe(["Low", "Medium", "High"]);
    }

    /// <summary>
    /// Saving is the end of the errand, so the window it was done in goes with
    /// it — and only that one. The panel lives in a window of its own, and the
    /// day something shows the settings there, closing it would close the
    /// program.
    /// </summary>
    /// <remarks>
    /// The dismissal is exercised rather than the Save button that calls it,
    /// because pressing Save writes the settings file this machine actually
    /// uses. What is worth a test here is finding the right window, which is
    /// the half that has somewhere to go wrong.
    /// </remarks>
    [AvaloniaFact]
    public void Saving_closes_the_window_the_settings_were_shown_in()
    {
        var window = Showing();
        var panel = All<AssistantPanel>(window).Single();

        var dialog = new Window
        {
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = panel.SettingsSection(),
        };

        dialog.Show();
        Settle(dialog);

        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        panel.DismissSettings();

        closed.ShouldBeTrue();
        window.IsVisible.ShouldBeTrue("the window the panel itself lives in stays where it is");
    }

    /// <summary>Asked for when nothing is showing them, which is not an error.</summary>
    [AvaloniaFact]
    public void Dismissing_settings_nobody_is_showing_does_nothing()
    {
        var window = Showing();
        var panel = All<AssistantPanel>(window).Single();

        Should.NotThrow(() => panel.DismissSettings());

        panel.SettingsSection();
        Should.NotThrow(() => panel.DismissSettings());

        window.IsVisible.ShouldBeTrue();
    }

    /// <summary>
    /// In the box rather than beside it, and in the corner one finishes typing
    /// nearest. The box keeps a strip of padding along its bottom for it, so
    /// this is over the padding rather than over anything anybody wrote.
    /// </summary>
    [AvaloniaFact]
    public void The_button_sits_in_the_bottom_right_of_the_instruction_box()
    {
        var window = Showing();

        var box = On(window, Instruction(window));
        var button = On(window, SendButton(window));

        button.Width.ShouldBeLessThan(box.Width / 4, "it is a small square, not a bar");
        button.Height.ShouldBeLessThan(box.Height);

        box.Contains(button).ShouldBeTrue("the button is inside the box it belongs to");

        (box.Right - button.Right).ShouldBeLessThan(12, "hard against the right edge");
        (box.Bottom - button.Bottom).ShouldBeLessThan(12, "and the bottom one");
    }

    /// <summary>
    /// Which is the half of this the keystroke never had: pressing Enter with an
    /// empty box, or with no assistant installed, did nothing and said nothing
    /// about why.
    /// </summary>
    [AvaloniaFact]
    public void The_button_is_dead_while_there_is_nothing_to_send()
    {
        var window = Showing();
        var send = SendButton(window);

        send.IsEnabled.ShouldBeFalse("the box is empty");

        Instruction(window).Text = "a slow drifting field of blue";
        Settle(window);

        send.IsEnabled.ShouldBeFalse("and there is no assistant installed to send it to");
        ToolTip.GetTip(send).ShouldNotBeNull("which the button says when hovered");
    }
}
