using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.OpenAi;

/// <summary>
/// The half of this adapter that finds out what it has been pointed at.
/// </summary>
/// <remarks>
/// Worth more here than anywhere else, because this adapter does not know what
/// it is talking to: the endpoint is a field, and the list of models beside it
/// is a guess about a service nobody named. A survey turns that guess into what
/// the endpoint said.
/// </remarks>
public sealed partial class OpenAiAssistant : IModelSurvey
{
    public async Task<IReadOnlyList<ModelReport>> Survey(
        AssistantConfig config,
        SurveyOptions options,
        IProgress<string>? said = null,
        CancellationToken cancel = default)
    {
        var chosen = Schema.Read(config.Values);

        using var probe = new OpenAiProbe(config.ApiKey, chosen.BaseUrl ?? Schema.DefaultBaseUrl!);

        return await probe.Run(options, said, cancel).ConfigureAwait(false);
    }
}

/// <summary>
/// One survey of one chat-completions endpoint, whoever is running it.
/// </summary>
/// <remarks>
/// <para>
/// Shaped like the one in the Gemini adapter and deliberately not shared with
/// it, because the two differ in the part that matters. There the catalogue says
/// which models can hold a conversation; here it says nothing at all beyond a
/// list of ids, and the gate that decides whether a model is present has to be
/// conditional — see <see cref="Run"/>.
/// </para>
/// <para>
/// Nothing here measures a thinking budget. This adapter sends no effort at all
/// because it cannot know what endpoint it is pointed at, and the spelling this
/// format uses is a word rather than a number, so there is no range to find.
/// </para>
/// </remarks>
/// <param name="apiKey">The key. A local runtime will take any value.</param>
/// <param name="baseUrl">The endpoint, which may be anybody's.</param>
/// <param name="transport">Supplied by the tests; a real run makes its own.</param>
internal sealed class OpenAiProbe(string apiKey, string baseUrl, HttpMessageHandler? transport = null) : IDisposable
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
    /// listens with, and they are the whole reason the gate below is not a
    /// single question.
    /// </para>
    /// </remarks>
    private static readonly string[] Elsewhere =
    [
        "embedding", "whisper", "tts", "dall-e", "moderation", "image",
        "realtime", "transcribe", "search", "davinci", "babbage", "sora",
        "guard", "rerank",
    ];

    private readonly string address = baseUrl.TrimEnd('/');
    private readonly HttpClient http = Client(apiKey, transport);

    public void Dispose() => http.Dispose();

    public async Task<IReadOnlyList<ModelReport>> Run(
        SurveyOptions options,
        IProgress<string>? said,
        CancellationToken cancel)
    {
        // Only asked for when it is needed. A run naming its models wants
        // nothing from the catalogue, and a local runtime that has no /models is
        // then a server this works against rather than one it refuses.
        var chosen = options.Only is { Count: > 0 } named
            ? named
            : (await Catalogue(said, cancel).ConfigureAwait(false))
                .Where(m => options.All || Candidate(m))
                .ToList();

        if (options.Bounds)
            said?.Report("Nothing here has a thinking budget to measure; asking the other questions only.");

        var found = new List<ModelReport>();

        foreach (var model in chosen)
        {
            cancel.ThrowIfCancellationRequested();

            if (await Look(model, cancel).ConfigureAwait(false) is not { } report)
            {
                said?.Report($"{model}: no");
                continue;
            }

            found.Add(report);
            said?.Report($"{model}: {Says(report)}");
        }

        return found;
    }

    /// <summary>
    /// What one model turned out to be, or null where it is not a model here.
    /// </summary>
    /// <remarks>
    /// The gate is two questions rather than one, and that is forced. A plain
    /// text turn is how you find out whether a model exists — except for the
    /// ones this adapter listens with, which require <em>every</em> request to
    /// carry a sound and answer a text-only one by saying so (ADR-0047). Gating
    /// on text alone would drop exactly the models the ear is for, so a refusal
    /// that names audio is a second question rather than an answer.
    /// </remarks>
    private async Task<ModelReport?> Look(string model, CancellationToken cancel)
    {
        var text = await Ask(model, Turn(), cancel).ConfigureAwait(false);

        if (text.Verdict is Verdict.Took)
        {
            var sees = await Ask(model, Turn(pictures: [Dot()]), cancel).ConfigureAwait(false);
            var hears = await Ask(model, Turn(sounds: [Tone()]), cancel).ConfigureAwait(false);

            // An indeterminate answer keeps the cautious value. A limit read as
            // "takes a sound" would send one to a model that refuses it and lose
            // every turn from the first listen onwards; read the other way it
            // costs a setting somebody can turn back on.
            return new ModelReport(model)
            {
                Vision = sees.Verdict is Verdict.Took,
                Hearing = hears.Verdict is Verdict.Took,
            };
        }

        if (text.Verdict is not Verdict.Refused || !MeansAudio(text.Detail)) return null;

        var only = await Ask(model, Turn(sounds: [Tone()]), cancel).ConfigureAwait(false);

        // Sight is not asked about: a model that refuses a turn without a sound
        // is one of the audio models, and none of them takes a picture.
        return only.Verdict is Verdict.Took
            ? new ModelReport(model) { Vision = false, Hearing = true }
            : null;
    }

    /// <summary>
    /// Every id the endpoint lists, or nothing where it does not list any.
    /// </summary>
    /// <remarks>
    /// A missing catalogue is not a failure. Plenty of things that speak this
    /// format do not answer <c>/models</c>, and the honest response is to say so
    /// and let somebody name what they wanted — not to refuse to work against
    /// the server they actually have.
    /// </remarks>
    private async Task<List<string>> Catalogue(IProgress<string>? said, CancellationToken cancel)
    {
        var found = new List<string>();

        using var response = await http.GetAsync(new Uri($"{address}/models"), cancel).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            said?.Report(
                $"{address}/models answered {(int)response.StatusCode}, so there is no list to read. "
                + "Name the models to ask about instead.");

            return found;
        }

        foreach (var model in JsonNode.Parse(body)?["data"]?.AsArray() ?? [])
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
    private static JsonObject Turn(IEnumerable<byte[]>? pictures = null, IEnumerable<byte[]>? sounds = null) =>
        new()
        {
            ["messages"] = new JsonArray(Wire.UserWithMedia(Ping, pictures ?? [], sounds ?? [])),
        };

    private async Task<Answer> Ask(string model, JsonObject body, CancellationToken cancel)
    {
        body["model"] = model;

        try
        {
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            using var response = await http
                .PostAsync(new Uri($"{address}/chat/completions"), content, cancel)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return new Answer(Verdict.Took, string.Empty);

            var said = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            // A refusal of the request is an answer about the model; anything
            // else — a limit, an outage, a proxy — is an answer about the
            // moment, and recording it as a capability would outlive the moment.
            return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
                ? new Answer(Verdict.Refused, Detail(said))
                : new Answer(Verdict.Unclear, Detail(said));
        }
        catch (HttpRequestException)
        {
            return new Answer(Verdict.Unclear, string.Empty);
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new Answer(Verdict.Unclear, string.Empty);
        }
    }

    /// <summary>
    /// Whether a refusal is the one that means "this model only works with a
    /// sound", read off the sentence because the status code is the same 400
    /// every other refusal uses.
    /// </summary>
    private static bool MeansAudio(string said) =>
        said.Contains("audio", StringComparison.OrdinalIgnoreCase);

    private static bool Candidate(string model) =>
        !Elsewhere.Any(m => model.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static HttpClient Client(string key, HttpMessageHandler? transport)
    {
        var client = transport is null ? new HttpClient() : new HttpClient(transport, disposeHandler: false);

        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return client;
    }

    private static string Says(ModelReport report) => (report.Vision, report.Hearing) switch
    {
        (true, true) => "sees, hears",
        (true, false) => "sees",
        (false, true) => "hears, and takes no picture",
        (false, false) => "text only",
    };

    private static string Detail(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() ?? body;
        }
        catch (System.Text.Json.JsonException)
        {
            return body;
        }
    }

    /// <summary>A 1×1 PNG. The smallest thing that is legally a picture.</summary>
    private static byte[] Dot() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>
    /// A tenth of a second of 440Hz rather than silence, so that a model which
    /// listens to what it was handed has something to find there.
    /// </summary>
    private static byte[] Tone()
    {
        const int Rate = 8000;
        const int Samples = Rate / 10;

        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + (Samples * 2));
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(Samples * 2);

        for (var i = 0; i < Samples; i++)
            writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / Rate) * 8000));

        writer.Flush();

        return buffer.ToArray();
    }

    /// <param name="Detail">What the endpoint said, which is load-bearing for one refusal.</param>
    private sealed record Answer(Verdict Verdict, string Detail);

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
