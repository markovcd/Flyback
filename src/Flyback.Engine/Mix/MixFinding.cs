namespace Flyback.Engine.Mix;

/// <summary>One thing a mix check found.</summary>
/// <param name="Problem">What kind of thing.</param>
/// <param name="Severity">How much it matters.</param>
/// <param name="Text">What was found and what to do about it, in a sentence or two.</param>
/// <param name="Modules">The modules it is about, if any.</param>
public sealed record MixFinding(MixProblem Problem, MixSeverity Severity, string Text, IReadOnlyList<Guid> Modules)
{
    /// <summary>The finding's kind as a script reads it: <c>no-master-chain</c>.</summary>
    public string Code => MixWords.Code(Problem);
}
