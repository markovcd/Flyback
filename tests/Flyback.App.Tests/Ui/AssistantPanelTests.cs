using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Flyback.App.Assist;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Secrets;
using Flyback.Plugins.Settings;
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
public class AssistantPanelTests : UiTest, IDisposable
{
    /// <summary>What the button shows when pressing it would ask.</summary>
    private const string Send = "⏎";

    /// <summary>
    /// Where a panel under test writes settings to, so pressing the real Save
    /// button in a test cannot land on the machine's own <c>assistant.json</c>.
    /// </summary>
    private readonly string settingsPath = Path.Combine(
        Path.GetTempPath(),
        "flyback-panel-settings-" + Guid.NewGuid().ToString("N"),
        "assistant.json");

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(settingsPath);

        if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private Window Showing(PluginCatalog? plugins = null, AssistantSettings? saved = null, Action<string, string?>? report = null)
    {
        var panel = new AssistantPanel(
            plugins ?? PluginCatalog.Empty,
            () => Presets.Plasma(NodeCatalog.BuiltIn),
            _ => { },
            report ?? ((_, _) => { }),
            saved,
            settingsPath);

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

    // --- a conversation saved with the patch ----------------------------------

    /// <summary>
    /// A panel over one patch that stays the same object, as the canvas's does
    /// until somebody changes it — which is what a conversation saved with the
    /// patch is checked against.
    /// </summary>
    private (Window Window, AssistantPanel Panel) Over(Patch patch)
    {
        var panel = new AssistantPanel(PluginCatalog.Empty, () => patch, _ => { }, (_, _) => { }, null, settingsPath);
        var window = Show(panel, 760);

        Settle(window);

        return (window, panel);
    }

    private static string Saved(params TranscriptLine[] transcript) => new SavedConversation(
        "gemini",
        SavedConversation.SettingsOf(SettingValues.None),
        1,
        new WorkbenchState("""{"nodes":[]}""", """{"nodes":[]}""", new Dictionary<string, Guid>(), 1, 2),
        null,
        transcript).ToJson();

    private static List<string?> Shown(Window window) =>
        [.. All<SelectableTextBlock>(window).Select(block => block.Text)];

    [AvaloniaFact]
    public void A_conversation_saved_with_a_patch_is_shown_when_the_patch_opens()
    {
        var (window, panel) = Over(new Patch());

        panel.Open(Saved(
            new TranscriptLine(Voice.You, "make a hard techno patch"),
            new TranscriptLine(Voice.Said, "Here is a kick at 150 bpm.")));
        Settle(window);

        Shown(window).ShouldContain("make a hard techno patch");
        Shown(window).ShouldContain("Here is a kick at 150 bpm.");
        panel.ConversationUnsaved.ShouldBeFalse("nothing has been said since it was opened");
    }

    /// <summary>
    /// Saved again with the patch for as long as it is still about that patch. An
    /// edit before the first message ends it, as an edit under a run does.
    /// </summary>
    [AvaloniaFact]
    public void A_conversation_opened_with_a_patch_goes_with_it_until_the_patch_changes()
    {
        var patch = new Patch();
        var (_, panel) = Over(patch);

        panel.Open(Saved(new TranscriptLine(Voice.You, "hello")));
        panel.ConversationToSave().ShouldNotBeNull();

        patch.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0));

