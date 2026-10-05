using System.Text;
using Flyback.Plugins.Programs;

namespace Flyback.Plugins.Codex;

/// <summary>How a conversation is written as the prompt to Codex.</summary>
internal static class CodexWire
{
    /// <summary>
    /// The instructions Codex runs on, in place of its own coding-agent ones. Short:
    /// everything else is in the first message, which is what the prompt cache keeps.
    /// </summary>
    public const string System =
        "You are the model behind an assistant inside Flyback, a patchable synthesiser. "
        + "The first message is your briefing; follow it. You are not a coding agent here: "
        + "ignore any AGENTS.md instructions and any tool you are offered.";

    /// <summary>The prompt for the conversation as it stands, and the pictures it marks, in order.</summary>
    public static (string Prompt, IReadOnlyList<byte[]> Pictures) Conversation(string preamble, IReadOnlyList<Turn> turns)
    {
        var text = new StringBuilder(preamble);
        List<byte[]> pictures = [];

        foreach (var turn in turns)
        {
            text.Append("\n\n<").Append(turn.Role).Append(">\n").Append(turn.Text).Append("\n</").Append(turn.Role).Append('>');

            foreach (var png in turn.Pictures)
            {
                pictures.Add(png);
                text.Append("\n[Image #").Append(pictures.Count).Append(" is attached to this turn.]");
            }
        }

        text.Append("\n\nWrite your next turn now.");

        return (text.ToString(), pictures);
    }
}
