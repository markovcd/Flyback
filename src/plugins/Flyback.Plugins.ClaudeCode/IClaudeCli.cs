namespace Flyback.Plugins.ClaudeCode;

/// <summary>Asks Claude Code one question and waits for its answer.</summary>
internal interface IClaudeCli
{
    /// <exception cref="ClaudeCodeFailure">It was not installed, not signed in, or said no.</exception>
    Task<ClaudeAnswer> Ask(ClaudeRequest request, CancellationToken cancel);
}
