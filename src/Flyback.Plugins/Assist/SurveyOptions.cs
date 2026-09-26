namespace Flyback.Plugins.Assist;

/// <summary>
/// What to ask, for a survey that costs a request per question.
/// </summary>
/// <param name="Only">
/// The models to probe, or empty for whichever the provider thinks are candidates. A
/// model named here is probed whether or not the endpoint listed it, because the two
/// disagree often enough to be worth checking.
/// </param>
/// <param name="All">Probe everything listed rather than the provider's own shortlist.</param>
/// <param name="Bounds">
/// Measure what each model will think for. Off by default: finding out costs a request
/// per step and makes the model think for real near the top of its range.
/// </param>
public sealed record SurveyOptions(
    IReadOnlyList<string>? Only = null,
    bool All = false,
    bool Bounds = false)
{
    /// <summary>
    /// Ask about the one model this configuration is already set to, and nothing
    /// else.
    /// </summary>
    /// <remarks>
    /// The question a window can ask and a command cannot: which of a provider's
    /// settings names the model is the provider's own business (ADR-0069), so the
    /// caller says which model it means by saying "the chosen one" and the
    /// provider turns that into a name — see <see cref="AssistantSchema.Asking"/>.
    /// A provider that has not heard of this probes its shortlist as before,
    /// which costs money rather than answers, so the ordinary shape resolves it
    /// in one shared place.
    /// </remarks>
    public bool Chosen { get; init; }
}