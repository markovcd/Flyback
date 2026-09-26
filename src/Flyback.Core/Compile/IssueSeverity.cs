namespace Flyback.Core.Compile;

/// <summary>
/// How much an issue ought to stop something. <see cref="Error"/> is first so
/// that a new complaint nobody has weighed blocks rather than slipping through.
/// </summary>
public enum IssueSeverity
{
    /// <summary>The patch is wrong here. What compiled is a stand-in.</summary>
    Error,

    /// <summary>
    /// Worth saying, but not wrong. A patch that trips only warnings is one
    /// somebody may have meant, and is still worth offering.
    /// </summary>
    Warning,
}