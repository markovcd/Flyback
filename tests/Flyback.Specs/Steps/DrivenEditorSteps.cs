using System.Text.Json.Nodes;
using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>
/// <c>flyback-cli mcp --editor</c> driving the headless editor over a channel of the
/// scenario's own, as a coding assistant drives the one somebody has open.
/// </summary>
[Binding]
public sealed class DrivenEditorSteps : IDisposable
{
    private readonly EditorDriver editor;

    private static readonly PluginCatalog Catalog = new([], [], NodeCatalog.BuiltIn, Presets.All, [], []);

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("fb-drive");

    private List<JsonNode> answers = [];

    /// <summary>The program's end of the channel, held open from its first call until it closes.</summary>
    private EditorTarget? target;

    private string Channel => OperatingSystem.IsWindows()
        ? $"flyback-specs-{folder.Name}"
        : Path.Combine(folder.FullName, "e.sock");

    /// <summary>The editor keeps its settings in the scenario's folder, so the switch has somewhere to be saved.</summary>
    public DrivenEditorSteps(EditorDriver editor)
    {
        this.editor = editor;
        editor.Setup = editor.Setup with
        {
            Folders = editor.Setup.Folders with { SettingsPath = Path.Combine(folder.FullName, "editor.json") },
        };
    }

    public void Dispose()
    {
        target?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        folder.Delete(recursive: true);
    }

    [Given("the editor lets a program on this computer edit the open patch")]
    public void GivenDrivable() => editor.LetAProgramDrive(Channel);

    [When("{string} adds a sine to the open patch over flyback-cli mcp --editor")]
    public void WhenAdds(string client) =>
        Serve(client, new JsonObject { ["name"] = "add_module", ["arguments"] = new JsonObject { ["type_id"] = "osc.sine" } });

    [When("{string} says {string} to the editor over flyback-cli mcp --editor")]
    public void WhenSays(string client, string text) =>
        Serve(client, new JsonObject { ["name"] = "say", ["arguments"] = new JsonObject { ["text"] = text } });

    [Given("the editor has not been let edit by a program")]
    public void GivenNotLet() => editor.ChannelAt(Channel);

    [Then("the editor keeps letting programs edit it, next time it starts")]
    public void ThenKept() => editor.LetsProgramsEdit.ShouldBeTrue();

    [Then("the call is refused, saying to turn on {string}")]
    public void ThenRefusedOff(string setting)
    {
        var answer = answers.ShouldHaveSingleItem();

        answer["result"]!["isError"]!.GetValue<bool>().ShouldBeTrue();
        answer["result"]!["content"]![0]!["text"]!.GetValue<string>().ShouldContain(setting);
    }

    [Given("the assistant's column is open")]
    public void GivenColumn() => editor.OpenAssistant(null);

    [When("the program closes its end")]
    public void WhenCloses()
    {
        editor.Turning(async () =>
        {
            await target!.DisposeAsync();
            return true;
        });
        target = null;
    }

    [Then("the column says it is driven by {string}")]
    public void ThenDrivenBy(string client) => editor.AssistantTexts.ShouldContain($"Driven by {client}");

    [Then("the column shows the call, and has no box to type in")]
    public void ThenShowsCall()
    {
        editor.WaitForComposer(shown: false);
        editor.TranscriptLines.ShouldContain(line => line.Contains("osc.sine"));
    }

    [Then("the column says {string} stopped driving, and has its box back")]
    public void ThenReleased(string client)
    {
        editor.WaitForComposer(shown: true);
        editor.TranscriptLines.ShouldContain($"{client} stopped driving the editor.");
    }

    [Then("the column shows {string} as what was said")]
    public void ThenSaid(string text) => editor.TranscriptLines.ShouldContain(text);

    [Then("the call was answered without an error")]
    public void ThenAnswered()
    {
        var answer = answers.ShouldHaveSingleItem();

        answer["result"]!["isError"]!.GetValue<bool>().ShouldBeFalse(answer.ToJsonString());
    }

    [Then("the canvas holds a sine")]
    public void ThenSine() => editor.CanvasTypes.ShouldContain("osc.sine");

    [Then("the canvas holds no sine")]
    public void ThenNoSine() => editor.CanvasTypes.ShouldNotContain("osc.sine");

    [Then("the editor's status line says {string}")]
    public void ThenReported(string text) => editor.Reported.ShouldContain(text);

    private void Serve(string client, JsonObject call)
    {
        string[] lines =
        [
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new JsonObject { ["protocolVersion"] = "2025-06-18", ["clientInfo"] = new JsonObject { ["name"] = client } },
            }.ToJsonString(),
            new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 2, ["method"] = "tools/call", ["params"] = call }.ToJsonString(),
        ];

        var output = editor.Turning(async () =>
        {
            var written = new StringWriter();

            // A channel that stops answering fails the step rather than hanging the run.
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            target ??= new EditorTarget(Channel);

            await new McpServer(Catalog, target, Path.Combine(folder.FullName, "settings.json"))
                .Serve(new StringReader(string.Join('\n', lines)), written, cancel.Token);

            return written;
        });

        answers = [.. output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).Skip(1)];
    }
}
