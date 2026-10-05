using System.Text.Json.Nodes;
using Flyback.Plugins.Programs;

namespace Flyback.Plugins.ClaudeCode;

/// <summary>How a conversation is written as the content of one message to Claude Code.</summary>
internal static class ClaudeWire
{
    /// <summary>
    /// The system prompt: short, because it is an argument and a command line is
    /// short on Windows. Everything else is in the first message.
    /// </summary>
    public const string System =
        "You are the model behind an assistant inside Flyback, a patchable synthesiser. "
        + "The first message is your briefing; follow it.";

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

    private static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };
}
