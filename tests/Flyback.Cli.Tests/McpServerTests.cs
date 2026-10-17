using System.Text.Json.Nodes;
using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary><c>mcp</c>: JSON-RPC lines in, answers out, each tool call run on the patch file as <c>tool</c> runs it.</summary>
public sealed class McpServerTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-mcp");

    private static readonly PluginCatalog Catalog = new([], [], NodeCatalog.BuiltIn, Presets.All, [], []);

    private readonly StringWriter log = new();

    private readonly McpServer server;

    public McpServerTests() =>
        server = new McpServer(Catalog, new FileTarget(Catalog, new ToolHistory(Path("history")), new FileInfo(Path("p.fbk")), log, Path("settings.json")), Path("settings.json"));

    public void Dispose() => folder.Delete(recursive: true);

    [Fact]
    public async Task Initialize_answers_in_the_version_asked_for_with_the_briefing_as_instructions()
    {
        var result = (await Ask("initialize", new JsonObject { ["protocolVersion"] = "2025-03-26" }))["result"]!;

        result["protocolVersion"]!.GetValue<string>().ShouldBe("2025-03-26");
        result["capabilities"]!["tools"].ShouldNotBeNull();
        result["instructions"]!.GetValue<string>().ShouldContain("osc.sine");
    }

    [Fact]
    public async Task Initialize_in_a_version_it_does_not_know_answers_in_its_newest()
    {
        var result = (await Ask("initialize", new JsonObject { ["protocolVersion"] = "1999-01-01" }))["result"]!;

        result["protocolVersion"]!.GetValue<string>().ShouldBe(McpServer.Versions[0]);
    }

    [Fact]
    public async Task A_notification_is_never_answered() =>
        (await server.Answer("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", CancellationToken.None)).ShouldBeNull();

    [Fact]
    public async Task The_tools_are_listed_with_object_schemas()
    {
        var tools = (await Ask("tools/list"))["result"]!["tools"]!.AsArray();

        tools.Select(tool => tool!["name"]!.GetValue<string>()).ShouldContain("write_patch");
        tools.ShouldAllBe(tool => tool!["inputSchema"]!["type"]!.GetValue<string>() == "object");
    }

    [Fact]
    public async Task A_call_edits_the_file_and_the_next_call_sees_the_edit()
    {
        await Call("write_patch", new JsonObject { ["source"] = "rings() |> out.color" });

        var result = await Call("add_module", new JsonObject { ["type_id"] = "osc.sine" });

        result["isError"]!.GetValue<bool>().ShouldBeFalse(result.ToJsonString());

        var saved = PatchIO.Read(File.ReadAllText(Path("p.fbk"))).Patch;

        saved.Nodes.ShouldContain(node => node.TypeId == "pattern.rings");
        saved.Nodes.ShouldContain(node => node.TypeId == "osc.sine");
        new ToolHistory(Path("history")).Find(Path("p.fbk")).ShouldNotBeNull().At.ShouldBe(2);
    }

    [Fact]
    public async Task A_render_comes_back_as_an_image()
    {
        await Call("write_patch", new JsonObject { ["source"] = "rings() |> out.color" });

        var content = (await Call("render", []))["content"]!.AsArray();

        content.ShouldContain(part => part!["type"]!.GetValue<string>() == "image" && part["mimeType"]!.GetValue<string>() == "image/png");
    }

    [Fact]
    public async Task Say_is_listed_and_writes_what_it_is_handed_where_the_person_reads_it()
    {
        var tools = (await Ask("tools/list"))["result"]!["tools"]!.AsArray();
        tools.ShouldContain(tool => tool!["name"]!.GetValue<string>() == "say");

        var result = await Call("say", new JsonObject { ["text"] = "Built a slow drone in A." });

        result["isError"]!.GetValue<bool>().ShouldBeFalse();
        log.ToString().ShouldContain("Built a slow drone in A.");
        File.Exists(Path("p.fbk")).ShouldBeFalse();
    }

    [Fact]
    public async Task Say_with_nothing_to_say_is_a_tool_error() =>
        (await Call("say", []))["isError"]!.GetValue<bool>().ShouldBeTrue();

    [Fact]
    public async Task A_refused_call_is_a_tool_error_saying_why()
    {
        var result = await Call("set_knobs", new JsonObject { ["handle"] = "nobody" });

        result["isError"]!.GetValue<bool>().ShouldBeTrue();
        result["content"]![0]!["text"]!.GetValue<string>().ShouldContain("nobody");
    }

    [Fact]
    public async Task Arguments_that_are_not_an_object_are_invalid_params()
    {
        var answer = await Ask("tools/call", new JsonObject { ["name"] = "describe_patch", ["arguments"] = new JsonArray() });

        answer["error"]!["code"]!.GetValue<int>().ShouldBe(-32602);
    }

    [Fact]
    public async Task A_line_that_is_not_json_is_a_parse_error()
    {
        var answer = (await server.Answer("not json", CancellationToken.None)).ShouldNotBeNull();

        answer["error"]!["code"]!.GetValue<int>().ShouldBe(-32700);
    }

    [Fact]
    public async Task An_unknown_method_is_method_not_found() =>
        (await Ask("resources/list"))["error"]!["code"]!.GetValue<int>().ShouldBe(-32601);

    [Fact]
    public async Task Serve_answers_each_request_on_a_line_of_its_own()
    {
        var input = new StringReader("""
            {"jsonrpc":"2.0","id":1,"method":"ping"}
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            {"jsonrpc":"2.0","id":"two","method":"ping"}
            """);
        var output = new StringWriter();

        await server.Serve(input, output, CancellationToken.None);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(2);
        JsonNode.Parse(lines[1])!["id"]!.GetValue<string>().ShouldBe("two");
    }

    [Fact]
    public async Task A_blank_line_between_messages_is_not_answered()
    {
        var output = new StringWriter();

        await server.Serve(new StringReader("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}\n\n  \n{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\"}\n"), output, CancellationToken.None);

        var answers = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToList();

        answers.Select(answer => answer["id"]!.GetValue<int>()).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task A_patch_file_that_cannot_be_read_is_a_tool_error_saying_why()
    {
        File.WriteAllText(Path("p.fbk"), "not a patch");

        var result = await Call("describe_patch", []);

        result["isError"]!.GetValue<bool>().ShouldBeTrue(result.ToJsonString());
        result["content"]![0]!["text"]!.GetValue<string>().ShouldContain("p.fbk");
    }

    private async Task<JsonObject> Call(string tool, JsonObject arguments) =>
        (JsonObject)(await Ask("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments }))["result"]!;

    private async Task<JsonObject> Ask(string method, JsonObject? parameters = null)
    {
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 7, ["method"] = method };

        if (parameters is not null) request["params"] = parameters;

        return (await server.Answer(request.ToJsonString(), CancellationToken.None)).ShouldNotBeNull();
    }

    private string Path(string name) => System.IO.Path.Combine(folder.FullName, name);
}
