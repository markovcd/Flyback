using System.Net;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Gemini.Tests;

/// <summary>
/// What a survey makes of an endpoint, driven by canned replies rather than by
/// one.
/// </summary>
/// <remarks>
/// The two facts under test are the two the catalogue cannot supply: whether a
/// model answers at all, and what it takes being handed. Both are only ever
/// known by asking, so every test here is about what the asking concluded from
/// a particular set of refusals.
/// </remarks>
public class SurveyTests
{
    [Fact]
    public async Task Reports_what_each_model_took()
    {
        using var endpoint = new Endpoint(["gemini-3.6-flash"]);

        var found = await Survey(endpoint);

        var model = found.ShouldHaveSingleItem();

        model.Id.ShouldBe("gemini-3.6-flash");
        model.Vision.ShouldBeTrue();
        model.Hearing.ShouldBeTrue();
    }

    [Fact]
    public async Task A_model_that_refuses_a_sound_is_reported_without_one()
    {
        using var endpoint = new Endpoint(["gemini-3.6-flash"]) { Deaf = true };

        var found = await Survey(endpoint);

        found.ShouldHaveSingleItem().Hearing.ShouldBeFalse();
        found[0].Vision.ShouldBeTrue();
    }

    /// <summary>
    /// The whole reason for asking: a model the catalogue lists and the endpoint
    /// refuses is not a model with no senses, it is not a model here.
    /// </summary>
    [Fact]
    public async Task A_model_the_endpoint_refuses_is_not_reported_at_all()
    {
        using var endpoint = new Endpoint(["gemini-2.5-flash", "gemini-3.6-flash"]) { Gone = ["gemini-2.5-flash"] };

        var found = await Survey(endpoint);

        found.ShouldHaveSingleItem().Id.ShouldBe("gemini-3.6-flash");
    }

    /// <summary>
    /// A limit is an answer about the moment rather than about the model, and
    /// recording it as a sense would outlive the moment. Cautious rather than
    /// generous: sending a sound to a model that refuses one loses every turn
    /// from the first listen onwards, and the other way costs a tick.
    /// </summary>
    [Fact]
    public async Task A_rate_limit_is_not_read_as_a_sense()
    {
        using var endpoint = new Endpoint(["gemini-3.6-flash"]) { Limited = true };

        var found = await Survey(endpoint);

        found.ShouldHaveSingleItem().Hearing.ShouldBeFalse();
    }

    [Fact]
    public async Task Models_that_could_not_build_a_patch_are_left_out()
    {
        using var endpoint = new Endpoint(["gemini-3.6-flash", "gemini-3.1-flash-image", "gemini-embedding-2"]);

        var found = await Survey(endpoint);

        found.Select(m => m.Id).ShouldBe(["gemini-3.6-flash"]);
    }

    [Fact]
    public async Task Everything_listed_is_asked_when_told_to()
    {
        using var endpoint = new Endpoint(["gemini-3.6-flash", "gemini-3.1-flash-image"]);

        var found = await Survey(endpoint, new SurveyOptions(All: true));

        found.Select(m => m.Id).ShouldBe(["gemini-3.6-flash", "gemini-3.1-flash-image"]);
    }

    /// <summary>
    /// A model named by hand is asked whether or not the catalogue mentions it,
    /// because a listing and an endpoint disagree often enough to be worth
    /// checking.
    /// </summary>
    [Fact]
    public async Task A_model_named_by_hand_is_asked_though_nothing_listed_it()
    {
        using var endpoint = new Endpoint([]);

        var found = await Survey(endpoint, new SurveyOptions(Only: ["gemini-9-flash"]));

        found.ShouldHaveSingleItem().Id.ShouldBe("gemini-9-flash");
    }

    [Fact]
    public async Task Says_what_it_is_asking_as_it_goes()
    {
        using var endpoint = new Endpoint(["gemini-3.6-flash"]);
        var said = new Transcript();

        await Survey(endpoint, said: said);

        said.Lines.ShouldContain(line => line.Contains("gemini-3.6-flash", StringComparison.Ordinal));
    }

    /// <summary>
    /// The far end of the loop: what a survey wrote is what the box offers. The
    /// window never learns a model name — it draws the field this hands back —
    /// so this is the only place the two are seen to agree.
    /// </summary>
    [Fact]
    public void The_model_box_offers_what_the_survey_wrote()
    {
        var found = Assist.Survey.Write([new ModelReport("gemini-9-flash") { Hearing = true }]);
        var values = AssistantValues.None.With(Assist.Survey.Key, found);

        var box = new GeminiAssistant().Form(values)
            .OfType<AssistantField.Pick>()
            .First(f => f.Key == AssistantSchema.ModelKey);

        box.Options.Select(o => o.Id).ShouldBe(["gemini-9-flash"]);
        box.Fallback.ShouldBe("gemini-9-flash");
    }

    private static async Task<IReadOnlyList<ModelReport>> Survey(
        Endpoint endpoint,
        SurveyOptions? options = null,
        IProgress<string>? said = null)
    {
        using var probe = new GeminiProbe("key", "https://example.test/v1beta", endpoint);

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
    /// because a survey asks a different number of questions per model and a
    /// queue of answers would have to be rewritten for every case.
    /// </summary>
    private sealed class Endpoint(string[] listed) : HttpMessageHandler
    {
        /// <summary>Refuses a sound, as a model that only reads does.</summary>
        public bool Deaf { get; init; }

        /// <summary>Answers the sound with a limit, which is not an answer about the model.</summary>
        public bool Limited { get; init; }

        /// <summary>Listed, and gone when actually asked.</summary>
        public string[] Gone { get; init; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancel)
        {
            var url = request.RequestUri!.ToString();

            if (request.Method == HttpMethod.Get) return Reply(HttpStatusCode.OK, Catalogue());

            var model = url.Split("/models/")[1].Split(':')[0];

            if (Gone.Contains(model)) return Reply(HttpStatusCode.NotFound, Refusal("no longer available"));

            var body = await request.Content!.ReadAsStringAsync(cancel).ConfigureAwait(false);

            if (body.Contains("audio/wav", StringComparison.Ordinal))
            {
                if (Limited) return Reply(HttpStatusCode.TooManyRequests, Refusal("slow down"));
                if (Deaf) return Reply(HttpStatusCode.BadRequest, Refusal("does not take audio"));
            }

            return Reply(HttpStatusCode.OK, "{\"candidates\":[]}");
        }

        private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body) };

        private static string Refusal(string said) =>
            new JsonObject { ["error"] = new JsonObject { ["message"] = said } }.ToJsonString();

        private string Catalogue()
        {
            var models = new JsonArray();

            foreach (var name in listed)
                models.Add(new JsonObject
                {
                    ["name"] = $"models/{name}",
                    ["supportedGenerationMethods"] = new JsonArray("generateContent", "countTokens"),
                });

            return new JsonObject { ["models"] = models }.ToJsonString();
        }
    }
}
