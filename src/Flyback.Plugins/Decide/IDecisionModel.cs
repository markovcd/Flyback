using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Decide;

/// <summary>
/// Something that answers typed questions about a piece of text with a probability
/// rather than with prose: one of a list, a level on a scale, or yes or no.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="IPatchAssistant"/> because it writes nothing and is
/// asked from inside other features, where an answer is a number to compare with a
/// threshold. Registering one costs nothing: a model is loaded, or an endpoint
/// reached, on the first <see cref="DecideAsync"/>.
/// </remarks>
public interface IDecisionModel
{
    /// <summary>Stable identifier, e.g. <c>laya</c>. What a setting names.</summary>
    string Id { get; }

    /// <summary>What a person should see.</summary>
    string Name { get; }

    /// <summary>Higher wins when several are installed. Ties break on <see cref="Id"/>.</summary>
    int Priority { get; }

    /// <summary>Where this one's key comes from, or null for one that needs none. The host holds it (ADR-0034).</summary>
    AssistantCredential? Credential { get; }

    /// <summary>Where a model configured this way sends its requests, or null for one that runs here.</summary>
    Uri? Endpoint(SettingValues values) => null;

    /// <summary>Every setting this model has, as the form should stand with <paramref name="values"/> on it.</summary>
    IReadOnlyList<SettingField> Form(SettingValues values);

    /// <summary>Why this configuration cannot answer, or null when it can.</summary>
    /// <remarks>Answers without a network call, without loading a model and without throwing; a throw is taken as a no.</remarks>
    string? Unavailable(DecisionConfig config);

    /// <summary>Answers every question in <paramref name="request"/>.</summary>
    /// <exception cref="HttpRequestException">An endpoint refused, said as a sentence.</exception>
    /// <exception cref="InvalidOperationException">The model could not be run, said as a sentence.</exception>
    Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel);
}
