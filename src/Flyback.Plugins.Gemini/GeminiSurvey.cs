using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

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

        using var probe = new GeminiProbe(config.ApiKey, chosen.BaseUrl ?? Schema.DefaultBaseUrl!);

        return await probe.Run(options, said, cancel).ConfigureAwait(false);
    }
}

/// <summary>
/// One survey of one endpoint, from the catalogue down to what each model took.
/// </summary>
/// <remarks>
/// Separate from <see cref="GeminiSession"/> despite speaking the same format, because
/// none of what a session carries applies: no briefing, no tools, no history, no retry
/// budget spent on behalf of somebody waiting for a patch.
/// </remarks>
/// <param name="apiKey">The key. Sent as a header rather than in the query, which keeps it out of logs.</param>
/// <param name="address">The endpoint, without a trailing slash.</param>
/// <param name="transport">Supplied by the tests; a real run makes its own.</param>
internal sealed class GeminiProbe(string apiKey, string address, HttpMessageHandler? transport = null) : IDisposable
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

    private readonly HttpClient http = Client(apiKey, transport);

    public void Dispose() => http.Dispose();

    public async Task<IReadOnlyList<ModelReport>> Run(
        SurveyOptions options,
        IProgress<string>? said,
        CancellationToken cancel)
    {
        var catalogue = await Catalogue(cancel).ConfigureAwait(false);

        var chosen = options.Only is { Count: > 0 } named
            ? named
            : catalogue.Where(m => options.All || Candidate(m)).ToList();

        said?.Report($"{catalogue.Count} models claim generateContent; asking {chosen.Count}.");

        var found = new List<ModelReport>();

        foreach (var model in chosen)
        {
            cancel.ThrowIfCancellationRequested();

            // Nothing else is worth asking once the model itself is refused, and
            // a refused model is not a model with no senses — it is not a model
            // here, which is the difference the catalogue could not tell us.
            if (await Ask(model, Turn(), cancel).ConfigureAwait(false) is not Verdict.Took)
            {
                said?.Report($"{model}: no");
                continue;
            }

            var sees = await Ask(model, Turn(Inline("image/png", Probe.Picture())), cancel).ConfigureAwait(false);
            var hears = await Ask(model, Turn(Inline("audio/wav", Probe.Sound())), cancel).ConfigureAwait(false);

            var report = new ModelReport(model)
            {
                // An indeterminate answer keeps the cautious value rather than
                // the generous one. A rate limit read as "takes a sound" would
                // send one to a model that refuses it and lose every turn from
                // the first listen onwards; read the other way it costs a
                // setting somebody can turn back on.
                Vision = sees is Verdict.Took,
                Hearing = hears is Verdict.Took,
            };

            if (options.Bounds && await Bounds(model, said, cancel).ConfigureAwait(false) is var (least, most))
                report = report with { Least = least, Most = most };

            found.Add(report);
            said?.Report($"{model}: {Says(report, sees, hears)}");
        }

        return found;
    }

    /// <summary>
    /// Every id in the catalogue that claims generateContent. Claiming it is not
    /// the same as answering it, which is why this is where a survey starts
    /// rather than where it stops.
    /// </summary>
    private async Task<List<string>> Catalogue(CancellationToken cancel)
    {
        var found = new List<string>();
        string? page = null;

        do
        {
            var url = $"{address}/models?pageSize=1000"
                + (page is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(page)}");

            using var response = await http.GetAsync(new Uri(url), cancel).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"models.list: {(int)response.StatusCode} {Probe.Detail(body)}");

            var parsed = JsonNode.Parse(body)?.AsObject();

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
    private async Task<(int? Least, int? Most)> Bounds(string model, IProgress<string>? said, CancellationToken cancel)
    {
        said?.Report($"{model}: measuring what it will think for, which is billed");

        async Task<bool> Takes(int budget) =>
            await Ask(model, Turn(thinking: new JsonObject { ["thinkingBudget"] = budget }), cancel)
                .ConfigureAwait(false) is Verdict.Took;

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
    private static JsonObject Turn(JsonObject? attachment = null, JsonObject? thinking = null)
    {
        JsonNode text = new JsonObject { ["text"] = "Reply with the single word: ok" };

        var parts = attachment is null ? new JsonArray(text) : new JsonArray(text, attachment);

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

    private async Task<Verdict> Ask(string model, JsonObject body, CancellationToken cancel)
    {
        var endpoint = new Uri($"{address}/models/{Uri.EscapeDataString(model)}:generateContent");

        try
        {
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(endpoint, content, cancel).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return Verdict.Took;

            // A refusal of the request is an answer about the model; anything
            // else — a limit, an outage, a proxy — is an answer about the
            // moment, and recording it as a capability would outlive the moment.
            return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
                ? Verdict.Refused
                : Verdict.Unclear;
        }
        catch (HttpRequestException)
        {
            return Verdict.Unclear;
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return Verdict.Unclear;
        }
    }

    private static bool Candidate(string model) =>
        model.StartsWith("gemini-", StringComparison.Ordinal)
        && !Elsewhere.Any(m => model.Contains(m, StringComparison.Ordinal));

    private static HttpClient Client(string key, HttpMessageHandler? transport)
    {
        var client = Probe.Client(transport);

        client.DefaultRequestHeaders.Add("x-goog-api-key", key);

        return client;
    }

    private static string Says(ModelReport report, Verdict sees, Verdict hears)
    {
        var senses = new List<string>();

        if (report.Vision) senses.Add("sees");
        if (report.Hearing) senses.Add("hears");
        if (sees is Verdict.Unclear || hears is Verdict.Unclear) senses.Add("asked at a bad moment");
        if (report.Least is { } least) senses.Add($"thinks {least}..{report.Most}");

        return senses.Count == 0 ? "answers, and takes nothing else" : string.Join(", ", senses);
    }



    /// <summary>
    /// What one question came back as. Three rather than two because a limit and
    /// a refusal look the same to a caller that only asks whether it worked.
    /// </summary>
    private enum Verdict
    {
        Took,
        Refused,
        Unclear,
    }
}
