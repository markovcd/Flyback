using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Flyback.Editor.Assist;
using Flyback.Editor.Notices;
using Flyback.Editor.Windows;
using Flyback.Assist;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Secrets;
using Flyback.Plugins.Settings;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Assist;

public partial class AssistantPanelTests
{
    // --- handbook text the assistant looked up --------------------------------

    private const string Looked = "core.filter | Filter | Filter";

    /// <summary>One that looks a module up and then says something.</summary>
    private sealed class Reads() : Provider(new AssistantSchema(
        "reads",
        [new AssistantModel("reads")],
        "NONE",
        "none needed"))
    {
        public override string Id => "reads";

        public override string Name => "Looks one module up";

        public override IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => new Read();
    }

    private sealed class Read : IPatchSession
    {
        public async IAsyncEnumerable<PatchEvent> Ask(
            string instruction,
            [EnumeratorCancellation] CancellationToken cancel)
        {
            await Task.Yield();

            yield return new PatchEvent.Read(Looked);
            yield return new PatchEvent.Said("a filter it is.");
        }

        public void Dispose()
        {
        }
    }

    private Window Asked(AssistantSettings settings)
    {
        var window = Showing(With(new Reads()), settings);

        Instruction(window).Text = "make something";
        Settle(window);

        Press(SendButton(window));
        Settle(window);
        Settle(window);

        return window;
    }

    /// <summary>A lookup is one of the steps it took, so it stays in their run rather than splitting it in two.</summary>
    [AvaloniaFact]
    public void Handbook_text_stays_in_the_run_of_steps_it_came_in()
    {
        var transcript = new TranscriptView();

        transcript.Put(Voice.Note, "switched glow off.");
        transcript.Put(Voice.Handbook, "feedback.trails | Trails | Feedback");
        transcript.Put(Voice.Aside, "45073 in (43039 cached), 1187 out.");

        var runs = transcript.GetLogicalDescendants().OfType<StepsGroup>().ToList();

        runs.Count.ShouldBe(1);
        runs[0].GetLogicalDescendants().OfType<SelectableTextBlock>()
            .ShouldContain(block => block.Text == "feedback.trails | Trails | Feedback");
    }

    private static SelectableTextBlock Handbook(Window window) =>
        All<SelectableTextBlock>(window).Single(block => block.Text == Looked);

    [AvaloniaFact]
    public void Handbook_text_the_assistant_looked_up_is_shown_by_default()
    {
        var window = Asked(Configured("reads"));

        Handbook(window).IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Handbook_text_is_hidden_while_the_setting_is_off_and_shown_again_once_it_is_on()
    {
        var settings = Configured("reads");
        settings.ShowLookups = false;

        var window = Asked(settings);

        Handbook(window).IsVisible.ShouldBeFalse();
        All<SelectableTextBlock>(window).ShouldContain(block => block.Text == "a filter it is." && block.IsVisible);

        var host = Settings(window);
        All<CheckBox>(host).Single(c => c.Name == "showLookups").IsChecked = true;
        Settle(host);

        All<AssistantPanel>(window).Single().SaveSettings();
        Settle(window);

        settings.ShowLookups.ShouldBeTrue();
        Handbook(window).IsVisible.ShouldBeTrue("kept all along, so turning it on shows what was already looked up");
    }

    /// <summary>The folded block the briefing arrives as.</summary>
    private static Control Briefing(Window window) =>
        (Control)All<Button>(window)
            .Single(b => b.Name == "fold" && Gist(b).Contains("The briefing it was handed"))
            .Parent!;

    [AvaloniaFact]
    public void The_briefing_the_assistant_was_handed_heads_the_conversation()
    {
        var window = Asked(Configured("reads"));

        Briefing(window).IsVisible.ShouldBeTrue();
        Handbook(window).IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_briefing_is_hidden_on_its_own_setting_and_the_lookups_stay()
    {
        var settings = Configured("reads");
        settings.ShowBriefing = false;

        var window = Asked(settings);

        Briefing(window).IsVisible.ShouldBeFalse();
        Handbook(window).IsVisible.ShouldBeTrue();

        var host = Settings(window);
        All<CheckBox>(host).Single(c => c.Name == "showBriefing").IsChecked = true;
        Settle(host);

        All<AssistantPanel>(window).Single().SaveSettings();
        Settle(window);

        Briefing(window).IsVisible.ShouldBeTrue();
    }

    /// <summary>The settings, in a window of their own, as opening them makes one.</summary>
    private Window Settings(Window panel)
    {
        var host = Owned(new Window { Content = All<AssistantPanel>(panel).Single().SettingsSection() });

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

        public Uri? Endpoint(SettingValues values) => new("https://assistant.test/");

        // Through Surveyed, as both shipped adapters declare themselves: a
        // survey of the endpoint replaces the written-down models, and a fake
        // that skipped it would be a fake nothing could probe.
        public IReadOnlyList<SettingField> Form(SettingValues values) => Schema.Surveyed(values).Form(values);

        public AssistantSenses Senses(SettingValues values) => Schema.Surveyed(values).Senses(values);

        public virtual string? Unavailable(AssistantConfig config) => null;

        public virtual IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
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
        All<Button>(window).Single(b => b.Name == "send");

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
            new PluginCatalog([], [], NodeCatalog.BuiltIn, [.. Presets.All], [], [new Keyless(), new Both()]),
            Configured("both")));

        // Row 0 is "None", so a real provider sits one row below its own place
        // in the catalog — "both" is the second provider offered.
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
    /// The bug this was written for: the ear sat grayed out under a ticked box, and
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
    /// graying out — a disabled control asks somebody to work out why it is
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
            new PluginCatalog([], [], NodeCatalog.BuiltIn, [.. Presets.All], [], [new Deaf(), new Both()]),
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

    private const string KeyVariable = "FLYBACK_PANEL_TEST_KEY";

    private const string Key = "sk-proj-paneltestpaneltestpanel42";

    /// <summary>One whose key is read from <see cref="KeyVariable"/>, and which answers once it has one.</summary>
    private sealed class Keyed() : Provider(new AssistantSchema(
        "keyed",
        [new AssistantModel("keyed")],
        KeyVariable,
        "none needed"))
    {
        public override string Id => "keyed";

        public override string Name => "Needs a key";

        public override string? Unavailable(AssistantConfig config) => config.Transport.HasKey ? null : "no key";

        public override IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) =>
            new Turn(Task.CompletedTask);
    }

