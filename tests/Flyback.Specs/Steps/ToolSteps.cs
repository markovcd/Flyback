using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Cli.Commands;
using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Plugins.Hosting;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>
/// <c>flyback-cli tool</c> called the way a coding assistant calls it, one tool at a time
/// on a patch file, with its undo history kept in a folder of the scenario's own.
/// </summary>
[Binding]
public sealed class ToolSteps : IDisposable
{
    private static readonly PluginCatalog Catalog = new([], [], NodeCatalog.BuiltIn, Presets.All, [], []);

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-tool-specs");

    private string patch = string.Empty;
    private int code;
    private string complained = string.Empty;
    private List<JsonNode> answers = [];

    private ToolHistory History => new(Path("history"));

    public void Dispose() => folder.Delete(recursive: true);

    [When("a coding assistant writes a patch of rings into {string}")]
    public Task WhenWritesRings(string name)
    {
        patch = name;
        return Call("write_patch", JsonSerializer.Serialize(new { source = "rings() |> out.color" }));
    }

    [When("it adds a sine it calls {string}")]
    public Task WhenAddsSine(string handle) =>
        Call("add_module", JsonSerializer.Serialize(new { type_id = "osc.sine", handle }));

    [When("it wires {string} into the rings across")]
    public Task WhenWires(string handle) =>
        Call("connect", JsonSerializer.Serialize(new { from = handle, to = "rings1", to_port = "x" }));

    [When("it turns a knob on a module called {string}")]
    public Task WhenTurnsKnob(string handle) =>
        Call("set_knobs", JsonSerializer.Serialize(new { handle, knobs = new[] { new { port = "freq", value = 2 } } }), expectOk: false);

    [Given("a coding assistant has built rings moved across by a sine in {string}")]
    public async Task GivenBuilt(string name)
    {
        await WhenWritesRings(name);
        await WhenAddsSine("lfo");
        await WhenWires("lfo");
    }

    [When("a coding assistant builds rings moved across by a sine in {string} over flyback-cli mcp")]
    public async Task WhenServed(string name)
    {
        patch = name;

        string[] lines =
        [
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"specs","version":"1"}}}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"write_patch","arguments":{"source":"rings() |> out.color"}}}""",
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"add_module","arguments":{"type_id":"osc.sine","handle":"lfo"}}}""",
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"connect","arguments":{"from":"lfo","to":"rings1","to_port":"x"}}}""",
        ];

        var output = new StringWriter();

        await new McpServer(Catalog, History, new FileInfo(Path(name)), TextWriter.Null, Path("settings.json"))
            .Serve(new StringReader(string.Join('\n', lines)), output, CancellationToken.None);

        answers = [.. output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!)];
    }

    [Then("every request it made was answered without an error")]
    public void ThenAnswered()
    {
        answers.Select(answer => answer["id"]!.GetValue<int>()).ShouldBe([1, 2, 3, 4, 5]);
        foreach (var answer in answers)
        {
            answer["error"].ShouldBeNull(answer.ToJsonString());
            (answer["result"]!["isError"]?.GetValue<bool>() ?? false).ShouldBeFalse(answer.ToJsonString());
        }
    }

    [When("flyback-cli tool undo is run on {string}")]
    public void WhenUndone(string name) =>
        ToolCommand.Undo(History, new FileInfo(Path(name)), null, false, TextWriter.Null, TextWriter.Null).ShouldBe(Exit.Ok);

    [When("flyback-cli tool redo is run on {string}")]
    public void WhenRedone(string name) =>
        ToolCommand.Redo(History, new FileInfo(Path(name)), null, false, TextWriter.Null, TextWriter.Null).ShouldBe(Exit.Ok);

    [Then("{string} is rings moved across by a sine")]
    public void ThenMoved(string name)
    {
        var (built, rings) = Read(name);
        var sine = built.Nodes.Where(node => node.TypeId == "osc.sine").ShouldHaveSingleItem();

        built.Connections.ShouldContain(wire =>
            wire.SourceNode == sine.Id && wire.TargetNode == rings.Id && Socket(rings, wire.TargetPort) == "x");
    }

    [Then("nothing moves the rings in {string}")]
    public void ThenStill(string name)
    {
        var (built, rings) = Read(name);

        built.Connections.ShouldNotContain(wire => wire.TargetNode == rings.Id);
    }

    [Then("the history lists the wire as undone")]
    public void ThenWireUndone()
    {
        var output = new StringWriter();
        ToolCommand.History(History, new FileInfo(Path(patch)), null, true, output, TextWriter.Null).ShouldBe(Exit.Ok);

        var last = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("steps").EnumerateArray().Last();

        last.GetProperty("tool").GetString().ShouldBe("connect");
        last.GetProperty("undone").GetBoolean().ShouldBeTrue();
    }

    [Then("the history has {int} edits")]
    public void ThenEdits(int count)
    {
        var log = History.Find(Path(patch)).ShouldNotBeNull();

        log.Steps.Count.ShouldBe(count);
        log.At.ShouldBe(count);
    }

    [Then("the call is refused, saying there is no module called {string}")]
    public void ThenRefused(string handle)
    {
        code.ShouldBe(Exit.Problems);
        complained.ShouldContain($"there is no module called '{handle}'");
    }

    private async Task Call(string tool, string arguments, bool expectOk = true)
    {
        var error = new StringWriter();

        code = await ToolCommand.Call(
            Catalog,
            History,
            new FileInfo(Path(patch)),
            tool,
            arguments,
            null,
            null,
            false,
            TextWriter.Null,
            error,
            CancellationToken.None,
            Path("settings.json"));

        complained = error.ToString();

        if (expectOk) code.ShouldBe(Exit.Ok, complained);
    }

    private (Patch Patch, NodeInstance Rings) Read(string name)
    {
        var built = PatchLanguage.Build(File.ReadAllText(Path(name)), NodeCatalog.BuiltIn).Patch;

        return (built, built.Nodes.Where(node => node.TypeId == "pattern.rings").ShouldHaveSingleItem());
    }

    private static string Socket(NodeInstance node, int port) => NodeCatalog.BuiltIn.Get(node.TypeId)!.Inputs[port].Name;

    private string Path(string name) => System.IO.Path.Combine(folder.FullName, name);
}
