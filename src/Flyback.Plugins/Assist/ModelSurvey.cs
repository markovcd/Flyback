using System.Text.Json;

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

/// <summary>
/// What a survey found, on its way into and out of a provider's settings.
/// </summary>
/// <remarks>
/// One string under one key, because that is the only shape the settings file has
/// (ADR-0069): a list of models is not a string, so it is written as one here and read
/// back here. The file is hand-edited, so <see cref="Read"/> never throws and never
/// half-succeeds — anything it cannot make sense of is nothing at all.
/// </remarks>
public static class Survey
{
    /// <summary>
    /// What the survey is filed under. Stable — it is in the settings file of
    /// everybody who has ever run one.
    /// </summary>
    public const string Key = "models";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Write(IEnumerable<ModelReport> found) =>
        JsonSerializer.Serialize(found.ToArray(), Options);

    /// <summary>Never throws, and never returns half a list.</summary>
    public static IReadOnlyList<ModelReport> Read(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return [];

        try
        {
            var found = JsonSerializer.Deserialize<ModelReport[]>(stored, Options);

            return found is null
                ? []
                : found.Where(m => !string.IsNullOrWhiteSpace(m.Id)).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
