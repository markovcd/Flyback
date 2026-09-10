using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Flyback.App.Assist;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The one button on the instruction box, which sends a message and stops the run
/// it started.
/// </summary>
/// <remarks>
/// Mostly with no plugins, which is the state every machine is in until one is
/// installed. Where a provider is needed one is handed in — the half of this panel
/// that reacts to what a provider can do cannot be looked at in front of none.
/// Driving an actual run is the plugin tests' job.
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
    /// The settings, as a provider that has already been configured leaves them.
    /// </summary>
    /// <remarks>
    /// The answers go in under the provider's own names, because that is the
    /// only shape the file has now — the App does not know a model from an
    /// endpoint, so it keeps a bag of strings per provider and hands it back
    /// (ADR-0069).
    /// </remarks>
    private static AssistantSettings Configured(string provider, params (string Key, string Value)[] answers)
    {
        var settings = new AssistantSettings { Provider = provider };

        settings.Remember(
            provider,
            new AssistantValues(answers.ToDictionary(answer => answer.Key, answer => answer.Value)));

        return settings;
    }

    /// <summary>
    /// A provider of the ordinary shape, which declares the form its schema
    /// declares. What differs between the ones below is the models, which is
    /// what the form is drawn from.
    /// </summary>
    private abstract class Provider(AssistantSchema schema) : IPatchAssistant
    {
        public abstract string Id { get; }

        public abstract string Name { get; }

        public int Priority => 0;

        public AssistantSchema Schema { get; } = schema;

        public AssistantCredential Credential => Schema.Credential;

        public IReadOnlyList<AssistantField> Form(AssistantValues values) => Schema.Form(values);

        public AssistantSenses Senses(AssistantValues values) => Schema.Senses(values);

        public virtual string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
            throw new NotSupportedException("this one is only ever asked what it can do.");
    }

    /// <summary>
    /// One provider that can see and one model that can hear, which is the shape
    /// every real provider here has: nothing does both.
    /// </summary>
    private sealed class Hearing() : Provider(new AssistantSchema(
        "sees",
        [new AssistantModel("sees"), new AssistantModel("hears", Vision: false, Hearing: true)],
        "NONE",
        "none needed"))
    {
        public override string Id => "hearing";

        public override string Name => "Can hear";
    }

    /// <summary>
    /// One provider whose model both sees and hears, which is what the Gemini
    /// adapter brought and nothing had before it.
    /// </summary>
    private sealed class Both() : Provider(new AssistantSchema(
        "both",
        [new AssistantModel("both", Hearing: true), new AssistantModel("deaf")],
        "NONE",
        "none needed"))
    {
        public override string Id => "both";

        public override string Name => "Sees and hears";
    }

    /// <summary>One with nothing that takes a sound, which most endpoints are.</summary>
    private sealed class Deaf() : Provider(new AssistantSchema(
        "quiet",
        [new AssistantModel("quiet")],
        "NONE",
        "none needed"))
    {
        public override string Id => "deaf";

        public override string Name => "Sees only";
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
    /// The rows come from the enum, so the box cannot offer a level that does not
    /// exist. What it can still do is offer the wrong ones: the setting is stored as
    /// the index of the row somebody picked, so the levels are named here in order.
    /// </summary>
    /// <remarks>
    /// Order is load-bearing and membership is not enough to pin: nothing refers to
    /// these members by name elsewhere, so dropping or swapping one relabels what
    /// was already saved without anything failing to compile.
    /// </remarks>
    [AvaloniaFact]
    public void The_effort_box_offers_exactly_the_levels_there_are()
    {
        var host = Settings(Showing(With(new Deaf())));

        var box = All<ComboBox>(host).Single(c => c.Name == AssistantSchema.EffortKey);
        var offered = ((IEnumerable<AssistantOption>)box.ItemsSource!).Select(o => o.Name).ToArray();

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
        var host = Settings(Showing(With(new Deaf())));

        var box = All<ComboBox>(host).Single(c => c.Name == AssistantSchema.ModelKey);

        box.IsEditable.ShouldBeTrue();

        box.Text = "something-nobody-here-has-heard-of";
        Settle(host);

        box.Text.ShouldBe("something-nobody-here-has-heard-of");
    }

    /// <summary>
    /// The settings open showing who is actually being talked to.
    /// </summary>
    /// <remarks>
    /// The choice is restored while the panel is built and the window is not
    /// built until somebody opens one, so the list has to exist before the row
    /// in it can be picked — a box with no rows cannot be told which to show,
    /// and the failure is a blank provider over a form belonging to one.
    /// </remarks>
    [AvaloniaFact]
    public void The_settings_open_on_the_provider_that_is_in_force()
    {
        var host = Settings(Showing(
            new PluginCatalog([], [], NodeCatalog.BuiltIn, [], [], [new Keyless(), new Both()]),
            Configured("both")));

        All<ComboBox>(host).Single(c => c.Name == "provider").SelectedIndex.ShouldBe(1);
    }

    /// <summary>
    /// The App draws what it is told and nothing else: a provider that declares
    /// no settings gets no rows, and one that declares five gets five.
    /// </summary>
    /// <remarks>
    /// The one test that is about the route rather than about any field on it.
    /// Nothing in the panel names a model, an endpoint or an ear any more — see
    /// ADR-0069 — so what is worth pinning is that the form is the declaration
    /// and not a layout somebody wrote out here.
    /// </remarks>
    [AvaloniaFact]
    public void The_form_is_what_the_provider_declared_and_nothing_else()
    {
        var declared = new Deaf().Form(AssistantValues.None).Select(field => field.Key).ToArray();

        var host = Settings(Showing(With(new Deaf())));

        var drawn = All<Control>(host)
            .Where(c => c.Name is { } name && declared.Contains(name))
            .Select(c => c.Name!)
            .ToArray();

        drawn.ShouldBe(declared, ignoreOrder: true);

        // And with nothing installed there is nothing to draw at all — only the
        // two the host owns, which are the provider list and the key.
        All<Control>(Settings(Showing()))
            .Select(c => c.Name)
            .ShouldNotContain(name => name != null && declared.Contains(name));
    }

    /// <summary>
    /// A setting that was on when the window closed is on, and usable, when it opens
    /// again.
    /// </summary>
    /// <remarks>
    /// The bug this was written for: the ear sat greyed out under a ticked box, and
    /// came right the instant the tick was touched. The form is now asked for afresh
    /// with everything already on it, so there is no order for the two to be
    /// restored in.
    /// </remarks>
    [AvaloniaFact]
    public void An_ear_is_ready_to_change_the_moment_the_settings_are_opened()
    {
        var host = Settings(Showing(
            With(new Hearing()),
            Configured("hearing", (AssistantSchema.HearingKey, "1"), (AssistantSchema.EarKey, "hears"))));

        var ear = All<ComboBox>(host).Single(c => c.Name == AssistantSchema.EarKey);

        ear.IsEnabled.ShouldBeTrue("listening is on, so the model doing it is a live choice");
        ((AssistantOption)ear.SelectedItem!).Id.ShouldBe("hears");
    }

    /// <summary>
    /// And the other way round, so what is pinned above is the tick being read
    /// rather than the box simply always being on.
    /// </summary>
    [AvaloniaFact]
    public void An_ear_nobody_asked_for_is_shown_but_not_a_choice_yet()
    {
        var host = Settings(Showing(With(new Hearing()), Configured("hearing")));

        var ear = All<ComboBox>(host).Single(c => c.Name == AssistantSchema.EarKey);

        ear.IsEnabled.ShouldBeFalse("nobody is listening, so there is nobody to choose");
    }

    /// <summary>
    /// A provider with nothing that can hear has nothing to offer here, and the
    /// tick above it is not a question either.
    /// </summary>
    [AvaloniaFact]
    public void A_provider_that_cannot_hear_at_all_offers_no_ear()
    {
        var host = Settings(Showing(
            With(new Deaf()),
            Configured("deaf", (AssistantSchema.HearingKey, "1"))));

        All<ComboBox>(host).ShouldNotContain(c => c.Name == AssistantSchema.EarKey);

        All<CheckBox>(host)
            .Single(c => c.Name == AssistantSchema.HearingKey)
            .IsEnabled.ShouldBeFalse("there is nothing to turn on");
    }

    /// <summary>
    /// A model that takes a sound itself is played the clip directly, so there
    /// is no second model and no question to put. The row goes rather than
    /// greying out — a disabled control asks somebody to work out why it is
    /// there, and this one has stopped meaning anything at all.
    /// </summary>
    [AvaloniaFact]
    public void A_model_that_hears_for_itself_needs_no_ear_chosen()
    {
        var host = Settings(Showing(
            With(new Both()),
            Configured("both", (AssistantSchema.ModelKey, "both"), (AssistantSchema.HearingKey, "1"))));

        All<ComboBox>(host).ShouldNotContain(c => c.Name == AssistantSchema.EarKey);
    }

    /// <summary>
    /// And it comes back for a model that cannot, which is what pins the row to
    /// the model in the box rather than to the provider alone.
    /// </summary>
    [AvaloniaFact]
    public void The_ear_returns_for_a_model_that_cannot_hear()
    {
        var host = Settings(Showing(
            With(new Both()),
            Configured("both", (AssistantSchema.ModelKey, "deaf"), (AssistantSchema.HearingKey, "1"))));

        All<ComboBox>(host).ShouldContain(c => c.Name == AssistantSchema.EarKey);
    }

    /// <summary>
    /// A model going from one that hears to one that does not takes the ear with
    /// it, without the window being reopened.
    /// </summary>
    /// <remarks>
    /// The declaration is asked for again after every change, which is the whole
    /// of how a form answers itself. Nothing in the panel worked this out — the
    /// provider left the ear out of the list it sent back.
    /// </remarks>
    [AvaloniaFact]
    public void Choosing_a_model_that_hears_takes_the_ear_away_where_it_stands()
    {
        var host = Settings(Showing(
            With(new Both()),
            Configured("both", (AssistantSchema.ModelKey, "deaf"), (AssistantSchema.HearingKey, "1"))));

        All<ComboBox>(host).ShouldContain(c => c.Name == AssistantSchema.EarKey);

        All<ComboBox>(host).Single(c => c.Name == AssistantSchema.ModelKey).Text = "both";
        Settle(host);

        All<ComboBox>(host).ShouldNotContain(c => c.Name == AssistantSchema.EarKey);
    }

    /// <summary>
    /// Not knowing what a model accepts is not the same as knowing it refuses. A
    /// name nobody wrote down leaves both switches the person's to set rather
    /// than taking one away on a guess.
    /// </summary>
    [AvaloniaFact]
    public void A_model_nobody_knows_anything_about_leaves_both_switches_alone()
    {
        var host = Settings(Showing(
            With(new Hearing()),
            Configured("hearing", (AssistantSchema.ModelKey, "something-nobody-wrote-down"))));

        var switches = All<CheckBox>(host)
            .Where(c => c.Name is AssistantSchema.VisionKey or AssistantSchema.HearingKey)
            .ToArray();

        switches.Length.ShouldBe(2, "one for the picture and one for the sound");
        switches.ShouldAllBe(c => c.IsEnabled);
    }

    /// <summary>
    /// Saving writes the provider that was picked while the window was open.
    /// Discarding puts back whichever one was in force before it opened —
    /// which is the point of the test, since picking a provider writes it to
    /// <see cref="AssistantSettings"/> straight away, for the form under it to
    /// follow.
    /// </summary>
    [AvaloniaFact]
    public void Discarding_settings_puts_back_the_provider_that_was_in_force()
    {
        var window = Showing(
            new PluginCatalog([], [], NodeCatalog.BuiltIn, [], [], [new Deaf(), new Both()]),
            Configured("deaf"));
        var panel = All<AssistantPanel>(window).Single();

        var host = Settings(window);
        var provider = All<ComboBox>(host).Single(c => c.Name == "provider");

        provider.SelectedIndex = 1;
        Settle(host);

        panel.DiscardSettings();

        provider.SelectedIndex.ShouldBe(0, "back to the one that was in force, not the one picked");
    }

    /// <summary>
    /// A key typed but never saved is not this program's to keep, so it has to
    /// be gone even from the box it was typed into — never mind the store.
    /// </summary>
    [AvaloniaFact]
    public void Discarding_settings_blanks_a_key_typed_but_not_saved()
    {
        var window = Showing(With(new Deaf()));
        var panel = All<AssistantPanel>(window).Single();

        var host = Settings(window);
        var key = All<TextBox>(host).Single(t => t.PasswordChar != default);

        key.Text = "sk-typed-but-not-saved";
        Settle(host);

        panel.DiscardSettings();

        key.Text.ShouldBeNullOrEmpty();
    }

    /// <summary>
    /// Ticking the box and closing some way other than Save leaves the setting
    /// as it was — the same rule <see cref="Discarding_settings_puts_back_the_provider_that_was_in_force"/>
    /// pins for the provider, here for the box that answers no test until it is
    /// looked for on its own.
    /// </summary>
    [AvaloniaFact]
    public void Discarding_settings_puts_back_whether_logging_was_on()
    {
        var window = Showing(With(new Deaf()), new AssistantSettings { LogConversations = false });
        var panel = All<AssistantPanel>(window).Single();

        var host = Settings(window);
        var logging = All<CheckBox>(host).Single(c => c.Content as string == "Log conversations to disk");

        logging.IsChecked = true;
        Settle(host);

        panel.DiscardSettings();

        logging.IsChecked.ShouldBe(false, "never saved, so still off");
    }

    /// <summary>
    /// Saving with the box ticked is what makes the setting stick — the other
    /// half of the discard test above.
    /// </summary>
    [AvaloniaFact]
    public void Saving_settings_keeps_whether_logging_was_turned_on()
    {
        var saved = new AssistantSettings();
        var window = Showing(With(new Deaf()), saved);

        var host = Settings(window);
        var logging = All<CheckBox>(host).Single(c => c.Content as string == "Log conversations to disk");

        logging.IsChecked = true;
        Settle(host);

        All<Button>(host)
            .Single(b => b.Content as string == "Save")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(host);

        saved.LogConversations.ShouldBeTrue();
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

    /// <summary>
    /// The footer says what is true now, not what was true the last time it had bad
    /// news.
    /// </summary>
    /// <remarks>
    /// The bug this was written for: the amber branch wrote the excuse and the one
    /// under it wrote only the color, so a panel that had once had no key went on
    /// saying "No key yet" over every key that arrived afterwards. A key arriving
    /// now hides the footer outright, so the equivalent bug would be the excuse text
    /// surviving once there is nothing left to excuse.
    /// </remarks>
    [AvaloniaFact]
    public void The_footer_stops_saying_what_was_wrong_once_it_is_right()
    {
        var window = Showing(
            new PluginCatalog([], [], NodeCatalog.BuiltIn, [], [], [new Keyless(), new Both()]),
            new AssistantSettings { Provider = "keyless" });

        var footer = All<TextBlock>(window).Single(t => t.Name == "footer");

        footer.IsVisible.ShouldBeTrue("there is no key, and the footer is where that is said");
        footer.Text.ShouldBe(Keyless.Excuse);

        var host = Settings(window);

        All<ComboBox>(host).Single(c => c.Name == "provider").SelectedIndex = 1;
        Settle(host);
        Settle(window);

        // Nothing left to excuse, so the footer drops out rather than standing
        // in grey over stale amber text.
        footer.IsVisible.ShouldBeFalse();
    }

    /// <summary>One that is never ready, which is what a provider is until a key turns up.</summary>
    private sealed class Keyless() : Provider(new AssistantSchema(
        "only",
        [new AssistantModel("only")],
        "NONE",
        "none needed"))
    {
        internal const string Excuse = "No key yet — put one in Settings.";

        public override string Id => "keyless";

        public override string Name => "Wants a key";

        public override string? Unavailable(AssistantConfig config) => Excuse;
    }
}
