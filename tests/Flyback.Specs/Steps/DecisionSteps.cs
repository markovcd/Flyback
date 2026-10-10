using System.CommandLine;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Specs.Steps;

/// <summary>
/// <c>flyback-cli decide</c> over the decision models laid out under <c>plugins\</c>, and the
/// editor's Decisions settings, with settings of the scenario's own.
/// </summary>
[Binding]
public sealed class DecisionSteps(EditorDriver editor) : IDisposable
{
    /// <summary>The For picker's wording for <see cref="DecisionUse.Modules"/>, and for the model's own settings.</summary>
    private const string ForModules = "Finding a module by meaning", ForEveryUse = "Every use";

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-decide-specs");

    private PluginCatalog? catalog;
    private int code;
    private string said = string.Empty;
    private string complained = string.Empty;

    private string SettingsPath => Path.Combine(folder.FullName, "settings.json");

    public void Dispose() => folder.Delete(recursive: true);

    [Given("the decision models that are installed")]
    public void GivenTheModels()
    {
        catalog = ShippedPlugins.Loaded;

        // The scripted one, which answers without a model or a network.
        new DecisionSettings { Model = "scripted" }.Save(SettingsPath);
    }

    [Given("decisions are turned off")]
    public void GivenOff()
    {
        catalog ??= ShippedPlugins.Loaded;
        new DecisionSettings { Model = DecisionSettings.Off }.Save(SettingsPath);
    }

    [Given("a decision model that takes {string} to mean a Kaleidoscope")]
    public void GivenKaleidoscopic(string phrase)
    {
        catalog = new PluginCatalog([], [], NodeCatalog.BuiltIn, Presets.All, [], decisionModels: [new Meaning(phrase, "Kaleidoscope", "Geometry")]);
        new DecisionSettings().Save(SettingsPath);
    }

    [Given("a decision model that takes {string} to mean a Kaleidoscope only when its model is {string}")]
    public void GivenKaleidoscopicWhenSet(string phrase, string model)
    {
        catalog = new PluginCatalog([], [], NodeCatalog.BuiltIn, Presets.All, [], decisionModels: [new Meaning(phrase, "Kaleidoscope", "Geometry", model)]);
        new DecisionSettings().Save(SettingsPath);
    }

    [Given("flyback-cli sets its model to {string} for finding modules alone")]
    public void GivenSetForModules(string model)
    {
        Cli("decide", "--for", DecisionUse.Modules, "--set", $"model={model}", "--save");

        code.ShouldBe(Exit.Ok, complained);
    }

    [When("the settings window sets the model to {string} for finding modules alone")]
    public void WhenTheWindowSetsForModules(string model)
    {
        OpenDecisions();
        editor.PickDecisionUse(ForModules);
        editor.TypeSetting("model", model);
        editor.Answer("Save");
    }

    [Then("the settings window shows the model as {string} for finding modules, and nothing set for every use")]
    public void ThenTheWindowShows(string model)
    {
        OpenDecisions();
        editor.PickDecisionUse(ForModules);
        editor.SettingText("model").ShouldBe(model);
        editor.PickDecisionUse(ForEveryUse);
        editor.SettingText("model").ShouldBeEmpty();
    }

    /// <summary>Opens the editor on the scenario's models and settings, then its settings window on the Decisions tab.</summary>
    private void OpenDecisions()
    {
        editor.Setup = editor.Setup with
        {
            Folders = editor.Setup.Folders with { SettingsPath = SettingsPath },
            Plugins = catalog.ShouldNotBeNull(),
        };

        editor.OpenSettings("Decisions");
    }

    [When("flyback-cli finds the modules {string} describes")]
    public void WhenFound(string phrase) => Cli("modules", "--find", phrase, "--json");

    [Given("a decision model that blames the module {string}")]
    public void GivenBlaming(string module)
    {
        catalog = new PluginCatalog([], [], NodeCatalog.BuiltIn, Presets.All, [], decisionModels: [new Blaming(module)]);
        new DecisionSettings().Save(SettingsPath);
    }

    [When("flyback-cli checks a patch missing two modules, likeliest first")]
    public void WhenTriaged()
    {
        var patch = Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn);

