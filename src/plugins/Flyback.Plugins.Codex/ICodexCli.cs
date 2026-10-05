namespace Flyback.Plugins.Codex;

/// <summary>Asks Codex one question and waits for its answer.</summary>
internal interface ICodexCli
{
    /// <exception cref="CodexFailure">It was not installed, not signed in, or said no.</exception>
    Task<CodexAnswer> Ask(CodexRequest request, CancellationToken cancel);
}