        panel.ConversationToSave().ShouldBeNull();
    }

    [AvaloniaFact]
    public void A_document_with_no_conversation_empties_the_panel()
    {
        var (window, panel) = Over(new Patch());

        panel.Open(Saved(new TranscriptLine(Voice.You, "hello")));
        panel.Open(null);
        Settle(window);

        Shown(window).ShouldNotContain("hello");
        panel.ConversationToSave().ShouldBeNull();
    }

    [AvaloniaFact]
    public void Something_that_is_not_a_conversation_opens_as_none()
    {
        var (window, panel) = Over(new Patch());

        panel.Open("not a conversation");
        Settle(window);

        panel.ConversationToSave().ShouldBeNull();
        Shown(window).ShouldBeEmpty();
    }

    private static Button Fresh(Window window) => All<Button>(window).Single(b => b.Name == "fresh");

    [AvaloniaFact]
    public void There_is_no_new_conversation_to_start_with_none_to_set_aside()
    {
        var (window, _) = Over(new Patch());

        Fresh(window).IsEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// Set aside, and no longer what saving the patch writes: the new one is, and
    /// so far there is none. Nothing new has been said, so there is nothing to lose.
    /// </summary>
    [AvaloniaFact]
    public void A_new_conversation_sets_the_one_on_screen_aside()
    {
        var (window, panel) = Over(new Patch());

        panel.Open(Saved(new TranscriptLine(Voice.You, "make a hard techno patch")));
        Settle(window);

        Fresh(window).IsEnabled.ShouldBeTrue();
        Fresh(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        Shown(window).ShouldNotContain("make a hard techno patch");
        panel.ConversationToSave().ShouldBeNull();
        panel.ConversationUnsaved.ShouldBeFalse();
        Fresh(window).IsEnabled.ShouldBeFalse();
    }

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
            new SettingValues(answers.ToDictionary(answer => answer.Key, answer => answer.Value)));

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

        // Through Surveyed, as both shipped adapters declare themselves: a
        // survey of the endpoint replaces the written-down models, and a fake
        // that skipped it would be a fake nothing could probe.
        public IReadOnlyList<SettingField> Form(SettingValues values) => Schema.Surveyed(values).Form(values);

        public AssistantSenses Senses(SettingValues values) => Schema.Surveyed(values).Senses(values);

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
        var host = Settings(Showing(With(new Deaf()), Configured("deaf")));

        var box = All<ComboBox>(host).Single(c => c.Name == AssistantSchema.EffortKey);
        var offered = ((IEnumerable<SettingOption>)box.ItemsSource!).Select(o => o.Name).ToArray();

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
        var host = Settings(Showing(With(new Deaf()), Configured("deaf")));

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

        // Row 0 is "None", so a real provider sits one row below its own place
        // in the catalogue — "both" is the second provider offered.
        All<ComboBox>(host).Single(c => c.Name == "provider").SelectedIndex.ShouldBe(2);
    }

    /// <summary>
    /// "None" is offered whether or not anything is installed, so leaving a
    /// provider is a choice made the same way picking one is — not something
    /// that only happens by there being nothing else on the list.
    /// </summary>
    [AvaloniaFact]
    public void None_is_offered_even_when_a_provider_is_installed()
    {
        var host = Settings(Showing(With(new Deaf()), Configured("deaf")));
        var provider = All<ComboBox>(host).Single(c => c.Name == "provider");

        ((IEnumerable<string>)provider.ItemsSource!).First().ShouldBe("None");
        provider.SelectedIndex.ShouldBe(1, "the provider already configured, not None");
    }

    /// <summary>
    /// Picking None while a provider is installed is not the same as there
    /// being nothing to pick — the footer has to say which is true, since
    /// "put a key in Settings" is not the right advice for someone who has not
    /// chosen anybody to give a key to.
    /// </summary>
    [AvaloniaFact]
    public void Picking_none_leaves_the_footer_naming_the_actual_reason()
    {
        var window = Showing(With(new Deaf()), Configured("deaf"));

        Instruction(window).Text = "a slow drifting field of blue";
        Settle(window);

        var host = Settings(window);

        All<ComboBox>(host).Single(c => c.Name == "provider").SelectedIndex = 0;
        Settle(host);
        Settle(window);

        SendButton(window).IsEnabled.ShouldBeFalse("nobody is chosen to send it to");

        var footer = All<TextBlock>(window).Single(t => t.Name == "footer");
        footer.Text.ShouldBe("No assistant is selected. Pick one in Settings.");
    }

    /// <summary>
    /// The key, "keep this key" and "forget key" rows are all about a provider
    /// that has something to hold a key for. With None picked there is nobody
    /// to hold one, so the whole row of them hides rather than sitting there
    /// asking to be filled in for nobody.
    /// </summary>
    [AvaloniaFact]
    public void The_key_controls_hide_once_none_is_picked()
    {
        var host = Settings(Showing(With(new Deaf()), Configured("deaf")));

        var key = All<TextBox>(host).Single(t => t.PasswordChar != default);
        var keep = All<CheckBox>(host).Single(c => c.Content as string == "Keep this key");
        var forgetButton = All<Button>(host).Single(b => b.Content as string == "Forget key");

        key.IsEffectivelyVisible.ShouldBeTrue("deaf is picked, so there is a key to ask about");

        All<ComboBox>(host).Single(c => c.Name == "provider").SelectedIndex = 0;
        Settle(host);

        key.IsEffectivelyVisible.ShouldBeFalse("None takes no key");
        keep.IsEffectivelyVisible.ShouldBeFalse("nor is there one to keep");
        forgetButton.IsEffectivelyVisible.ShouldBeFalse("nor one to forget");

        // Logging is a choice about this machine, not about whoever is picked
        // (ADR-0034), so it stays put whether or not anybody is.
        All<CheckBox>(host).Single(c => c.Content as string == "Log conversations to disk")
            .IsEffectivelyVisible.ShouldBeTrue();
    }

    /// <summary>
    /// Saving with None picked is what makes leaving stick — the same rule
    /// <see cref="Saving_settings_keeps_whether_logging_was_turned_on"/> pins
    /// for a checkbox, here for the provider itself.
    /// </summary>
    [AvaloniaFact]
    public void Saving_none_clears_the_provider_setting()
    {
        var saved = Configured("deaf");
        var window = Showing(With(new Deaf()), saved);
        var host = Settings(window);

        All<ComboBox>(host).Single(c => c.Name == "provider").SelectedIndex = 0;
        Settle(host);

        All<AssistantPanel>(window).Single().SaveSettings();
        Settle(host);

        saved.Provider.ShouldBeEmpty();
    }

    /// <summary>
    /// A saved id nothing here answers to is not the same as never having
    /// chosen at all, but the two must not be told apart by which other
    /// provider happens to be installed: falling back to whichever one that is
    /// would send a message to somebody the person never picked.
    /// </summary>
    [AvaloniaFact]
    public void A_provider_no_longer_installed_falls_back_to_none_not_to_another_one()
    {
        var window = Showing(With(new Both()), new AssistantSettings { Provider = "gone" });

        Instruction(window).Text = "a slow drifting field of blue";
        Settle(window);

        SendButton(window).IsEnabled.ShouldBeFalse("nothing was chosen, so nothing is guessed at");

        var footer = All<TextBlock>(window).Single(t => t.Name == "footer");
        footer.Text.ShouldBe("No assistant is selected. Pick one in Settings.");
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
        var declared = new Deaf().Form(SettingValues.None).Select(field => field.Key).ToArray();

        var host = Settings(Showing(With(new Deaf()), Configured("deaf")));

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
        ((SettingOption)ear.SelectedItem!).Id.ShouldBe("hears");
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

        // Row 0 is "None", row 1 is "deaf" (already in force) and row 2 is "both".
        provider.SelectedIndex = 2;
        Settle(host);

        panel.DiscardSettings();

        provider.SelectedIndex.ShouldBe(1, "back to the one that was in force, not the one picked");
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

        All<AssistantPanel>(window).Single().SaveSettings();
        Settle(host);

        saved.LogConversations.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_turn_limit_shows_what_was_saved_and_keeps_what_is_saved()
    {
        var saved = new AssistantSettings { TurnLimit = 20 };
        var window = Showing(With(new Deaf()), saved);
        var panel = All<AssistantPanel>(window).Single();

        var host = Settings(window);
        var turns = All<NumericUpDown>(host).Single(c => c.Name == "turnLimit");

        turns.Value.ShouldBe(20);

        turns.Value = 40;
        Settle(host);

        saved.TurnLimit.ShouldBe(20, "nothing is kept until Save");

        panel.SaveSettings();
        Settle(host);

        saved.TurnLimit.ShouldBe(40);
    }

    [AvaloniaFact]
    public void The_briefing_budget_shows_what_was_saved_and_keeps_what_is_saved()
    {
        var saved = new AssistantSettings { ProseBudget = 90_000 };
        var window = Showing(With(new Deaf()), saved);
        var panel = All<AssistantPanel>(window).Single();

        var host = Settings(window);
        var budget = All<NumericUpDown>(host).Single(c => c.Name == "proseBudget");

        budget.Value.ShouldBe(90_000);

        budget.Value = 50_000;
        Settle(host);

        saved.ProseBudget.ShouldBe(90_000, "nothing is kept until Save");

        panel.SaveSettings();
        Settle(host);

        saved.ProseBudget.ShouldBe(50_000);
    }

    /// <summary>The list is somebody's to edit, so the settings say where it is.</summary>
    [AvaloniaFact]
    public void The_settings_say_where_the_priority_list_is()
    {
        var window = Showing(With(new Deaf()));
        var host = Settings(window);

        All<SelectableTextBlock>(host).Single(t => t.Name == "priorityFile").Text
            .ShouldBe(Path.Combine(Path.GetDirectoryName(settingsPath)!, "priority-modules.txt"));
    }

    [AvaloniaFact]
    public void Past_the_budget_the_modules_off_the_list_are_undescribed()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(settingsPath)!, "priority-modules.txt"), "osc.string");

        var window = Showing(
            With(new Deaf()),
            new AssistantSettings { Provider = "deaf", ProseBudget = AssistantSettings.LeastProse });
        var panel = All<AssistantPanel>(window).Single();

        panel.Undescribed.ShouldContain("scan");
        panel.Undescribed.ShouldNotContain("osc.string");
    }

    /// <summary>With nobody to tell, nothing is being left out of what they are told.</summary>
    [AvaloniaFact]
    public void With_no_provider_nothing_is_undescribed()
    {
        var window = Showing(With(new Deaf()), new AssistantSettings { ProseBudget = AssistantSettings.LeastProse });

        All<AssistantPanel>(window).Single().Undescribed.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Saving_a_budget_that_fits_takes_the_marks_away()
    {
        var saved = new AssistantSettings { Provider = "deaf", ProseBudget = AssistantSettings.LeastProse };
        var window = Showing(With(new Deaf()), saved);
        var panel = All<AssistantPanel>(window).Single();
        var changed = 0;

        panel.UndescribedChanged += (_, _) => changed++;
        panel.Undescribed.ShouldNotBeEmpty();

        var host = Settings(window);

        All<NumericUpDown>(host).Single(c => c.Name == "proseBudget").Value = AssistantSettings.DefaultProseBudget;
        panel.SaveSettings();
        Settle(host);

        panel.Undescribed.ShouldBeEmpty();
        changed.ShouldBe(1);
    }

    /// <summary>Closing some way other than Save puts the box back to what was saved.</summary>
    [AvaloniaFact]
    public void Discarding_puts_the_turn_limit_back()
    {
        var saved = new AssistantSettings { TurnLimit = 20 };
        var window = Showing(With(new Deaf()), saved);
        var panel = All<AssistantPanel>(window).Single();

        var host = Settings(window);
        var turns = All<NumericUpDown>(host).Single(c => c.Name == "turnLimit");

        turns.Value = 5;
        panel.DiscardSettings();
        Settle(host);

        turns.Value.ShouldBe(20);
        saved.TurnLimit.ShouldBe(20);
    }

    /// <summary>
    /// "Key saved" is news about something that just happened, not a standing
    /// description of the key's state — a second Save with nothing typed and
    /// the box still ticked has nothing new to report.
    /// </summary>
    [AvaloniaFact]
    public void Saving_again_with_nothing_changed_does_not_repeat_the_key_saved_message()
    {
        var store = new FakeStore();
        var plugins = new PluginCatalog([], [], NodeCatalog.BuiltIn, [], [], [new Deaf()], [store]);
        var messages = new List<string>();

        var window = Showing(plugins, new AssistantSettings(), (message, _) => messages.Add(message));
        var panel = All<AssistantPanel>(window).Single();

        var host = Settings(window);

        All<ComboBox>(host).Single(c => c.Name == "provider").SelectedIndex = 1;
        Settle(host);

        var key = All<TextBox>(host).Single(t => t.PasswordChar != default);
        var keep = All<CheckBox>(host).Single(c => c.Content as string == "Keep this key");

        key.Text = "sk-typed";
        keep.IsChecked = true;
        Settle(host);

        panel.SaveSettings();
        Settle(host);

        messages.ShouldHaveSingleItem();
        messages[0].ShouldContain("Key saved");

        panel.SaveSettings();
        Settle(host);

        messages.ShouldHaveSingleItem("nothing about the key changed on the second save");
    }

    // --- probing the endpoint -------------------------------------------------

    /// <summary>
    /// One that can be asked what it offers, which is the half of the settings
    /// the probe button is for. It answers from here rather than over a network,
    /// and keeps what it was asked with.
    /// </summary>
    private sealed class Surveying(Func<CancellationToken, Task<IReadOnlyList<ModelReport>>>? answering = null)
        : Provider(new AssistantSchema("quiet", [new AssistantModel("quiet")], "NONE", "none needed")), IModelSurvey
    {
        public override string Id => "surveying";

        public override string Name => "Can be asked";

        /// <summary>What the last probe went out with, which is the whole question.</summary>
        public AssistantConfig? Asked { get; private set; }

        /// <summary>
        /// What it was asked about, once this has worked out what the caller
        /// meant. Resolved here because that is where it is resolved for real:
        /// the window says "the chosen one" and the provider says which.
        /// </summary>
        public SurveyOptions? Wanted { get; private set; }

        public Task<IReadOnlyList<ModelReport>> Survey(
            AssistantConfig config,
            SurveyOptions options,
            IProgress<string>? said = null,
            CancellationToken cancel = default)
        {
            Asked = config;
            Wanted = Schema.Asking(options, config.Values);

            // An endpoint that has whatever it was asked about, which takes both
            // kinds of input — so a report landing on the form is visible in it.
            return answering is null
                ? Task.FromResult<IReadOnlyList<ModelReport>>(
                    [.. (Wanted.Only ?? []).Select(id => new ModelReport(id) { Hearing = true })])
                : answering(cancel);
        }
    }

    /// <summary>A survey that answers only by being stopped.</summary>
    private static async Task<IReadOnlyList<ModelReport>> Never(CancellationToken cancel)
    {
        await Task.Delay(Timeout.Infinite, cancel);

        return [];
    }

    private static Button ProbeButton(Window host) =>
        All<Button>(host).Single(button => button.Name == "probe");

    private static string ProbeNote(Window host) =>
        All<TextBlock>(host).Single(block => block.Name == "probeNote").Text ?? string.Empty;

    /// <summary>The key field, told apart from the message box by hiding what is typed.</summary>
    private static TextBox KeyBox(Window host) =>
        All<TextBox>(host).Single(box => box.PasswordChar != default);

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// The point of the button: the settings window is where somebody finds out
    /// whether a key and an endpoint work at all, and they have not been saved
    /// yet because that is what they are trying to find out.
    /// </summary>
    [AvaloniaFact]
    public void The_probe_goes_out_with_what_is_on_the_form_rather_than_what_was_saved()
    {
        var provider = new Surveying();
        var host = Settings(Showing(
            With(provider),
            Configured("surveying", (AssistantSchema.ModelKey, "saved-model"))));

        KeyBox(host).Text = "sk-typed";
        All<ComboBox>(host).Single(box => box.Name == AssistantSchema.ModelKey).Text = "typed-model";
        Settle(host);

        Click(ProbeButton(host));
        Settle(host);

        provider.Asked.ShouldNotBeNull();
        provider.Asked!.ApiKey.ShouldBe("sk-typed");
        provider.Asked.Values.Text(AssistantSchema.ModelKey).ShouldBe("typed-model");
    }

    /// <summary>
    /// One model, because the button is billed by the question and the one worth
    /// asking about is the one that is going to be used.
    /// </summary>
    /// <remarks>
    /// Which setting names it is the provider's business (ADR-0069), so what the
    /// window asks for is "the chosen one" and the provider resolves it — which
    /// is what the fake does here, through the same helper both adapters use.
    /// </remarks>
    [AvaloniaFact]
    public void The_probe_asks_about_the_model_on_the_form_and_no_others()
    {
        var provider = new Surveying();
        var host = Settings(Showing(With(provider), Configured("surveying")));

        KeyBox(host).Text = "sk-typed";
        All<ComboBox>(host).Single(box => box.Name == AssistantSchema.ModelKey).Text = "the-one-i-picked";
        Settle(host);

        Click(ProbeButton(host));
        Settle(host);

        provider.Wanted.ShouldNotBeNull();
        provider.Wanted!.Chosen.ShouldBeTrue();
        provider.Wanted.Only.ShouldBe(["the-one-i-picked"]);
        provider.Wanted.All.ShouldBeFalse();
    }

    /// <summary>A survey of two models, as a full probe leaves one behind.</summary>
    private static string Listed(params string[] models) =>
        Survey.Write(models.Select(id => new ModelReport(id)));

    /// <summary>
    /// What a probe found is on the form like anything else on it, and this
    /// window keeps nothing until Save. What it did not ask about is left alone:
    /// one model answering says nothing about the rest of the list.
    /// </summary>
    [AvaloniaFact]
    public void What_the_probe_finds_is_written_over_the_model_it_is_about()
    {
        var saved = Configured(
            "surveying",
            (Survey.Key, Listed("first", "second")),
            (AssistantSchema.ModelKey, "second"));

        var window = Showing(With(new Surveying()), saved);
        var panel = All<AssistantPanel>(window).Single();
        var host = Settings(window);

        KeyBox(host).Text = "sk-typed";
        Settle(host);

        Click(ProbeButton(host));
        Settle(host);

        var models = All<ComboBox>(host).Single(box => box.Name == AssistantSchema.ModelKey);

        ((IEnumerable<string>)models.ItemsSource!).ShouldBe(["first", "second"], "the list is not rebuilt");
        Survey.Read(saved.Of("surveying").Text(Survey.Key))
            .ShouldAllBe(model => !model.Hearing, "nothing is kept until Save");

        panel.SaveSettings();

        var written = Survey.Read(saved.Of("surveying").Text(Survey.Key));

        written.Select(model => model.Id).ShouldBe(["first", "second"]);
        written.Single(model => model.Id == "second").Hearing.ShouldBeTrue("this is the one that answered");
        written.Single(model => model.Id == "first").Hearing.ShouldBeFalse("and this one was never asked");
    }

    /// <summary>
    /// With nothing written down there is nothing to write over, and one model is
    /// not an inventory: storing it as one would take every other name off the
    /// box on the strength of never having asked about them.
    /// </summary>
    [AvaloniaFact]
    public void A_probe_of_one_model_does_not_become_the_whole_list()
    {
        var saved = Configured("surveying");
        var window = Showing(With(new Surveying()), saved);
        var panel = All<AssistantPanel>(window).Single();
        var host = Settings(window);

        KeyBox(host).Text = "sk-typed";
        Settle(host);

        Click(ProbeButton(host));
        Settle(host);

        panel.SaveSettings();

        Survey.Read(saved.Of("surveying").Text(Survey.Key)).ShouldBeEmpty();
        ProbeNote(host).ShouldContain("quiet answers:", Case.Sensitive);
    }

    /// <summary>
    /// The one answer worth the money: the model is not there, or the key does
    /// not reach it. Nothing on the form is disturbed by finding out.
    /// </summary>
    [AvaloniaFact]
    public void A_model_the_endpoint_will_not_answer_for_is_said_and_nothing_else()
    {
        var saved = Configured("surveying", (Survey.Key, Listed("first")));
        var host = Settings(Showing(With(new Surveying(_ => Task.FromResult<IReadOnlyList<ModelReport>>([]))), saved));

        KeyBox(host).Text = "sk-typed";
        Settle(host);

        Click(ProbeButton(host));
        Settle(host);

        ProbeNote(host).ShouldContain("did not answer");

        var models = All<ComboBox>(host).Single(box => box.Name == AssistantSchema.ModelKey);

        ((IEnumerable<string>)models.ItemsSource!).ShouldBe(["first"]);
    }

    /// <summary>
    /// A key that has not been saved is still a key. Without one there is
    /// nothing to ask with, and the button says so rather than doing nothing.
    /// </summary>
    [AvaloniaFact]
    public void The_probe_waits_for_a_key_and_not_for_it_to_be_saved()
    {
        var host = Settings(Showing(With(new Surveying()), Configured("surveying")));

        ProbeButton(host).IsEnabled.ShouldBeFalse("there is no key to ask with");

        KeyBox(host).Text = "sk-typed";
        Settle(host);

        ProbeButton(host).IsEnabled.ShouldBeTrue();
    }

    /// <summary>
    /// Gone rather than dead for a provider that cannot answer it: a survey is
    /// something a provider either has or has not, and a button that could never
    /// work is a question about itself.
    /// </summary>
    [AvaloniaFact]
    public void A_provider_that_cannot_be_asked_has_no_probe_button()
    {
        var host = Settings(Showing(With(new Deaf()), Configured("deaf")));

        ProbeButton(host).IsEffectivelyVisible.ShouldBeFalse();
    }

    /// <summary>
    /// The one button in both its jobs, as the message box's is. A survey walks a
    /// list of models over minutes, so the way out of one has to be the way in.
    /// </summary>
    [AvaloniaFact]
    public void The_probe_button_stops_the_probe_it_started()
    {
        var host = Settings(Showing(With(new Surveying(Never)), Configured("surveying")));

        KeyBox(host).Text = "sk-typed";
        Settle(host);

        Click(ProbeButton(host));
        Settle(host);

        ProbeButton(host).Content.ShouldBe("Stop");

        Click(ProbeButton(host));
        Settle(host);

        ProbeButton(host).Content.ShouldBe("Probe this model");
        ProbeNote(host).ShouldBe("Stopped. Nothing was kept.");
    }

    private sealed class FakeStore : ISecretStore
    {
        private readonly Dictionary<string, string> held = new(StringComparer.Ordinal);

        public string Id => "fake";

        public string Name => "Fake store";

        public int Priority => 0;

        public bool IsSupported => true;

        public void Keep(string account, string secret) => held[account] = secret;

        public string? Recall(string account) => held.GetValueOrDefault(account);

        public void Forget(string account) => held.Remove(account);
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

        // Row 0 is "None", row 1 is "keyless" (already in force) and row 2 is "both".
        All<ComboBox>(host).Single(c => c.Name == "provider").SelectedIndex = 2;
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
