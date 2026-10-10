using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Decide;
using Flyback.Editor.Settings;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.FakeDecider;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Decide;

/// <summary>The Decisions section of the settings window, built by the editor's own container.</summary>
public sealed class DecisionsSectionTests : EditorTest
{
    private readonly string root = Directory.CreateTempSubdirectory("flyback-decisions-ui").FullName;

    public override void Dispose()
    {
        base.Dispose();
        Directory.Delete(root, recursive: true);
    }

    private string SettingsPath => Path.Combine(root, "settings.json");

    private (DecisionsSection Section, Decisions Decisions) Built(Action<IServiceCollection>? replace = null, params IDecisionModel[] models)
    {
        var container = Container(
            new EditorSetup
            {
                Folders = new EditorFolders { SettingsPath = SettingsPath, ModelFolder = Path.Combine(root, "models") },
                Plugins = new PluginCatalog([], [], NodeCatalog.BuiltIn, Presets.All, [], decisionModels: models),
            },
            replace);

        var section = container.GetRequiredService<DecisionsSection>();
        section.Opening();

        // In a window, so the form's rows under its content are built and found.
        Show(section.View, SettingsSession.SectionWidth);

        return (section, container.GetRequiredService<Decisions>());
    }

    [AvaloniaFact]
    public void Nobody_having_chosen_shows_the_model_that_runs_here_ready()
    {
        var (section, _) = Built(models: new ScriptedDecider());

        Named<ComboBox>(section.View, "decisionModel").SelectedItem.ShouldBe("Scripted decider");
        Named<TextBlock>(section.View, "decisionStatus").Text.ShouldBe("Scripted decider is ready, and sends nothing anywhere.");
    }

    [AvaloniaFact]
    public void Saving_without_touching_the_picker_leaves_the_choice_unmade()
    {
        var (section, _) = Built(models: new ScriptedDecider());

        section.Save();

        DecisionSettings.Load(SettingsPath).Model.ShouldBeNull();
    }

    [AvaloniaFact]
    public void Picking_none_turns_decisions_off()
    {
        var (section, decisions) = Built(models: new ScriptedDecider());

        Named<ComboBox>(section.View, "decisionModel").SelectedIndex = 0;
        section.Save();

        DecisionSettings.Load(SettingsPath).Model.ShouldBe(DecisionSettings.Off);
        decisions.Chosen.ShouldBeNull();
        Named<TextBlock>(section.View, "decisionStatus").Text.ShouldNotBeNull().ShouldContain("nothing is sent anywhere");
    }

    [AvaloniaFact]
    public void A_hosted_model_asks_for_a_key_and_one_that_runs_here_does_not()
    {
        var (section, _) = Built(models: [new ScriptedDecider(), new Hosted()]);
        var model = Named<ComboBox>(section.View, "decisionModel");
        var key = Named<TextBox>(section.View, "decisionKey");

        model.SelectedItem = "Scripted decider";
        key.IsEffectivelyVisible.ShouldBeFalse();

        model.SelectedItem = "Hosted";
        key.IsEffectivelyVisible.ShouldBeTrue();
        Named<TextBlock>(section.View, "decisionStatus").Text.ShouldBe("No key yet.");
    }

    [AvaloniaFact]
    public void A_key_typed_is_taken_on_save_said_where_it_is_held_and_can_be_forgotten()
    {
        var (section, _) = Built(models: [new ScriptedDecider(), new Hosted()]);
        Named<ComboBox>(section.View, "decisionModel").SelectedItem = "Hosted";
        var key = Named<TextBox>(section.View, "decisionKey");

        key.Text = "test-key-not-real";
        section.Save();

        key.Text.ShouldBeEmpty("a key taken is not left in the box");
        Named<TextBlock>(section.View, "decisionStatus").Text.ShouldBe("Hosted is ready. What is asked is sent to it.");
        All<TextBlock>(section.View).ShouldContain(t => t.Text != null && t.Text.StartsWith("In force, held for this window only", StringComparison.Ordinal));

        Press(All<Button>(section.View).Single(b => b.Content as string == "Forget key"));

        Named<TextBlock>(section.View, "decisionStatus").Text.ShouldBe("No key yet.");
    }

    [AvaloniaFact]
    public void Trying_it_answers_the_routing_question_about_what_was_typed()
    {
        var scripted = new ScriptedDecider();
        var (section, _) = Built(models: scripted);

        Named<TextBox>(section.View, "tryDecision").Text = "make the bass slower";
        Press(Named<Button>(section.View, "askDecision"));

        Pump(() => Named<TextBlock>(section.View, "decisionAnswer").Text != "Asking…");

        Named<TextBlock>(section.View, "decisionAnswer").Text.ShouldBe("Yes, 1.00 that it asks for a change.");
        scripted.Asked.ShouldHaveSingleItem().State.ShouldBe("make the bass slower");
    }

