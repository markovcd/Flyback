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

        Press(ProbeButton(host));
        Settle(host);

        provider.Asked.ShouldNotBeNull();
        provider.Asked!.Transport.HasKey.ShouldBeTrue();
        provider.Asked.Transport.Origin.ShouldBe("https://assistant.test");
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

        Press(ProbeButton(host));
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

        Press(ProbeButton(host));
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

        Press(ProbeButton(host));
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

        Press(ProbeButton(host));
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

        Press(ProbeButton(host));
        Settle(host);

        ProbeButton(host).Content.ShouldBe("Stop");

        Press(ProbeButton(host));
        Settle(host);

        ProbeButton(host).Content.ShouldBe("Probe this model");
        ProbeNote(host).ShouldBe("Stopped. Nothing was kept.");
    }

    /// <summary>
    /// In the field the message is written in rather than beside it, and in the
    /// corner one finishes typing nearest, on a row of its own under the text.
    /// </summary>
    [AvaloniaFact]
    public void The_button_sits_in_the_bottom_right_of_the_message_field()
    {
        var window = Showing();

        var box = On(window, All<Border>(window).Single(b => b.Name == "composer"));
        var text = On(window, Instruction(window));
        var button = On(window, SendButton(window));

        button.Width.ShouldBeLessThan(box.Width / 4, "it is a small square, not a bar");
        button.Height.ShouldBeLessThan(box.Height);

        box.Contains(button).ShouldBeTrue("the button is inside the box it belongs to");

        (box.Right - button.Right).ShouldBeLessThan(12, "hard against the right edge");
        (box.Bottom - button.Bottom).ShouldBeLessThan(12, "and the bottom one");
        button.Top.ShouldBeGreaterThanOrEqualTo(text.Bottom, "under the text, never over it");
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
            new PluginCatalog([], [], NodeCatalog.BuiltIn, [.. Presets.All], [], [new Keyless(), new Both()]),
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
        // in gray over stale amber text.
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
