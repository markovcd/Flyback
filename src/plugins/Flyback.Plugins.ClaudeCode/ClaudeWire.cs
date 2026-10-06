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
    /// <remarks>
    /// The last turn carries the one cache breakpoint Claude Code leaves free, so the
    /// next request reads everything before its newest turn from the cache. Its own
    /// breakpoints are an hour long, and a shorter one ahead of them is refused. A
    /// turn's pictures go inside it, so it ends on text: a breakpoint on an image
    /// caches nothing the next request reads.
    /// </remarks>
    public static JsonArray Content(string preamble, IReadOnlyList<Turn> turns)
    {
        var blocks = new JsonArray { Text(preamble) };

        foreach (var turn in turns)
        {
            if (turn.Pictures.Count == 0)
            {
                blocks.Add(Text($"<{turn.Role}>\n{turn.Text}\n</{turn.Role}>"));
                continue;
            }

            blocks.Add(Text($"<{turn.Role}>\n{turn.Text}\n"));

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

            blocks.Add(Text($"</{turn.Role}>"));
        }

        blocks[^1]!["cache_control"] = new JsonObject { ["type"] = "ephemeral", ["ttl"] = "1h" };
        blocks.Add(Text("Write your next turn now."));

        return blocks;
    }

    private static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };
}
