using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Surveys;

namespace Flyback.Plugins.OpenAi;

/// <summary>
/// What a survey needs to know about a chat-completions endpoint, whoever is running it.
/// </summary>
/// <remarks>
/// The catalog here says nothing beyond a list of ids, and the models this adapter
/// listens with refuse a ping without a sound, so the gate takes a second question
/// (<see cref="OnlyHears"/>). Nothing here measures a thinking budget, since this
/// adapter sends no effort at all.
/// </remarks>
/// <param name="transport">What to send over, which adds the key (<see cref="IAssistantTransport"/>).</param>
/// <param name="baseUrl">The endpoint, which may be anybody's.</param>
internal sealed class OpenAiProbe(IAssistantTransport transport, string baseUrl) : IModelProbe
{
    private const string Ping = "Reply with the single word: ok";

    /// <summary>
    /// Families that cannot hold a conversation whatever they answer.
    /// </summary>
    /// <remarks>
    /// Matched anywhere in the id rather than against a prefix, because there is
    /// no prefix to match: this adapter reaches a dozen services and every local
    /// runtime, and a model here may as easily be called <c>qwen2.5</c> as
    /// <c>gpt-4o</c>. Which also makes this list the only thing standing between
    /// somebody and a survey of every voice and embedding a large provider
    /// ships, so it errs towards excluding — <see cref="SurveyOptions.All"/>
    /// asks about everything for whoever thinks it is wrong.
    /// <para>
    /// <c>audio</c> is conspicuously absent. Those are the models this adapter
    /// listens with, and they are the reason for <see cref="OnlyHears"/>.
    /// </para>
    /// </remarks>
    private static readonly string[] Elsewhere =
    [
        "embedding", "whisper", "tts", "dall-e", "moderation", "image",
        "realtime", "transcribe", "search", "davinci", "babbage", "sora",
        "guard", "rerank",
    ];

    private readonly string address = baseUrl.TrimEnd('/');

    /// <summary>
    /// Every id the endpoint lists, or nothing where it does not list any.
    /// </summary>
    /// <remarks>
    /// A missing catalog is not a failure. Plenty of things that speak this
    /// format do not answer <c>/models</c>, and the honest response is to say so
    /// and let somebody name what they wanted — not to refuse to work against
    /// the server they actually have.
    /// </remarks>
    public async Task<IReadOnlyList<string>> Catalog(IProgress<string>? said, CancellationToken cancel)
    {
        var found = new List<string>();

        var response = await transport.Send(new Uri($"{address}/models"), null, cancel).ConfigureAwait(false);

        if (!response.Succeeded)
        {
            said?.Report(
                $"{address}/models answered {response.Status}, so there is no list to read. "
                + "Name the models to ask about instead.");

            return found;
        }

        foreach (var model in JsonNode.Parse(response.Body)?["data"]?.AsArray() ?? [])
            if (model?["id"]?.GetValue<string>() is { } id && !string.IsNullOrWhiteSpace(id))
                found.Add(id);

        said?.Report($"{found.Count} models listed.");

        return found;
    }

    /// <summary>
    /// One turn, built by <see cref="Wire.UserWithMedia"/> — the same call the
    /// adapter makes — so that an answer here is an answer about what this
    /// plugin sends rather than about what a probe sends.
    /// </summary>
    /// <remarks>
    /// No cap on what comes back. <c>max_tokens</c> is refused by some models on
    /// this format and <c>max_completion_tokens</c> by others, and a probe that
    /// argued about the parameter would be measuring itself.
    /// </remarks>
    private static JsonObject Turn(string model, byte[]? picture, byte[]? sound) =>
        new()
        {
            ["messages"] = new JsonArray(
                Wire.UserWithMedia(Ping, picture is null ? [] : [picture], sound is null ? [] : [sound])),
            ["model"] = model,
        };

    public Task<ProbeAnswer> Ask(string model, byte[]? picture, byte[]? sound, CancellationToken cancel) =>
        SurveyLoop.Ask(
            transport,
            new Uri($"{address}/chat/completions"),
            Turn(model, picture, sound).ToJsonString(),
            cancel);

    /// <summary>
    /// Whether a refusal is the one that means "this model only works with a
    /// sound", read off the sentence because the status code is the same 400
    /// every other refusal uses.
    /// </summary>
    public bool OnlyHears(ProbeAnswer refused) =>
        refused.Detail.Contains("audio", StringComparison.OrdinalIgnoreCase);

    public bool Candidate(string model) =>
        !Elsewhere.Any(m => model.Contains(m, StringComparison.OrdinalIgnoreCase));
}
