namespace Flyback.Plugins.Codex.Tests;

/// <summary>Codex replaced by replies written out in advance, remembering what it was sent.</summary>
internal sealed class ScriptedCli(params string[] replies) : ICodexCli
{
    private readonly Queue<string> left = new(replies);

    /// <summary>Each question, as sent.</summary>
    public List<CodexRequest> Sent { get; } = [];

    /// <summary>The last question's prompt.</summary>
    public string LastText => Sent[^1].Prompt;

    public Task<CodexAnswer> Ask(CodexRequest request, CancellationToken cancel)
    {
        Sent.Add(request);

        return left.TryDequeue(out var reply)
            ? Task.FromResult(new CodexAnswer(reply, 100, 50, 10))
            : throw new CodexFailure("nothing left to say.");
    }
}