    [AvaloniaFact]
    public void Download_fetches_the_model_and_it_is_then_ready()
    {
        var network = new Weights();
        var (section, decisions) = Built(
            services => services.AddHttpClient(DecisionsSection.Client).ConfigurePrimaryHttpMessageHandler(() => network),
            new Downloaded());

        var download = Named<Button>(section.View, "downloadModel");

        download.IsVisible.ShouldBeTrue();
        Named<TextBlock>(section.View, "decisionStatus").Text.ShouldNotBeNull().ShouldContain("needs 0 MB downloaded from models.test");

        Press(download);

        Pump(() => decisions.Store.Prepared(decisions.Models[0]));
        Pump(() => !download.IsVisible);

        Named<TextBlock>(section.View, "decisionStatus").Text.ShouldBe("Downloaded is ready, and sends nothing anywhere.");
    }

    [AvaloniaFact]
    public void A_setting_kept_for_one_use_is_shown_when_that_use_is_picked_and_listed_under_the_form()
    {
        var settings = new DecisionSettings();
        settings.Remember("settable", DecisionUse.Modules, new SettingValues([new("model", "typed")]));
        settings.Save(SettingsPath);

        var (section, _) = Built(models: new Settable());

        Named<TextBox>(section.View, "model").Text.ShouldBeEmpty("the model's own settings come first");
        Named<TextBlock>(section.View, "decisionUses").Text.ShouldBe("For finding a module by meaning: model=typed.");

        section.ShowUse("Finding a module by meaning");

        Named<TextBox>(section.View, "model").Text.ShouldBe("typed");
    }

    [AvaloniaFact]
    public void A_setting_typed_for_one_use_is_kept_for_that_use_alone()
    {
        var (section, decisions) = Built(models: new Settable());

        section.ShowUse("Finding a module by meaning");
        Named<TextBox>(section.View, "model").Text = "typed";

        Named<TextBlock>(section.View, "decisionUses").Text.ShouldBe("For finding a module by meaning: model=typed.");

        section.ShowUse("Every use");
        Named<TextBox>(section.View, "model").Text.ShouldBeEmpty("a use's setting is laid over the model's own, not written into them");

        section.Save();

        var saved = DecisionSettings.Load(SettingsPath);
        saved.Of("settable", DecisionUse.Modules).Text("model").ShouldBe("typed");
        saved.Of("settable").Text("model").ShouldBeEmpty();
        decisions.Settings.Of("settable", DecisionUse.Turns).Text("model").ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void A_use_set_back_to_the_model_s_own_value_lays_nothing_over_it()
    {
        var settings = new DecisionSettings();
        settings.Remember("settable", new SettingValues([new("model", "plain")]));
        settings.Remember("settable", DecisionUse.Modules, new SettingValues([new("model", "typed")]));
        settings.Save(SettingsPath);

        var (section, _) = Built(models: new Settable());

        section.ShowUse("Finding a module by meaning");
        Named<TextBox>(section.View, "model").Text = "plain";
        section.Save();

        Named<TextBlock>(section.View, "decisionUses").IsVisible.ShouldBeFalse();
        DecisionSettings.Load(SettingsPath).Uses.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Closing_without_saving_drops_what_was_typed_for_a_use()
    {
        var (section, _) = Built(models: new Settable());

        section.ShowUse("Finding a module by meaning");
        Named<TextBox>(section.View, "model").Text = "typed";

        section.Show();

        Named<TextBlock>(section.View, "decisionUses").IsVisible.ShouldBeFalse();
        section.ShowUse("Finding a module by meaning");
        Named<TextBox>(section.View, "model").Text.ShouldBeEmpty();
    }

    private sealed class Hosted : IDecisionModel
    {
        public string Id => "hosted";

        public string Name => "Hosted";

        public int Priority => 0;

        public AssistantCredential? Credential { get; } = new("HOSTED_DECISION_KEY_NOT_SET", "Get one somewhere.");

        public Uri? Endpoint(SettingValues values) => new("https://hosted.test/v1/systemone");

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => config.Transport.HasKey ? null : "No key yet.";

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel) => throw new NotSupportedException();
    }

    /// <summary>Runs here, and has one setting, a model name, to lay a use's own over.</summary>
    private sealed class Settable : IDecisionModel
    {
        public string Id => "settable";

        public string Name => "Settable";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [new SettingField.Text("model", "Model")];

        public string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel) => throw new NotSupportedException();
    }

    private sealed class Downloaded : IDecisionModel, IPreparedModel
    {
        private readonly ScriptedDecider answers = new();

        public string Id => "downloaded";

        public string Name => "Downloaded";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<ModelFile> Needs { get; } =
            [new(new Uri("https://models.test/w.bin"), "w.bin", Convert.ToHexStringLower(SHA256.HashData(Weights.Body)), Weights.Body.Length)];

        public bool Prepared(string folder) => File.Exists(Path.Combine(folder, "w.bin"));

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel) => answers.DecideAsync(request, config, cancel);
    }

    private sealed class Weights : HttpMessageHandler
    {
        public static readonly byte[] Body = Encoding.ASCII.GetBytes("weights");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Body) });
    }
}
