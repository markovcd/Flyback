using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flyback.Plugins.Assist;

/// <summary>
/// One model as an endpoint answered for it, rather than as somebody wrote it
/// down.
/// </summary>
/// <remarks>
/// The same facts <see cref="AssistantModel"/> carries, plus the two a
/// suggestion cannot hold because they are arithmetic rather than a claim: what
/// the model will think for. A list of these is what a provider knows about an
/// endpoint after asking it, and what <see cref="Survey"/> keeps in the
/// settings so it need not be asked again.
/// </remarks>
/// <param name="Id">What goes in the request.</param>
public sealed record ModelReport(string Id)
{
    /// <summary>Whether a picture was accepted.</summary>
    public bool Vision { get; init; } = true;

    /// <summary>Whether a sound was accepted.</summary>
    public bool Hearing { get; init; }

    /// <summary>The smallest thinking budget taken, where anybody measured.</summary>
    public int? Least { get; init; }

    /// <summary>The largest, where anybody measured.</summary>
    public int? Most { get; init; }

    /// <summary>What a form can be built from, which is the report minus the arithmetic.</summary>
    /// <remarks>
    /// Kept out of the file it is written to. It is derived from three fields
    /// that are already there, and a settings file carrying both says the same
    /// thing twice in a place somebody has to read by hand.
    /// </remarks>
    [JsonIgnore]
    public AssistantModel Suggestion => new(Id, Vision, Hearing);
}

/// <summary>
/// What to ask, for a survey that costs a request per question.
/// </summary>
/// <param name="Only">
/// The models to probe, or empty for whichever the provider thinks are
/// candidates. A model named here is probed whether or not the endpoint listed
/// it, because a listing and an endpoint disagree often enough to be worth
/// checking by hand.
/// </param>
/// <param name="All">Probe everything listed rather than the provider's own shortlist.</param>
/// <param name="Bounds">
/// Measure what each model will think for. Off by default because finding out
/// costs a request per step and makes the model think for real near the top of
/// its range — which is billed like any other thinking.
/// </param>
public sealed record SurveyOptions(
    IReadOnlyList<string>? Only = null,
    bool All = false,
    bool Bounds = false);

/// <summary>
/// A provider that can be asked what its endpoint actually accepts.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IPatchAssistant"/> rather than a member of it,
/// because whether a provider can answer this at all is a fact about the
/// provider: an endpoint that lists nothing, or that has no way to be asked what
/// it takes, cannot, and an interface it had to implement and refuse would be
/// worse than one it does not implement. A caller asks
/// <c>assistant is IModelSurvey</c> and says so plainly when the answer is no.
/// </para>
/// <para>
/// Every question is a real request against a real key, which is why this is the
/// one thing on this boundary that takes a <see cref="CancellationToken"/> and
/// reports as it goes: a survey of thirty models is thirty round trips and
/// somebody watching deserves to see which one it is on.
/// </para>
/// </remarks>
public interface IModelSurvey
{
    /// <summary>
    /// Asks the endpoint about each candidate and answers with what it accepted.
    /// </summary>
    /// <remarks>
    /// Only models that answered at all come back. A model the catalogue lists
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
/// <para>
/// One string under one key, because that is the only shape the settings file
/// has (ADR-0069): the shell keeps a bag of strings per provider and knows what
/// none of them mean. A list of models is not a string, so it is written as one
/// here and read back here, and the shell carries it the same way it carries an
/// endpoint or a tick.
/// </para>
/// <para>
/// The file is hand-editable and hand-edited, so <see cref="Read"/> never throws
/// and never half-succeeds: anything it cannot make sense of is nothing at all,
/// which falls back to whatever the provider had written down.
/// </para>
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
