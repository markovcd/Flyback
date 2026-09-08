using System.Net;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.OpenAi.Tests;

/// <summary>
/// What a survey makes of an endpoint that could be anybody's.
/// </summary>
/// <remarks>
/// Most of this is the same shape as the Gemini adapter's, and the interesting
/// part is where it is not. The catalogue here carries no capabilities, so the
/// filter is doing real work; and the models this adapter listens with refuse a
/// turn that has no sound in it, so the question "is this a model here" cannot
/// be a single question.
/// </remarks>
public class SurveyTests
{
    [Fact]
    public async Task Reports_what_each_model_took()
    {
        using var endpoint = new Endpoint(["gpt-4o"]);

        var found = await Survey(endpoint);

        var model = found.ShouldHaveSingleItem();

        model.Id.ShouldBe("gpt-4o");
        model.Vision.ShouldBeTrue();
        model.Hearing.ShouldBeFalse();
    }

    /// <summary>
    /// The one this adapter cannot get wrong. An audio model refuses a text-only
    /// turn (ADR-0047), so a survey that treated a plain ping as the gate would
    /// drop exactly the models the ear exists for.
    /// </summary>
    [Fact]
    public async Task A_model_that_will_only_take_a_sound_is_still_a_model()
    {
        using var endpoint = new Endpoint(["gpt-audio"]) { Deafening = ["gpt-audio"] };

        var found = await Survey(endpoint);

        var model = found.ShouldHaveSingleItem();

        model.Id.ShouldBe("gpt-audio");
        model.Hearing.ShouldBeTrue();
        model.Vision.ShouldBeFalse();
    }

    [Fact]
    public async Task A_model_refused_for_any_other_reason_is_not_reported()
    {
        using var endpoint = new Endpoint(["gpt-4o", "retired-model"]) { Gone = ["retired-model"] };

        var found = await Survey(endpoint);

        found.ShouldHaveSingleItem().Id.ShouldBe("gpt-4o");
    }

    [Fact]
    public async Task A_rate_limit_is_not_read_as_a_sense()
    {
        using var endpoint = new Endpoint(["gpt-4o"]) { Limited = true };

        var found = await Survey(endpoint);

        found.ShouldHaveSingleItem().Vision.ShouldBeFalse();
    }

    /// <summary>
    /// The catalogue is a list of ids and nothing else here, so the filter is
    /// the only thing keeping a survey of a large provider down to the models
    /// that could build a patch.
    /// </summary>
    [Fact]
    public async Task Models_that_could_not_hold_a_conversation_are_left_out()
    {
        using var endpoint = new Endpoint(["gpt-4o", "text-embedding-3-small", "whisper-1", "dall-e-3", "gpt-4o-mini-tts"]);

        var found = await Survey(endpoint);

        found.Select(m => m.Id).ShouldBe(["gpt-4o"]);
    }

    /// <summary>
    /// A local runtime names its models nothing like a large provider does, so
    /// the filter cannot key off a prefix the way the Gemini one does.
    /// </summary>
    [Fact]
    public async Task A_model_named_the_way_a_local_runtime_names_it_is_a_candidate()
    {
        using var endpoint = new Endpoint(["qwen2.5", "llama3.1"]);

        var found = await Survey(endpoint);

        found.Select(m => m.Id).ShouldBe(["qwen2.5", "llama3.1"]);
    }

    [Fact]
    public async Task Everything_listed_is_asked_when_told_to()
    {
        using var endpoint = new Endpoint(["gpt-4o", "whisper-1"]);

        var found = await Survey(endpoint, new SurveyOptions(All: true));

        found.Select(m => m.Id).ShouldBe(["gpt-4o", "whisper-1"]);
    }

