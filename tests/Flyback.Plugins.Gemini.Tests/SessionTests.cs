using System.Net;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Gemini.Tests;

/// <summary>
/// How a turn ends, and where a clip goes, driven by canned replies rather than
/// by an endpoint.
/// </summary>
/// <remarks>
/// The endings are the same three the other adapter has and are checked the same
/// way. What is new here is the ear: this is the first adapter where the model
/// building the patch can be played it, so most of what follows is about the
/// sound arriving in the conversation rather than in a second request.
/// </remarks>
public class SessionTests
{
    /// <summary>
    /// A grey field — the smallest patch that compiles cleanly, and so the
    /// smallest one that may be proposed.
    /// </summary>
    private static readonly (string Name, string Arguments)[] Building =
    [
        ("add_module", """{"type_id":"value","handle":"knob1","knobs":[{"port":"value","value":0.5}]}"""),
        // The Output is already there under the handle the workbench gave it.
        ("connect", """{"from":"knob1","to":"output1","to_port":"color"}"""),
    ];

    /// <summary>
    /// The smallest patch that actually sounds. The clock is not decoration: an
    /// oscillator accumulates how far its 'in' has moved, so one without it is
    /// silent whatever its freq says — and silence comes back as a sentence
    /// rather than as a WAV.
    /// </summary>
    private static readonly (string Name, string Arguments)[] Sounding =
    [
        ("add_module", """{"type_id":"time","handle":"clock1"}"""),
        ("add_module", """{"type_id":"osc.sine","handle":"tone1","knobs":[{"port":"freq","value":440}]}"""),
        ("connect", """{"from":"clock1","to":"tone1","to_port":"in"}"""),
        ("connect", """{"from":"tone1","to":"output1","to_port":"left"}"""),
    ];

    /// <summary>
    /// Wired to the speakers and silent anyway. Legal, compiling, and nothing at
    /// all — the failure the compiler cannot see and the ear exists for.
    /// </summary>
    /// <remarks>
    /// A knob at zero rather than at any other value, and the difference is not
    /// pedantry: a constant is DC, the blocker removes it, and removing it
    /// leaves a decaying thump that measures around -11 dBFS. Silent on purpose
    /// takes a signal that is actually zero.
    /// </remarks>
    private static readonly (string Name, string Arguments)[] Mute =
    [
        ("add_module", """{"type_id":"value","handle":"knob1","knobs":[{"port":"value","value":0}]}"""),
        ("connect", """{"from":"knob1","to":"output1","to_port":"left"}"""),
    ];

    // --- the endings --------------------------------------------------------

