namespace Flyback.Plugins.Assist;

/// <summary>
/// A provider that can be asked what its endpoint actually accepts.
/// </summary>
/// <remarks>
/// Separate from <see cref="IPatchAssistant"/> because whether a provider can answer
/// at all is a fact about the provider, and an interface it had to implement and
/// refuse would be worse than one it does not implement. Every question is a real
/// request against a real key, which is why this is the one thing on the boundary that
/// takes a <see cref="CancellationToken"/> and reports as it goes.
/// </remarks>
public interface IModelSurvey
{
    /// <summary>
    /// Asks the endpoint about each candidate and answers with what it accepted.
    /// </summary>
    /// <remarks>
    /// Only models that answered at all come back. A model the catalog lists
    /// and the endpoint refuses is not a model with no senses — it is not a
    /// model here — and the difference is the whole reason for asking.
    /// </remarks>
    /// <param name="said">Told what is being probed, one line at a time.</param>
    Task<IReadOnlyList<ModelReport>> Survey(
        AssistantConfig config,
        SurveyOptions options,
        IProgress<string>? said = null,
        CancellationToken cancel = default);
}