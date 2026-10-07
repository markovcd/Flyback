using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Editor.Decide;
using Flyback.Editor.Tests.Ui;
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

        return (section, container.GetRequiredService<Decisions>());
    }

    private static T Named<T>(DecisionsSection section, string name) where T : Control =>
        All<T>(section.View).Single(c => c.Name == name);

    [AvaloniaFact]
    public void Nobody_having_chosen_shows_the_model_that_runs_here_ready()
    {
        var (section, _) = Built(models: new ScriptedDecider());

        Named<ComboBox>(section, "decisionModel").SelectedItem.ShouldBe("Scripted decider");
        Named<TextBlock>(section, "decisionStatus").Text.ShouldBe("Scripted decider is ready, and sends nothing anywhere.");
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

        Named<ComboBox>(section, "decisionModel").SelectedIndex = 0;
        section.Save();

        DecisionSettings.Load(SettingsPath).Model.ShouldBe(DecisionSettings.Off);
        decisions.Chosen.ShouldBeNull();
        Named<TextBlock>(section, "decisionStatus").Text.ShouldNotBeNull().ShouldContain("nothing is sent anywhere");
    }

    [AvaloniaFact]
    public void A_hosted_model_asks_for_a_key_and_one_that_runs_here_does_not()
    {
        var (section, _) = Built(models: [new ScriptedDecider(), new Hosted()]);
        var model = Named<ComboBox>(section, "decisionModel");
        var key = Named<TextBox>(section, "decisionKey");

        model.SelectedItem = "Scripted decider";
        key.IsEffectivelyVisible.ShouldBeFalse();

        model.SelectedItem = "Hosted";
        key.IsEffectivelyVisible.ShouldBeTrue();
        Named<TextBlock>(section, "decisionStatus").Text.ShouldBe("No key yet.");
    }

    [AvaloniaFact]
    public async Task Trying_it_answers_the_routing_question_about_what_was_typed()
    {
        var scripted = new ScriptedDecider();
        var (section, _) = Built(models: scripted);

        Named<TextBox>(section, "tryDecision").Text = "make the bass slower";
        Named<Button>(section, "askDecision").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Until(() => Named<TextBlock>(section, "decisionAnswer").Text != "Asking…");

        Named<TextBlock>(section, "decisionAnswer").Text.ShouldBe("Yes, 1.00 that it asks for a change.");
        scripted.Asked.ShouldHaveSingleItem().State.ShouldBe("make the bass slower");
    }

    [AvaloniaFact]
    public async Task Download_fetches_the_model_and_it_is_then_ready()
    {
        var network = new Weights();
        var (section, decisions) = Built(
            services => services.AddHttpClient(DecisionsSection.Client).ConfigurePrimaryHttpMessageHandler(() => network),
            new Downloaded());

        var download = Named<Button>(section, "downloadModel");

        download.IsVisible.ShouldBeTrue();
        Named<TextBlock>(section, "decisionStatus").Text.ShouldNotBeNull().ShouldContain("needs 0 MB downloaded from models.test");

        download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await Until(() => decisions.Store.Prepared(decisions.Models[0]));
        await Until(() => !download.IsVisible);

        Named<TextBlock>(section, "decisionStatus").Text.ShouldBe("Downloaded is ready, and sends nothing anywhere.");
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }

        done().ShouldBeTrue();
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