    /// <summary>
    /// Plenty of things that speak this format do not answer <c>/models</c>, and
    /// the honest response is to say so rather than to refuse to work against
    /// the server somebody actually has.
    /// </summary>
    [Fact]
    public async Task An_endpoint_with_no_catalogue_says_so_rather_than_failing()
    {
        using var endpoint = new Endpoint([]) { Listless = true };
        var said = new Transcript();

        var found = await Survey(endpoint, said: said);

        found.ShouldBeEmpty();
        said.Lines.ShouldContain(line => line.Contains("no list to read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Naming_the_models_asks_the_catalogue_nothing()
    {
        using var endpoint = new Endpoint([]) { Listless = true };

        var found = await Survey(endpoint, new SurveyOptions(Only: ["qwen2.5"]));

        endpoint.Listed.ShouldBeFalse();
        found.ShouldHaveSingleItem().Id.ShouldBe("qwen2.5");
    }

    [Fact]
    public async Task There_is_no_thinking_budget_here_to_measure()
    {
        using var endpoint = new Endpoint(["gpt-4o"]);
        var said = new Transcript();

        var found = await Survey(endpoint, new SurveyOptions(Bounds: true), said);

        found.ShouldHaveSingleItem().Least.ShouldBeNull();
        said.Lines.ShouldContain(line => line.Contains("thinking budget", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<IReadOnlyList<ModelReport>> Survey(
        Endpoint endpoint,
        SurveyOptions? options = null,
        IProgress<string>? said = null)
    {
        using var probe = new OpenAiProbe("key", "https://example.test/v1", endpoint);

        return await probe.Run(options ?? new SurveyOptions(), said, CancellationToken.None);
    }

    /// <summary>
    /// Kept rather than <see cref="Progress{T}"/>, which posts through the
    /// synchronisation context and so arrives after the assertion.
    /// </summary>
    private sealed class Transcript : IProgress<string>
    {
        public List<string> Lines { get; } = [];

        public void Report(string value) => Lines.Add(value);
    }

    /// <summary>
    /// An endpoint that lists what it was told to and refuses whatever the test
    /// asked it to refuse. Routed on the request rather than canned in order,
    /// because the gate asks a different number of questions per model.
    /// </summary>
    private sealed class Endpoint(string[] listed) : HttpMessageHandler
    {
        /// <summary>Whether anybody asked for the catalogue.</summary>
        public bool Listed { get; private set; }

        /// <summary>Has no <c>/models</c> at all, as a good many of these do not.</summary>
        public bool Listless { get; init; }

        /// <summary>Refuses any turn that carries no sound, as the audio models do.</summary>
        public string[] Deafening { get; init; } = [];

        /// <summary>Listed, and gone when actually asked.</summary>
        public string[] Gone { get; init; } = [];

        /// <summary>Answers the picture with a limit, which is not an answer about the model.</summary>
        public bool Limited { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancel)
        {
            if (request.Method == HttpMethod.Get)
            {
                Listed = true;

                return Listless
                    ? Reply(HttpStatusCode.NotFound, Refusal("no such endpoint"))
                    : Reply(HttpStatusCode.OK, Catalogue());
            }

            var body = await request.Content!.ReadAsStringAsync(cancel).ConfigureAwait(false);
            var asked = JsonNode.Parse(body)!;
            var model = asked["model"]!.GetValue<string>();

            if (Gone.Contains(model)) return Reply(HttpStatusCode.NotFound, Refusal("no such model"));

            var sound = body.Contains("input_audio", StringComparison.Ordinal);
            var picture = body.Contains("image_url", StringComparison.Ordinal);

            if (Deafening.Contains(model) && !sound)
                return Reply(
                    HttpStatusCode.BadRequest,
                    Refusal("This model requires that either input content or output modality contain audio."));

            if (sound && !Deafening.Contains(model))
                return Reply(HttpStatusCode.BadRequest, Refusal("Invalid content type 'input_audio'."));

            if (picture && Limited) return Reply(HttpStatusCode.TooManyRequests, Refusal("slow down"));

            return Reply(HttpStatusCode.OK, "{\"choices\":[]}");
        }

        private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body) };

        private static string Refusal(string said) =>
            new JsonObject { ["error"] = new JsonObject { ["message"] = said } }.ToJsonString();

        private string Catalogue()
        {
            var models = new JsonArray();

            foreach (var id in listed) models.Add(new JsonObject { ["id"] = id });

            return new JsonObject { ["data"] = models }.ToJsonString();
        }
    }
}
