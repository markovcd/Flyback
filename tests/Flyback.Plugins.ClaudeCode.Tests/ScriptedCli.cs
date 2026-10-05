using System.Text.Json.Nodes;

namespace Flyback.Plugins.ClaudeCode.Tests;

/// <summary>Claude Code replaced by replies written out in advance, remembering what it was sent.</summary>
internal sealed class ScriptedCli(params string[] replies) : IClaudeCli
{
    private readonly Queue<string> left = new(replies);

    /// <summary>The content of each question, as sent.</summary>
    public List<JsonArray> Sent { get; } = [];

    /// <summary>Every text block of the last question, joined.</summary>
    public string LastText => TextOf(Sent[^1]);

    /// <summary>Every text block of <paramref name="content"/>, joined.</summary>
    public static string TextOf(JsonArray content) =>
        string.Join("\n", content.Select(b => (string?)b!["text"]).OfType<string>());

    public Task<ClaudeAnswer> Ask(ClaudeRequest request, CancellationToken cancel)
    {
        Sent.Add((JsonArray)request.Content.DeepClone());

        return left.TryDequeue(out var reply)
            ? Task.FromResult(new ClaudeAnswer(reply, 100, 50, 10))
            : throw new ClaudeCodeFailure("nothing left to say.");
    }
}