    /// <summary>
    /// Nothing reaches the editor until <c>propose</c>, so a model that builds a
    /// patch and then signs off leaves the person looking at the patch they
    /// already had.
    /// </summary>
    [Fact]
    public async Task Building_and_stopping_without_offering_it_is_pointed_out()
    {
        var (events, sent) = await Run(
            Asking(Building),
            Prose("You're now set to further refine the patch as needed."));

        events.OfType<PatchEvent.Proposed>().ShouldBeEmpty();
        events.OfType<PatchEvent.Did>().ShouldContain(d => d.Summary.Contains("canvas still shows"));

        // Told to the person, not sent back to the model. Nothing extra is asked
        // of a model that has already given its answer.
        sent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_question_with_nothing_built_is_left_alone()
    {
        var (events, sent) = await Run(Prose("Which key should it be in?"));

        sent.Count.ShouldBe(1);
        events.OfType<PatchEvent.Did>().ShouldBeEmpty();
        events.OfType<PatchEvent.Said>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_patch_it_proposes_is_offered_to_the_person()
    {
        var (events, _) = await Run(
            Asking(Building),
            Asking(("propose", """{"summary":"a flat grey field"}""")));

        var proposed = events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();

        proposed.Summary.ShouldBe("a flat grey field");
        proposed.Patch.Nodes.Count.ShouldBe(2);
        events.OfType<PatchEvent.Failed>().ShouldBeEmpty();
    }

    /// <summary>
    /// Every call is answered, in the order it arrived. An answer out of order
    /// is answering the wrong call, because a functionResponse carries a name
    /// and nothing else to match on.
    /// </summary>
    [Fact]
    public async Task Every_call_is_answered_in_the_order_it_was_made()
    {
        var (_, sent) = await Run(
            Asking(Building),
            Asking(("propose", """{"summary":"a flat grey field"}""")));

        var answers = sent[1]["contents"]!.AsArray()
            .Last(content => content!["role"]!.GetValue<string>() == "user")!["parts"]!
            .AsArray()
            .Select(part => part!["functionResponse"]?["name"]?.GetValue<string>())
            .ToArray();

        answers.ShouldBe(["add_module", "connect"]);
    }

    /// <summary>
    /// A second message carries everything the first one said and built, and the
    /// briefing is not among it — it rides beside the conversation, so growing
    /// history never pushes it out of place.
    /// </summary>
    [Fact]
    public async Task A_second_message_keeps_the_first_ones_history()
    {
        var canned = new Canned(
            new Answer(Asking(Building)),
            new Answer(Prose("Done. Grey enough?")),
            new Answer(Asking(("propose", """{"summary":"a flat grey field"}"""))));

        using var session = Session(canned);

        await Drain(session, "make a grey field");
        await Drain(session, "yes, propose it");

        var last = canned.Sent[^1];
        var turns = last["contents"]!.AsArray();

        turns.Count.ShouldBeGreaterThan(4);
        turns[0]!["parts"]![0]!["text"]!.GetValue<string>().ShouldBe("make a grey field");
        last["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().ShouldNotBeNullOrEmpty();
    }

    /// <summary>The model is named in the path here, not in the body.</summary>
    [Fact]
    public async Task The_model_is_asked_for_by_url()
    {
        var canned = new Canned(new Answer(Prose("Hello.")));

        using var session = Session(canned);

        await Drain(session, "hello");

        canned.Urls[0].ShouldBe(
            "https://nowhere.invalid/v1beta/models/gemini-test:generateContent");
        canned.Sent[0]["model"].ShouldBeNull();
    }

    // --- the ear ------------------------------------------------------------

    /// <summary>
    /// The whole point of this adapter. A clip goes into the conversation as an
    /// ordinary part, addressed to the model that is building the patch — no
    /// second model, no second request, no description written by somebody who
    /// was not there.
    /// </summary>
    [Fact]
    public async Task A_clip_goes_to_the_model_that_is_building_the_patch()
    {
        var (events, sent) = await Listening(
            Asking(Sounding),
            Asking(("listen", """{"seconds":0.5}""")),
            Asking(("propose", """{"summary":"a sine at 440"}""")));

        events.OfType<PatchEvent.Heard>().ShouldHaveSingleItem();
        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();

        var parts = sent[^1]["contents"]!.AsArray()
            .Last(content => content!["role"]!.GetValue<string>() == "user")!["parts"]!
            .AsArray();

        Named(parts, "functionResponse", "name").ShouldContain("listen");
        Named(parts, "inlineData", "mimeType").ShouldContain("audio/wav");
        parts.Select(part => part?["text"]?.GetValue<string>())
            .ShouldContain("Here is what that sounded like.");
    }

    /// <summary>
    /// And nothing is asked of anybody else. A second request per listen is what
    /// the other adapter pays and this one does not.
    /// </summary>
    [Fact]
    public async Task Nobody_else_is_asked_to_listen()
    {
        var (_, sent) = await Listening(
            Asking(Sounding),
            Asking(("listen", """{"seconds":0.5}""")),
            Asking(("propose", """{"summary":"a sine at 440"}""")));

        // Four calls, one listen, one propose: four exchanges and not five.
        sent.Count.ShouldBe(3);

        foreach (var request in sent)
        {
            request.ToJsonString().ShouldNotContain("listening on behalf of");
        }
    }

    /// <summary>
    /// What the samples say goes back whether or not anybody heard it, and it is
    /// the only thing in the loop that can contradict the model's own ears.
    /// </summary>
    [Fact]
    public async Task What_the_samples_say_goes_back_with_the_clip()
    {
        var (events, _) = await Listening(
            Asking(Sounding),
            Asking(("listen", """{"seconds":0.5}""")),
            Asking(("propose", """{"summary":"a sine at 440"}""")));

        var heard = events.OfType<PatchEvent.Heard>().ShouldHaveSingleItem();

        // A sine is about 3 dB of crest, and the reply says so beside the
        // figure — which is what makes "thumping drums" checkable at all.
        heard.Caption.ShouldContain("crest 3.");
        heard.Caption.ShouldContain("12 dB or more");
    }

    /// <summary>
    /// Silence is never played to anybody. A model handed two seconds of nothing
    /// concludes the tool is broken rather than the patch.
    /// </summary>
    [Fact]
    public async Task Silence_is_a_sentence_rather_than_a_clip()
    {
        var (events, sent) = await Listening(
            Asking(Mute),
            Asking(("listen", """{"seconds":0.5}""")),
            Prose("Nothing there — I'll wire it to something that moves."));

        events.OfType<PatchEvent.Heard>().ShouldBeEmpty();
        events.OfType<PatchEvent.Did>().ShouldContain(d => d.Summary.Contains("silence"));

        sent[^1].ToJsonString().ShouldNotContain("audio/wav");
    }

    /// <summary>
    /// A model nobody wrote down falls back to the arrangement ADR-0047 built:
    /// a second model, played the clip on its own, answering in words. The
    /// endpoint is fixed here, so a stranger is a model newer than the table
    /// rather than a different service — but it is still a model this cannot
    /// promise takes a sound.
    /// </summary>
    [Fact]
    public async Task A_driver_that_cannot_hear_borrows_an_ear()
    {
        var canned = new Canned(
            new Answer(Asking(Sounding)),
            new Answer(Asking(("listen", """{"seconds":0.5}"""))),
            new Answer(Prose("One steady low tone and nothing else.")),
            new Answer(Asking(("propose", """{"summary":"a sine at 440"}"""))));

        var events = await Drive(canned, Listener.Another, ownEars: false, ear: "some-ear");

        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();

        // The clip went to the ear on its own, and what came back to the driver
        // is words rather than the sound.
        canned.Urls.ShouldContain(url => url.Contains("models/some-ear:"));

        var told = canned.Sent[^1].ToJsonString();

        told.ShouldContain("one steady low tone and nothing else", Case.Insensitive);
        told.ShouldNotContain("audio/wav");
    }

    /// <summary>
    /// An ear that will not answer costs the description, not the turn. The
    /// sound was rendered and its levels are already measured, so there is
    /// something true to carry on from.
    /// </summary>
    [Fact]
    public async Task An_ear_that_fails_is_a_sentence_rather_than_the_end_of_the_run()
    {
        var canned = new Canned(
            new Answer(Asking(Sounding)),
            new Answer(Asking(("listen", """{"seconds":0.5}"""))),
            new Answer("""{"error":{"message":"that model is not available here."}}""", HttpStatusCode.NotFound),
            new Answer(Asking(("propose", """{"summary":"a sine at 440"}"""))));

        var events = await Drive(canned, Listener.Another, ownEars: false, ear: "some-ear");

        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();
        events.OfType<PatchEvent.Failed>().ShouldBeEmpty();

        var heard = events.OfType<PatchEvent.Heard>().ShouldHaveSingleItem();

        heard.Caption.ShouldContain("not available here");
        heard.Caption.ShouldContain("peak -6", Case.Sensitive);
    }

    // --- what it costs and what it survives ---------------------------------

    [Fact]
    public async Task What_a_turn_cost_is_reported_including_what_it_thought()
    {
        var canned = new Canned(new Answer(Spending(Prose("Hello."))));

        using var session = Session(canned);

        var events = await Drain(session, "hello");
        var cost = events.OfType<PatchEvent.Cost>().ShouldHaveSingleItem();

        cost.Input.ShouldBe(9000);
        cost.CacheRead.ShouldBe(8000);
        cost.Output.ShouldBe(940);
    }

    /// <summary>
    /// A rate limit that clears quickly is absorbed silently. It is an ordinary
    /// event on a conversation that resends a large briefing every turn, and
    /// losing the turn over a wait of a second would be absurd.
    /// </summary>
    [Fact]
    public async Task A_rate_limit_that_clears_quickly_costs_nothing_but_time()
    {
        var canned = new Canned(
            new Answer(
                """{"error":{"message":"overloaded","details":[{"retryDelay":"0.001s"}]}}""",
                HttpStatusCode.TooManyRequests),
            new Answer(Prose("Hello.")));

        using var session = Session(canned);

        var events = await Drain(session, "hello");

        events.OfType<PatchEvent.Failed>().ShouldBeEmpty();
        events.OfType<PatchEvent.Said>().ShouldHaveSingleItem();
    }

    /// <summary>
    /// A refusal that will still be a refusal in a second's time ends the turn
    /// and not the window, saying what the endpoint said.
    /// </summary>
    [Fact]
    public async Task A_bad_request_costs_the_turn_and_says_why()
    {
        var canned = new Canned(new Answer(
            """{"error":{"message":"Unknown name \"parameters\"."}}""",
            HttpStatusCode.BadRequest));

        using var session = Session(canned);

        var events = await Drain(session, "hello");

        events.OfType<PatchEvent.Failed>().ShouldHaveSingleItem()
            .Message.ShouldContain("Unknown name");
    }

    // --- driving it ---------------------------------------------------------

    /// <summary>
    /// One field out of each part, as plain strings.
    /// </summary>
    /// <remarks>
    /// Read out before asserting because Shouldly's predicate is an expression
    /// tree, and a null-conditional cannot appear in one — which is the whole of
    /// why this is a method rather than a lambda at each call.
    /// </remarks>
    private static string?[] Named(JsonArray parts, string part, string field) =>
        [.. parts.Select(each => each?[part]?[field]?.GetValue<string>())];

    private const string Driver = "gemini-test";

    private static async Task<(List<PatchEvent> Events, List<JsonNode> Sent)> Run(params string[] replies)
    {
        var canned = new Canned([.. replies.Select(r => new Answer(r))]);

        return (await Drive(canned, Listener.None, ownEars: false), canned.Sent);
    }

    private static async Task<(List<PatchEvent> Events, List<JsonNode> Sent)> Listening(
        params string[] replies)
    {
        var canned = new Canned([.. replies.Select(r => new Answer(r))]);

        return (await Drive(canned, Listener.Itself, ownEars: true), canned.Sent);
    }

    /// <summary>One message put to a session that may be asked another.</summary>
    private static async Task<List<PatchEvent>> Drain(IPatchSession session, string instruction)
    {
        var events = new List<PatchEvent>();

        await foreach (var happened in session.Ask(instruction, TestContext.Current.CancellationToken))
            events.Add(happened);

        return events;
    }

    private static async Task<List<PatchEvent>> Drive(
        Canned canned,
        Listener hearing,
        bool ownEars,
        string? ear = null)
    {
        using var session = Session(canned, hearing, ownEars, ear);

        return await Drain(session, "make something");
    }

    private static GeminiSession Session(
        Canned canned,
        Listener hearing = Listener.None,
        bool ownEars = false,
        string? ear = null)
    {
        var workbench = new PatchWorkbench(
            NodeCatalog.BuiltIn,
            new Patch(),
            vision: false,
            hearing);

        return new GeminiSession(
            workbench,
            new AssistantConfig(
                "no-key-needed",
                Driver,
                Hearing: hearing is not Listener.None,
                EarModel: ear),
            "https://nowhere.invalid/v1beta",
            thinking: null,
            ownEars,
            canned);
    }

    // --- canned replies -----------------------------------------------------

    private static string Prose(string text) => Talking(text);

    private static string Asking(params (string Name, string Arguments)[] calls) => Talking(null, calls);

    /// <summary>Tool calls with a word about them, which is how a model narrates.</summary>
    private static string Talking(string? said, params (string Name, string Arguments)[] calls)
    {
        var parts = new JsonArray();

        if (said is not null) parts.Add(new JsonObject { ["text"] = said });

        foreach (var (name, arguments) in calls)
        {
            parts.Add(new JsonObject
            {
                ["functionCall"] = new JsonObject
                {
                    ["name"] = name,
                    ["args"] = JsonNode.Parse(arguments),
                },
            });
        }

        return new JsonObject
        {
            ["candidates"] = new JsonArray
            {
                new JsonObject
                {
                    ["content"] = new JsonObject { ["role"] = "model", ["parts"] = parts },
                },
            },
        }.ToJsonString();
    }

    /// <summary>The same answer, with what it cost attached.</summary>
    private static string Spending(string reply)
    {
        var body = JsonNode.Parse(reply)!;

        body["usageMetadata"] = new JsonObject
        {
            ["promptTokenCount"] = 9000,
            ["cachedContentTokenCount"] = 8000,
            ["candidatesTokenCount"] = 40,
            ["thoughtsTokenCount"] = 900,
        };

        return body.ToJsonString();
    }

    /// <summary>One canned answer, with whatever the endpoint would have said around it.</summary>
    private sealed record Answer(string Body, HttpStatusCode Status = HttpStatusCode.OK);

    /// <summary>
    /// Answers each request with the next canned one, and keeps what it was sent
    /// and where. The last answer repeats once they run out, so a test that
    /// wants a refusal to keep happening only has to say it once.
    /// </summary>
    private sealed class Canned(params Answer[] answers) : HttpMessageHandler
    {
        private int next;

        public List<JsonNode> Sent { get; } = [];

        /// <summary>Which model each request went to, which is in the path here.</summary>
        public List<string> Urls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancel)
        {
            var body = await request.Content!.ReadAsStringAsync(cancel).ConfigureAwait(false);

            Sent.Add(JsonNode.Parse(body)!);
            Urls.Add(request.RequestUri!.ToString());

            var answer = answers[Math.Min(next++, answers.Length - 1)];

            return new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body) };
        }
    }
}
