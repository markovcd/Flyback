using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Cli.Commands;
using Flyback.Core;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli.Common;

/// <summary>
/// The workbench's tools served over MCP on standard input and output, one JSON-RPC
/// message a line, each call run on a patch file as <c>flyback-cli tool</c> runs it (ADR-0202).
/// </summary>
/// <remarks>
/// Only what a client needs to call tools: <c>initialize</c>, <c>ping</c>, <c>tools/list</c>
/// and <c>tools/call</c>. Notifications are read and never answered. <c>say</c> is the
/// server's own tool, beside the workbench's.
/// </remarks>
internal sealed class McpServer(PluginCatalog plugins, ToolHistory history, FileInfo patch, TextWriter log, string? settingsPath = null)
{
    /// <summary>The protocol versions this answers in, newest first.</summary>
    internal static readonly string[] Versions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    private const int ParseError = -32700, InvalidRequest = -32600, MethodNotFound = -32601, InvalidParams = -32602;

    private readonly Lazy<Flyback.Plugins.Assist.PatchWorkbench> blank = new(() =>
        ToolCommand.Bench(plugins, Flyback.Engine.Graph.Presets.Empty(plugins.Modules), settingsPath));

    /// <summary>Answers each message on <paramref name="input"/> until it ends.</summary>
    public async Task Serve(TextReader input, TextWriter output, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested && await input.ReadLineAsync(cancel).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (await Answer(line, cancel).ConfigureAwait(false) is not { } answer) continue;

            await output.WriteAsync(answer.ToJsonString() + "\n").ConfigureAwait(false);
            await output.FlushAsync(cancel).ConfigureAwait(false);
        }
    }

    /// <summary>The reply to one line, or null where it was a notification.</summary>
    internal async Task<JsonObject?> Answer(string line, CancellationToken cancel)
    {
        JsonNode? message;

        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException ex)
        {
            return Error(null, ParseError, $"not JSON: {ex.Message}");
        }

        if (message is not JsonObject request || request["method"] is not JsonValue method || !method.TryGetValue(out string? name))
            return Error(null, InvalidRequest, "a request is a JSON object with a method.");

        var id = request["id"]?.DeepClone();

        // A notification has no id, and nothing is ever sent back for one.
        if (id is null) return null;

        var parameters = request["params"] as JsonObject;

        try
        {
            return name switch
            {
                "initialize" => Result(id, Initialize(parameters)),
                "ping" => Result(id, []),
                "tools/list" => Result(id, List()),
                "tools/call" => await Call(id, parameters, cancel).ConfigureAwait(false),
                _ => Error(id, MethodNotFound, $"there is no method called '{name}'."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(id, -32603, ex.Message);
        }
    }

    private JsonObject Initialize(JsonObject? parameters)
    {
        var asked = (parameters?["protocolVersion"] as JsonValue)?.TryGetValue(out string? version) == true ? version : null;

        return new JsonObject
        {
            ["protocolVersion"] = Versions.Contains(asked) ? asked : Versions[0],
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "flyback",
                ["title"] = GlobalConstants.ApplicationName,
                ["version"] = typeof(McpServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0",
            },
            ["instructions"] = $"Every tool edits or reads the patch {patch.FullName}, which is saved after each edit. "
                + "Undo with `flyback-cli tool undo` on that file.\n\n" + blank.Value.Briefing,
        };
    }

    /// <summary>The one tool the server answers itself rather than the workbench: what the person is told.</summary>
    internal const string Say = "say";

    private static readonly JsonObject SayTool = new()
    {
        ["name"] = Say,
        ["description"] = "Tells the person something: what you built, what you found, or a question. "
            + "Say it here rather than only in your own reply, since the person may be watching Flyback and not you.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject { ["type"] = "string", ["description"] = "What to say, in a sentence or a short paragraph." },
            },
            ["required"] = new JsonArray("text"),
        },
    };

    private JsonObject List() => new()
    {
        ["tools"] = new JsonArray([
            .. blank.Value.Tools.Select(tool => (JsonNode)new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = ToolCommand.Schema(tool),
            }),
            SayTool.DeepClone(),
        ]),
    };

    /// <summary>What <c>say</c> was handed, written where the person reads it: standard error, headless.</summary>
    private async Task<JsonObject> Said(JsonNode id, JsonObject? arguments)
    {
        if ((arguments?["text"] as JsonValue)?.TryGetValue(out string? text) != true || string.IsNullOrWhiteSpace(text))
            return Result(id, Content([Text("'text' is required: what to tell the person.")], error: true));

        await log.WriteLineAsync(text.Trim()).ConfigureAwait(false);
        await log.FlushAsync().ConfigureAwait(false);

        return Result(id, Content([Text("said.")], error: false));
    }

    private async Task<JsonObject> Call(JsonNode id, JsonObject? parameters, CancellationToken cancel)
    {
        if ((parameters?["name"] as JsonValue)?.TryGetValue(out string? tool) != true || string.IsNullOrEmpty(tool))
            return Error(id, InvalidParams, "tools/call needs the tool's name.");

        if (parameters!["arguments"] is not null and not JsonObject)
            return Error(id, InvalidParams, "a tool's arguments are a JSON object.");

        if (tool == Say) return await Said(id, parameters["arguments"] as JsonObject).ConfigureAwait(false);

        using var arguments = JsonDocument.Parse(parameters["arguments"]?.ToJsonString() ?? "{}");
        var said = new StringWriter();

        var ran = await ToolCommand
            .Dispatch(plugins, history, patch, tool, arguments.RootElement, said, cancel, settingsPath)
            .ConfigureAwait(false);

        if (ran is null) return Result(id, Content([Text(said.ToString().Trim())], error: true));

        if (said.ToString() is { Length: > 0 } aside) await log.WriteAsync(aside).ConfigureAwait(false);

        List<JsonNode> content = [Text(ran.Outcome.Text)];

        if (ran.Outcome.Png is { } png)
            content.Add(new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(png), ["mimeType"] = "image/png" });

        return Result(id, Content(content, error: !ran.Outcome.Ok));
    }

    private static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };

    private static JsonObject Content(List<JsonNode> content, bool error) => new()
    {
        ["content"] = new JsonArray([.. content]),
        ["isError"] = error,
    };

    private static JsonObject Result(JsonNode id, JsonObject result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result,
    };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}
