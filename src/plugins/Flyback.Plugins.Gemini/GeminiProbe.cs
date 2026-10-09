using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Surveys;

namespace Flyback.Plugins.Gemini;

/// <summary>
/// What a survey needs to know about the Gemini endpoint: a catalog that names the
/// models answering generateContent, and a thinking budget worth measuring.
/// </summary>
/// <remarks>
/// Separate from <see cref="GeminiSession"/> despite speaking the same format, because
/// none of what a session carries applies: no briefing, no tools, no history, no retry
/// budget spent on behalf of somebody waiting for a patch.
/// </remarks>
/// <param name="transport">What to send over, which adds the key (<see cref="IAssistantTransport"/>).</param>
/// <param name="address">The endpoint, without a trailing slash.</param>
internal sealed class GeminiProbe(IAssistantTransport transport, string address) : IModelProbe
{
    /// <summary>
    /// The largest budget worth searching for. Above any published ceiling, so
    /// a model whose real maximum is higher reports this and is not wrong about
    /// anything anybody can act on.
    /// </summary>
    private const int Ceiling = 262144;

    /// <summary>
    /// Families that cannot build a patch whatever they answer: embeddings and
    /// answer-only models cannot hold a conversation, and the rest are pointed
    /// at some other job entirely. <see cref="SurveyOptions.All"/> probes them
    /// anyway, for somebody who thinks this list is wrong.
    /// </summary>
    private static readonly string[] Elsewhere =
    [
        "embedding", "aqa", "veo", "imagen", "lyria", "gemma", "tts", "-image",
        "native-audio", "live", "transcribe", "robotics", "computer-use",
        "deep-research", "antigravity",
    ];

    public bool Thinks => true;

    /// <summary>
    /// Every id in the catalog that claims generateContent. Claiming it is not
    /// the same as answering it, which is why this is where a survey starts
    /// rather than where it stops.
    /// </summary>
    public async Task<IReadOnlyList<string>> Catalog(IProgress<string>? said, CancellationToken cancel)
    {
        var found = new List<string>();
        string? page = null;

        do
        {
            var url = $"{address}/models?pageSize=1000"
                + (page is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(page)}");

            var response = await transport.Send(new Uri(url), null, cancel).ConfigureAwait(false);

            if (!response.Succeeded)
                throw new HttpRequestException($"models.list: {response.Status} {Probe.Detail(response.Body)}");

            var parsed = JsonNode.Parse(response.Body)?.AsObject();

            foreach (var model in parsed?["models"]?.AsArray() ?? [])
            {
                var name = model?["name"]?.GetValue<string>();
                var methods = model?["supportedGenerationMethods"]?.AsArray();

                if (name is null || methods is null) continue;
                if (!methods.Any(m => m?.GetValue<string>() == "generateContent")) continue;

                found.Add(name.StartsWith("models/", StringComparison.Ordinal) ? name["models/".Length..] : name);
            }

            page = parsed?["nextPageToken"]?.GetValue<string>();
        }
        while (!string.IsNullOrEmpty(page));

        said?.Report($"{found.Count} models claim generateContent.");

        return found;
    }

    /// <summary>
    /// The smallest and largest budget a model will take, by halving.
    /// </summary>
    /// <remarks>
    /// Costs a request per step and makes the model think for real near the top of its
    /// range, which is billed like any other thinking — hence
    /// <see cref="SurveyOptions.Bounds"/> rather than always. The range is assumed
    /// contiguous, which is what sending one budget already assumes.
    /// </remarks>
    public async Task<(int? Least, int? Most)> Bounds(string model, IProgress<string>? said, CancellationToken cancel)
    {
        said?.Report($"{model}: measuring what it will think for, which is billed");

        async Task<bool> Takes(int budget) =>
            await Send(model, Turn(thinking: new JsonObject { ["thinkingBudget"] = budget }), cancel)
                .ConfigureAwait(false) is { Verdict: ProbeVerdict.Took };

        // Dynamic is what the model would have done unasked, so a refusal here
        // is the field being unknown rather than a budget being out of range —
        // and there is nothing to bisect for a field nobody reads.
        if (!await Takes(-1).ConfigureAwait(false)) return (null, null);

        var least = await Takes(0).ConfigureAwait(false) ? 0 : await Edge(0, Ceiling, Takes, smallest: true).ConfigureAwait(false);

        if (least is null) return (null, null);

        var most = await Edge(least.Value, Ceiling, Takes, smallest: false).ConfigureAwait(false);

        return most is null ? (null, null) : (least, most);
    }

    private static async Task<int?> Edge(int low, int high, Func<int, Task<bool>> takes, bool smallest)
    {
        int lo = low, hi = high;
        int? found = null;

        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);

            if (await takes(mid).ConfigureAwait(false))
            {
                found = mid;

                if (smallest) hi = mid - 1;
                else lo = mid + 1;
            }
            else if (smallest)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }

    /// <summary>
    /// One turn, in the shapes <see cref="Wire"/> sends — <c>inlineData</c>,
    /// <c>mimeType</c>, <c>generationConfig.thinkingConfig</c> — so that an
    /// answer here is an answer about what this plugin does rather than about
    /// what a probe does.
    /// </summary>
    private static JsonObject Turn(IEnumerable<JsonObject>? media = null, JsonObject? thinking = null)
    {
        var parts = new JsonArray(new JsonObject { ["text"] = "Reply with the single word: ok" });

        foreach (var part in media ?? []) parts.Add(part);

        var request = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = parts }),
        };

        if (thinking is not null)
            request["generationConfig"] = new JsonObject { ["thinkingConfig"] = thinking };

        return request;
    }

    private static JsonObject Inline(string type, byte[] bytes) => new()
    {
        ["inlineData"] = new JsonObject
        {
            ["mimeType"] = type,
            ["data"] = Convert.ToBase64String(bytes),
        },
    };

    public Task<ProbeAnswer> Ask(string model, byte[]? picture, byte[]? sound, CancellationToken cancel)
    {
        var media = new List<JsonObject>();

        if (picture is not null) media.Add(Inline("image/png", picture));
        if (sound is not null) media.Add(Inline("audio/wav", sound));

        return Send(model, Turn(media), cancel);
    }

    private Task<ProbeAnswer> Send(string model, JsonObject body, CancellationToken cancel) =>
        SurveyLoop.Ask(
            transport,
            new Uri($"{address}/models/{Uri.EscapeDataString(model)}:generateContent"),
            body.ToJsonString(),
            cancel);

    public bool Candidate(string model) =>
        model.StartsWith("gemini-", StringComparison.Ordinal)
        && !Elsewhere.Any(m => model.Contains(m, StringComparison.Ordinal));
}
