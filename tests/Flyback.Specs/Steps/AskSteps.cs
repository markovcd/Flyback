using System.CommandLine;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Flyback.Editor.Assist;
using Flyback.Assist;
using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Reqnroll;
using Shouldly;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Specs.Steps;

/// <summary>
/// <c>flyback-cli ask</c> over an assistant that answers from a script, with its
/// settings and conversations kept in a folder of the scenario's own.
/// </summary>
[Binding]
public sealed class AskSteps : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-ask-specs");

    private Remembering? assistant;
    private int code;
    private string said = string.Empty;
    private string[] lines = [];

    private Remembering Assistant => assistant.ShouldNotBeNull();

    private string SettingsPath => Path("settings.json");

    private ConversationStore Store => new(Path("sessions"));

    private IDecisionModel? reader;

    private PluginCatalog Catalog => new([], [], NodeCatalog.BuiltIn, Presets.All, [], [Assistant], decisionModels: reader is null ? null : [reader]);

    public void Dispose() => folder.Delete(recursive: true);

    [Given("an assistant that builds a gray field when asked")]
    public void GivenAnAssistant()
    {
        assistant = new Remembering();
        new AssistantSettings { Provider = Assistant.Id }.Save(SettingsPath);
    }

    [When("flyback-cli asks it about {string} for {string}")]
    public Task WhenAsked(string patch, string message) => Ask(patch, message, json: false);

    [Given("an assistant that writes ideas out in full")]
    public void GivenAnAssistantThatWrites()
    {
        assistant = new Remembering { Brief = "A slow tide, heard and seen as one slow swell, building over four minutes." };
        new AssistantSettings { Provider = Assistant.Id }.Save(SettingsPath);
    }

    [When("flyback-cli expands {string} over {string}")]
    public async Task WhenExpanded(string idea, string patch)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var about = AskCommand.Open(Catalog, new FileInfo(Path(patch)), null, null, error, Store).ShouldNotBeNull(error.ToString());

        code = await AskCommand.Run(
            Catalog,
            about,
            new AskOptions(idea, null, [], false, false, null, false, null, Expand: true),
            output,
            error,
            TextReader.Null,
            null,
            CancellationToken.None,
            SettingsPath,
            Store,
            Path("logs"));

        said = output + error.ToString();
    }

    [Then("the brief is printed")]
    public void ThenTheBriefIsPrinted()
    {
        code.ShouldBe(Exit.Ok, said);
        said.Trim().ShouldBe(Assistant.Brief);
    }

    [Then("{string} does not exist")]
    public void ThenDoesNotExist(string patch) => File.Exists(Path(patch)).ShouldBeFalse();

    [When("flyback-cli asks it about {string} for {string} as JSON")]
    public Task WhenAskedForJson(string patch, string message) => Ask(patch, message, json: true);

    [When("flyback-cli asks it about {string} with {string} after the patch")]
    public void WhenRunWith(string patch, string rest)
    {
        var error = new StringWriter();

        code = InProcessCli.Run(
            ["ask", Path(patch), .. rest.Split(' ')],
            new PluginRegistry(() => Catalog, folder.FullName, null),
            new InvocationConfiguration { Output = TextWriter.Null, Error = error });

        said = error.ToString();
    }

    private async Task Ask(string patch, string message, bool json)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var about = AskCommand.Open(Catalog, new FileInfo(Path(patch)), null, null, error, Store).ShouldNotBeNull(error.ToString());

        code = await AskCommand.Run(
            Catalog,
            about,
            new AskOptions(message, null, [], false, json, null, false, null),
            output,
            error,
            TextReader.Null,
            null,
            CancellationToken.None,
            SettingsPath,
            Store,
            Path("logs"));

        said = output + error.ToString();
        lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    [Then("{string} shows a gray field")]
    public void ThenAGrayField(string patch)
    {
        code.ShouldBe(Exit.Ok, said);

        var written = PatchIO.Read(File.ReadAllText(Path(patch))).Patch;
        var knob = written.Nodes.Where(node => node.TypeId == "value").ShouldHaveSingleItem();

        written.Connections.ShouldContain(wire => wire.SourceNode == knob.Id && wire.TargetNode == written.Output.Id);
    }

    /// <summary>Asked for, at the end of what it was sent: a conversation's first message opens with the patch.</summary>
    [Then("the assistant remembers being asked for {string}")]
    public void ThenItRemembers(string earlier) => Assistant.Remembered.ShouldContain(asked => asked.EndsWith(earlier, StringComparison.Ordinal));

    [Then("the command is refused, naming {string}")]
    public void ThenRefused(string flag)
    {
        code.ShouldBe(Exit.Failed);
        said.ShouldContain(flag);
        said.ShouldContain("--help");
    }

    [Given("a decision model that reads every message as a question about a module")]
    public void GivenAQuestionReader() => reader = new QuestionReader();

    [Then("the assistant was told to answer rather than build")]
    public void ThenToldToAnswer() => Assistant.Sent.ShouldHaveSingleItem().ShouldContain(TurnReading.Answering);

    [Then("the assistant was asked nothing")]
    public void ThenAskedNothing() => Assistant.Asked.ShouldBe(0);

    [Then("the turn's last line counts its requests and the tokens they took")]
    public void ThenTheTurnIsCounted()
    {
        code.ShouldBe(Exit.Ok, said);

        var turn = lines.Select(line => JsonDocument.Parse(line).RootElement)
            .Last(line => line.GetProperty("kind").GetString() != "wrote");

        turn.GetProperty("kind").GetString().ShouldBe("turn");
        turn.GetProperty("requests").GetInt32().ShouldBe(1);
        turn.GetProperty("input").GetInt32().ShouldBe(Remembering.Input);
        turn.GetProperty("cacheRead").GetInt32().ShouldBe(Remembering.Cached);
        turn.GetProperty("output").GetInt32().ShouldBe(Remembering.Output);
    }

    /// <summary>What the editor does with a bundle it opens: finds the conversation, finds it resumable, and resumes it.</summary>
    [Then("opening {string} in the editor carries the conversation on")]
    public void ThenTheEditorCarriesItOn(string bundle)
    {
        code.ShouldBe(Exit.Ok, said);

        using var archive = File.OpenRead(Path(bundle));

        var loaded = PatchBundle.Read(archive, NodeCatalog.BuiltIn);
        var conversation = new AssistantConversation(() => loaded.Patch);

        conversation.Open(loaded.Conversation);

        var waiting = conversation.Waiting.ShouldNotBeNull();

        waiting.Unresumable(AssistantSettings.DefaultContextLimit, Assistant, SettingValues.None).ShouldBeNull();

        using var run = new AssistantRun(Assistant, AssistantConfig.Unset, NodeCatalog.BuiltIn, loaded.Patch, resuming: waiting);

        run.PickedUp.ShouldBeTrue();
        run.Turns.ShouldBe(1);
    }

    private string Path(string name) => System.IO.Path.Combine(folder.FullName, name);

    /// <summary>Builds a gray field through the workbench, and remembers what it was asked across a saved conversation.</summary>
    private sealed class Remembering : IPatchAssistant
    {
        /// <summary>What its one request reports spending.</summary>
        public const int Input = 1200, Cached = 1000, Output = 40;

        public List<string> Remembered { get; } = [];

        /// <summary>Every instruction it was sent, this run.</summary>
        public List<string> Sent { get; } = [];

        /// <summary>What it says to a message that asks it to build nothing, or null to build.</summary>
        public string? Brief { get; init; }

        public int Asked { get; set; }

        public string Id => "remembering";

        public string Name => "Remembering";

        public int Priority => 0;

        public AssistantCredential Credential => new("FLYBACK_REMEMBERING_KEY", "No key is needed.");

        public Uri Endpoint(SettingValues values) => new("https://assistant.test/");

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public AssistantSenses Senses(SettingValues values) => new(false);

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => new Session(this, workbench, []);

        public IPatchSession Resume(PatchWorkbench workbench, AssistantConfig config, string saved)
        {
            var history = JsonSerializer.Deserialize<List<string>>(saved) ?? [];

            Remembered.AddRange(history);

            return new Session(this, workbench, history);
        }

        private sealed class Session(Remembering owner, PatchWorkbench workbench, List<string> history) : IPatchSession
        {
            public async IAsyncEnumerable<PatchEvent> Ask(string instruction, [EnumeratorCancellation] CancellationToken cancel)
            {
                history.Add(instruction);
                owner.Sent.Add(instruction);
                owner.Asked++;

                if (owner.Brief is { } brief)
                {
                    yield return new PatchEvent.Said(brief);
                    yield break;
                }

                yield return new PatchEvent.Cost(Input, Cached, Output);

                string[] calls =
                [
                    """{"type_id":"value","handle":"knob1","knobs":[{"port":"value","value":0.5}]}""",
                    """{"from":"knob1","to":"output1","to_port":"color"}""",
                    """{"summary":"a flat gray field"}""",
                ];

                string[] tools = ["add_module", "connect", "propose"];

                for (var i = 0; i < tools.Length; i++)
                {
                    var outcome = await workbench
                        .InvokeAsync(tools[i], JsonSerializer.Deserialize<JsonElement>(calls[i]), cancel)
                        .ConfigureAwait(false);

                    yield return new PatchEvent.Did(outcome.Text);
                }

                yield return new PatchEvent.Proposed(workbench.Snapshot(), workbench.ProposalSummary);
            }

            public string? Save() => JsonSerializer.Serialize(history);

            public void Dispose()
            {
            }
        }
    }

    /// <summary>Reads every message as a question about a module, surely.</summary>
    private sealed class QuestionReader : IDecisionModel
    {
        public string Id => "question-reader";

        public string Name => "Question reader";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel) =>
            Task.FromResult(new Decision("reader", request.Questions.ToDictionary(q => q.Key, q => q.Value switch
            {
                Question.Choice choice => (Answer)new Answer.Chosen("module", choice.Options.ToDictionary(o => o.Label, o => o.Label == "module" ? 0.9 : 0.02), 0.9),
                _ => new Answer.YesNo(1),
            }), DecisionUsage.None));
    }
}