    [AvaloniaFact]
    public void A_message_holding_the_key_is_logged_without_it()
    {
        var logs = Directory.CreateTempSubdirectory("flyback-panel-logs-").FullName;
        Environment.SetEnvironmentVariable(KeyVariable, Key);

        try
        {
            var settings = Configured("keyed");
            settings.LogConversations = true;

            var window = Showing(With(new Keyed()), settings, logs: logs);

            Instruction(window).Text = $"my key is {Key}, make something";
            Settle(window);

            Press(SendButton(window));
            Settle(window);
            Settle(window);

            // Setting the conversation aside closes its log, so the file can be read and deleted.
            All<AssistantPanel>(window).Single().Open(null);
            window.Close();

            var written = Directory.GetFiles(logs, "*.log");

            written.ShouldNotBeEmpty("logging is on, so the conversation has a file");

            foreach (var file in written)
                File.ReadAllText(file).ShouldNotContain(Key);
        }
        finally
        {
            Environment.SetEnvironmentVariable(KeyVariable, null);
            Directory.Delete(logs, recursive: true);
        }
    }

    [AvaloniaFact]
    public void The_context_limit_shows_what_was_saved_and_keeps_what_is_saved()
    {
        var saved = new AssistantSettings { ContextLimit = 200_000 };
        var window = Showing(With(new Deaf()), saved);
        var panel = All<AssistantPanel>(window).Single();

        var host = Settings(window);
        var context = All<NumericUpDown>(host).Single(c => c.Name == "contextLimit");

        context.Value.ShouldBe(200_000);

        context.Value = 400_000;
        Settle(host);

        saved.ContextLimit.ShouldBe(200_000, "nothing is kept until Save");

        panel.SaveSettings();
        Settle(host);

        saved.ContextLimit.ShouldBe(400_000);
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

        Service<Reactions>(window).Add<UndescribedChanged>(_ => changed++);
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
    public void Discarding_puts_the_context_limit_back()
    {
        var saved = new AssistantSettings { ContextLimit = 200_000 };
        var window = Showing(With(new Deaf()), saved);
        var panel = All<AssistantPanel>(window).Single();

        var host = Settings(window);
        var context = All<NumericUpDown>(host).Single(c => c.Name == "contextLimit");

        context.Value = 50_000;
        panel.DiscardSettings();
        Settle(host);

        context.Value.ShouldBe(200_000);
        saved.ContextLimit.ShouldBe(200_000);
    }

    /// <summary>
    /// "Key saved" is news about something that just happened, not a standing
    /// description of the key's state — a second Save with nothing typed and
    /// the box still ticked has nothing new to report.
    /// </summary>
    [AvaloniaFact]
    public void Saving_again_with_nothing_changed_does_not_repeat_the_key_saved_message()
    {
        var store = new FakeSecretStore();
        var plugins = new PluginCatalog([], [], NodeCatalog.BuiltIn, [.. Presets.All], [], [new Deaf()], [store]);
        var messages = new List<string>();

        var window = Showing(plugins, new AssistantSettings());
        var panel = All<AssistantPanel>(window).Single();

        Service<ReportLine>(window).Said += (_, message) => messages.Add(message);

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
}