        foreach (var (missing, port) in new[] { ("osc.nonesuch", NodeCatalog.OutputColorPort), ("osc.nothere", NodeCatalog.OutputLeftPort) })
        {
            patch.Nodes.Add(new NodeInstance { Id = Guid.NewGuid(), TypeId = missing });
            patch.Connect(patch.Nodes[^1].Id, 0, patch.Output.Id, port);
        }

        var file = Path.Combine(folder.FullName, "patch.fbk");
        File.WriteAllText(file, PatchIO.ToJson(patch));

        Cli("check", file, "--triage", "--json");
    }

    [Then("the first complaint is about {string}")]
    public void ThenFirst(string module) =>
        JsonNode.Parse(said)!["issues"]!.AsArray()[0]!["message"]!.GetValue<string>().ShouldContain(module);

    [Then("the first module found is the {string}")]
    public void ThenFirstFound(string name)
    {
        code.ShouldBe(Exit.Ok, complained);
        JsonNode.Parse(said)!.AsArray()[0]!["name"]!.GetValue<string>().ShouldBe(name);
    }

    [Then("the first module found is the Kaleidoscope")]
    public void ThenKaleidoscope()
    {
        code.ShouldBe(Exit.Ok, complained);
        JsonNode.Parse(said)!.AsArray()[0]!["typeId"]!.GetValue<string>().ShouldBe("space.kaleidoscope");
    }

    [When("flyback-cli decides whether {string} is about money, as JSON")]
    public void WhenDecided(string state) => Cli("decide", state, "--yes-no", "Is this about money?", "--json");

    /// <summary>Runs flyback-cli on the scenario's models and settings, keeping what it said and complained.</summary>
    private void Cli(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        code = InProcessCli.Run(
            [.. args, "--settings", SettingsPath],
            new PluginRegistry(() => catalog.ShouldNotBeNull(), folder.FullName, null),
            new InvocationConfiguration { Output = output, Error = error });

        said = output.ToString();
        complained = error.ToString();
    }

    [Then("the answer is a yes-no with a probability between 0 and 1")]
    public void ThenYesNo()
    {
        code.ShouldBe(Exit.Ok, complained);

        var answer = JsonNode.Parse(said)!["answers"]!.AsObject().ShouldHaveSingleItem().Value!;

        answer["type"]!.GetValue<string>().ShouldBe("noul");
        answer["noul"]!.GetValue<double>().ShouldBeInRange(0, 1);
    }

    [Then("the command fails, saying no decision model is in use")]
    public void ThenRefused()
    {
        code.ShouldBe(Exit.Failed);
        complained.ShouldContain("No decision model is in use");
    }

    /// <summary>
    /// Takes one phrase to mean one module, by name, of one category, and anything else to mean none
    /// of them; with <paramref name="needs"/>, only while its model setting is that.
    /// </summary>
    private sealed class Meaning(string phrase, string module, string category, string? needs = null) : IDecisionModel
    {
        public string Id => "meaning";

        public string Name => "Meaning";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [new SettingField.Text("model", "Model")];

        public string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel) =>
            Task.FromResult(new Decision("meaning", request.Questions.ToDictionary(q => q.Key, q => Chosen(request.State, config.Values, (Question.Choice)q.Value)), DecisionUsage.None));

        private Answer Chosen(string state, SettingValues values, Question.Choice choice)
        {
            // A module's label is its name, with its words in brackets after it.
            static bool Named(string label, string name) => label == name || label.StartsWith(name + " (", StringComparison.Ordinal);

            var labels = choice.Options.Select(o => o.Label).ToList();
            var wanted = state != phrase || (needs is not null && values.Text("model") != needs) ? "none"
                : labels.FirstOrDefault(l => Named(l, module))
                ?? labels.FirstOrDefault(l => Named(l, category))
                ?? "none";

            return new Answer.Chosen(wanted, choice.Options.ToDictionary(o => o.Label, o => o.Label == wanted ? 0.9 : 0.1 / choice.Options.Count), 0.9);
        }
    }

    /// <summary>Says yes to a complaint that names one module, and no to any other.</summary>
    private sealed class Blaming(string module) : IDecisionModel
    {
        public string Id => "blaming";

        public string Name => "Blaming";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel) =>
            Task.FromResult(new Decision("blaming", request.Questions.ToDictionary(q => q.Key, q =>
            {
                return (Answer)new Answer.YesNo(request.State.Contains(module, StringComparison.Ordinal) ? 1 : 0);
            }), DecisionUsage.None));
    }
}
