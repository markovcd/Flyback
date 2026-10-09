using Flyback.Plugins.Assist;
using Flyback.Plugins.Surveys;

namespace Flyback.Plugins.Gemini;

/// <summary>
/// The half of this provider that finds out what it is talking to. Worth having
/// because <c>models.list</c> answers neither question that matters: it says nothing
/// about which inputs a model takes, and a model it lists can still answer
/// <c>generateContent</c> with a 404 saying it is closed to new keys.
/// </summary>
public sealed partial class GeminiAssistant : IModelSurvey
{
    public async Task<IReadOnlyList<ModelReport>> Survey(
        AssistantConfig config,
        SurveyOptions options,
        IProgress<string>? said = null,
        CancellationToken cancel = default)
    {
        var chosen = Schema.Read(config.Values);

        var probe = new GeminiProbe(config.Transport, chosen.BaseUrl ?? Schema.DefaultBaseUrl!);

        return await SurveyLoop.Run(probe, Schema.Asking(options, config.Values), said, cancel).ConfigureAwait(false);
    }
}
