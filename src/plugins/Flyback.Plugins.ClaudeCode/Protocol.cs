using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.ClaudeCode;

/// <summary>
/// How a conversation is written to Claude Code and its calls read back.
/// </summary>
/// <remarks>
/// Claude Code is asked as a plain model with its own tools off, so the workbench's
/// tools are offered in words and called in a <c>&lt;calls&gt;</c> block at the end
/// of a reply. Pure functions over text, since this is the part that fails by a
/// model not following it.
/// </remarks>
internal static class Protocol
{
    public const string Open = "<calls>";
    public const string Close = "</calls>";

    /// <summary>The name a block that would not read is called under, answered by <see cref="ClaudeCodeSession"/> with why.</summary>
    public const string Malformed = "malformed_calls";

    /// <summary>
    /// The system prompt: short, because it is an argument and a command line is
    /// short on Windows. Everything else is in the first message.
    /// </summary>
    public const string System =
        "You are the model behind an assistant inside Flyback, a patchable synthesiser. "
        + "The first message is your briefing; follow it.";

    /// <summary>What the model is told before the conversation: the briefing, the way to call tools and the tools.</summary>
    public static string Preamble(string briefing, IReadOnlyList<PatchTool> tools)
    {
        var text = new StringBuilder(briefing.TrimEnd());

        text.Append("\n\n# How to use the tools\n\n")
            .Append("You have no tool API here. To act, end your reply with one block holding a JSON array of calls, ")
            .Append("run in order:\n\n")
            .Append(Open).Append("\n[{\"name\": \"add_module\", \"arguments\": {\"type_id\": \"value\"}}]\n").Append(Close)
            .Append("\n\nWrite nothing after the block. Flyback runs the calls and answers in the next message. ")
            .Append("A reply with no block ends your turn, so say what you did in plain words when you are done. ")
            .Append("Never describe a call you could make; make it.\n\n")
            .Append("The conversation follows as <person>, <you> and <flyback> entries: <person> is who you are ")
            .Append("working for, <you> is what you said before, <flyback> is the answer to your calls.\n\n")
            .Append("# Tools\n");

        foreach (var tool in tools)
        {
            text.Append("\n## ").Append(tool.Name).Append('\n')
                .Append(tool.Description.Trim()).Append("\n\nArguments, as the body of a JSON Schema object: ")
                .Append(tool.Schema.Trim()).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The message sent for the conversation as it stands.</summary>
    public static JsonArray Content(string preamble, IReadOnlyList<Turn> turns)
    {
        var blocks = new JsonArray { Text(preamble) };

        foreach (var turn in turns)
        {
            blocks.Add(Text($"<{turn.Role}>\n{turn.Text}\n</{turn.Role}>"));

            foreach (var png in turn.Pictures)
            {
                blocks.Add(new JsonObject
                {
                    ["type"] = "image",
                    ["source"] = new JsonObject
                    {
                        ["type"] = "base64",
                        ["media_type"] = "image/png",
                        ["data"] = Convert.ToBase64String(png),
                    },
                });
            }
        }

        blocks.Add(Text("Write your next turn now."));

        return blocks;
    }

    /// <summary>The answers to a reply's calls, as one <c>flyback</c> entry's text.</summary>
    public static string Answers(IReadOnlyList<ToolAnswer> answers)
    {
        var text = new StringBuilder();

        for (var i = 0; i < answers.Count; i++)
        {
            var call = answers[i].Call;

            text.Append(i == 0 ? string.Empty : "\n\n")
                .Append("Call ").Append(i + 1).Append(" (").Append(call.Name).Append("): ")
                .Append(call.Name == Malformed ? call.Arguments : answers[i].Text);

            if (answers[i].Png is not null) text.Append("\n[Its picture follows.]");
        }

        return text.ToString();
    }

    /// <summary>A reply as what was said and what was called.</summary>
    public static (string? Text, IReadOnlyList<ToolCall> Calls) Parse(string reply)
    {
        var start = reply.LastIndexOf(Open, StringComparison.Ordinal);

        if (start < 0) return (Spoken(reply), []);

        var said = Spoken(reply[..start]);
        var close = reply.IndexOf(Close, start, StringComparison.Ordinal);

        if (close < 0)
            return (said, [Rejected($"the {Open} block was never closed with {Close}.")]);

        try
        {
            var body = reply[(start + Open.Length)..close].Trim();

            if (JsonNode.Parse(body) is not JsonArray array)
                return (said, [Rejected($"the {Open} block must hold a JSON array of calls.")]);

            var calls = new List<ToolCall>();

            foreach (var item in array)
            {
                if (item is not JsonObject { } call || call["name"] is not JsonValue name
                    || !name.TryGetValue<string>(out var tool) || string.IsNullOrWhiteSpace(tool))
                    return (said, [Rejected("every call needs a string \"name\".")]);

                var arguments = call["arguments"] switch
                {
                    null => "{}",
                    JsonValue value when value.TryGetValue<string>(out var text) => text,
                    var node => node.ToJsonString(),
                };

                calls.Add(new ToolCall($"call{calls.Count + 1}", tool, arguments));
            }

            return (said, calls);
        }
        catch (JsonException ex)
        {
            return (said, [Rejected($"the {Open} block was not valid JSON: {ex.Message}")]);
        }
    }

    private static ToolCall Rejected(string why) =>
        new("call1", Malformed, $"nothing ran: {why} Send the calls again in the form described in the briefing.");

    private static string? Spoken(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };
}
