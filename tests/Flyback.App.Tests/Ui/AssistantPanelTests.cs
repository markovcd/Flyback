using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.App.Assist;
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
/// Mostly with no plugins, which is the state every machine is in until one is
/// installed. Where a provider is needed, one is handed in: the catalogue's
/// constructor is internal and this assembly is now named on it, because the
/// half of this panel that reacts to what a provider can do is the half that
/// went wrong, and it cannot be looked at in front of no provider. Driving an
/// actual run is still the plugin tests' job.
/// </remarks>
public class AssistantPanelTests : UiTest
{
    /// <summary>What the button shows when pressing it would ask.</summary>
    private const string Send = "⏎";

    private static Window Showing(PluginCatalog? plugins = null, AssistantSettings? saved = null)
    {
        var panel = new AssistantPanel(
            plugins ?? PluginCatalog.Empty,
            () => Presets.Plasma(NodeCatalog.BuiltIn),
            _ => { },
            (_, _) => { },
            saved);

        var window = Show(panel, 760);
        Settle(window);

        return window;
    }

    /// <summary>
    /// A catalogue holding one provider, which is the only way to see the half
    /// of this panel that reacts to what a provider can do.
    /// </summary>
    private static PluginCatalog With(IPatchAssistant assistant) =>
        new([], [], NodeCatalog.BuiltIn, [], [], [assistant]);

    /// <summary>The settings, in a window of their own, as opening them makes one.</summary>
    private static Window Settings(Window panel)
    {
        var host = new Window { Content = All<AssistantPanel>(panel).Single().SettingsSection() };

        host.Show();
        Settle(host);

        return host;
    }

    /// <summary>
    /// One provider that can see and one model that can hear, which is the shape
    /// every real provider here has: nothing does both.
    /// </summary>
    private sealed class Hearing : IPatchAssistant
    {
        public string Id => "hearing";

        public string Name => "Can hear";

        public int Priority => 0;

        public AssistantSchema Schema { get; } = new(
            "sees",
            [new AssistantModel("sees"), new AssistantModel("hears", Vision: false, Hearing: true)],
            "NONE",
            "none needed");

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
            throw new NotSupportedException("this one is only ever asked what it can do.");
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
    /// The model list is a set of suggestions, not a set of choices. The
    /// endpoint is a field — an OpenAI-shaped one reaches a dozen providers and
    /// a local runtime besides — so a name nobody wrote down here still has to
    /// be typeable, and an ordinary drop-down would make it not.
    /// </summary>
    [AvaloniaFact]
    public void The_model_box_takes_a_name_that_is_not_on_its_list()
    {
        var window = Showing();
        var panel = All<AssistantPanel>(window).Single();

        var host = new Window { Content = panel.SettingsSection() };
        host.Show();
        Settle(host);

        var box = All<ComboBox>(host).Single(c => c.Name == "model");

        box.IsEditable.ShouldBeTrue();

        box.Text = "something-nobody-here-has-heard-of";
        Settle(host);

        box.Text.ShouldBe("something-nobody-here-has-heard-of");
    }

    /// <summary>
    /// A setting that was on when the window closed is on, and usable, when it
    /// opens again.
    /// </summary>
    /// <remarks>
    /// The bug this was written for: the ear sat greyed out under a ticked box
    /// saying listening was on, and came right the instant the tick was touched.
    /// Two causes, either of which would do it on its own — the ear's state was
    /// worked out before the tick had been restored, and the handler that keeps
    /// the two in step was subscribed while building the settings window, which
    /// is not built until somebody opens one, long after the restoring is done.
    /// </remarks>
    [AvaloniaFact]
    public void An_ear_is_ready_to_change_the_moment_the_settings_are_opened()
    {
        var window = Showing(
            With(new Hearing()),
            new AssistantSettings { Provider = "hearing", Hearing = true, EarModel = "hears" });

        var host = Settings(window);
        var ear = All<ComboBox>(host).Single(c => c.Name == "ear");

        ear.IsVisible.ShouldBeTrue();
        ear.IsEnabled.ShouldBeTrue("listening is on, so the model doing it is a live choice");
        ear.SelectedItem.ShouldBe("hears");
    }

    /// <summary>
    /// And the other way round, so what is pinned above is the tick being read
    /// rather than the box simply always being on.
    /// </summary>
    [AvaloniaFact]
    public void An_ear_nobody_asked_for_is_shown_but_not_a_choice_yet()
    {
        var window = Showing(
            With(new Hearing()),
            new AssistantSettings { Provider = "hearing", Hearing = false });

        var host = Settings(window);
        var ear = All<ComboBox>(host).Single(c => c.Name == "ear");

        ear.IsVisible.ShouldBeTrue("the provider has one, so it is worth showing what it would be");
        ear.IsEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// A provider with nothing that can hear has nothing to offer here, and the
    /// tick above it is not a question either.
    /// </summary>
    [AvaloniaFact]
    public void A_provider_that_cannot_hear_at_all_offers_no_ear()
    {
        var window = Showing(PluginCatalog.Empty, new AssistantSettings { Hearing = true });

        var host = Settings(window);
        var ear = All<ComboBox>(host).Single(c => c.Name == "ear");

        ear.IsVisible.ShouldBeFalse();
    }

    /// <summary>
    /// Not knowing what a model accepts is not the same as knowing it refuses.
    /// With no provider installed nothing is known about anything, and the two
    /// switches stay the person's to set rather than being taken away.
    /// </summary>
    [AvaloniaFact]
    public void A_model_nobody_knows_anything_about_leaves_both_switches_alone()
    {
        var window = Showing();
        var panel = All<AssistantPanel>(window).Single();

        var host = new Window { Content = panel.SettingsSection() };
        host.Show();
        Settle(host);

        var switches = All<CheckBox>(host)
            .Where(c => c.Content is string content && content.Contains("Let it"))
            .ToArray();

        switches.Length.ShouldBe(2, "one for the picture and one for the sound");
        switches.ShouldAllBe(c => c.IsEnabled);
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
