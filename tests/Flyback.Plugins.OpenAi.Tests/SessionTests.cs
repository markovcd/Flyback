using System.Net;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.OpenAi.Tests;

/// <summary>
/// How a turn ends, driven by canned replies rather than by an endpoint.
/// </summary>
/// <remarks>
/// A turn ends in one of three ways and all three are ordinary: with a patch
/// proposed, with the model having stopped talking, or with the person having
/// cancelled it. The workbench is a copy, so the two that reach no proposal have
/// changed nothing anyone can see — which is why they are ends rather than
/// failures, and why nothing here answers one by asking again.
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
    /// The quiet failure. Nothing reaches the editor until <c>propose</c>, so a
    /// model that builds a patch and then signs off leaves the person looking at
    /// the patch they already had — one did exactly this, and told them they
    /// were "set to further refine" a patch that was not on their canvas.
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

    /// <summary>
    /// A turn that only asked a question says nothing of the sort. There is
    /// nothing waiting to be offered, so the canvas is not out of date and
    /// saying it were would be noise on every question anybody asks.
    /// </summary>
    [Fact]
    public async Task A_question_with_nothing_built_is_left_alone()
    {
        var (events, sent) = await Run(Prose("Which key should it be in?"));

        sent.Count.ShouldBe(1);
        events.OfType<PatchEvent.Did>().ShouldBeEmpty();
        events.OfType<PatchEvent.Said>().ShouldHaveSingleItem();
    }

    /// <summary>And a turn that did offer its work has nothing to warn about.</summary>
    [Fact]
    public async Task A_turn_that_proposed_says_nothing_about_the_canvas()
    {
        var (events, _) = await Run(
            Asking(Building),
            Asking(("propose", """{"summary":"a flat grey field"}""")));

        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();
        events.OfType<PatchEvent.Did>().ShouldNotContain(d => d.Summary.Contains("canvas still shows"));
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
    /// A second message is asked of the same conversation, carrying everything
    /// the first one said and built.
    /// </summary>
    [Fact]
    public async Task A_second_message_keeps_the_first_ones_history()
    {
        var canned = new Canned(
            new Answer(Asking(Building)),
            new Answer(Prose("Done. Grey enough?")),
            new Answer(Asking(("propose", """{"summary":"a flat grey field"}"""))));

        var workbench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false);

        using var session = new OpenAiSession(
            workbench,
            new AssistantConfig("no-key-needed", "some-model"),
            "https://nowhere.invalid/v1",
            canned);

        await Drain(session, "make a grey field");
        var second = await Drain(session, "yes, propose it");

        second.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem()
            .Patch.Nodes.Count.ShouldBe(2, "it proposed the patch the first message built");

        // Both messages are in the last request, and so is what it said between
        // them. A session that started over would have sent one.
        var sent = canned.Sent[^1].ToJsonString();

        sent.ShouldContain("make a grey field");
        sent.ShouldContain("Grey enough?");
        sent.ShouldContain("yes, propose it");
    }

    /// <summary>
    /// A proposal belongs to the turn that made it. The workbench remembers one
    /// until it is told otherwise, so a conversation carrying on past a proposal
    /// would hand the same patch over again — as the answer to a message that
    /// only asked a question.
    /// </summary>
    [Fact]
    public async Task A_proposal_is_not_offered_a_second_time_by_the_next_message()
    {
        var canned = new Canned(
            new Answer(Asking(Building)),
            new Answer(Asking(("propose", """{"summary":"a flat grey field"}"""))),
            new Answer(Prose("It is the one I just offered you.")));

        var workbench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false);

        using var session = new OpenAiSession(
            workbench,
            new AssistantConfig("no-key-needed", "some-model"),
            "https://nowhere.invalid/v1",
            canned);

        (await Drain(session, "make a grey field")).OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();

        var second = await Drain(session, "what did you build?");

        second.OfType<PatchEvent.Proposed>().ShouldBeEmpty();
        second.OfType<PatchEvent.Said>().ShouldHaveSingleItem();

        // The patch itself is still there to carry on from — only the offer was
        // taken back.
        workbench.Snapshot().Nodes.Count.ShouldBe(2);
    }

    /// <summary>
    /// A question is an answer. A model that stops to ask something has ended
    /// its turn on purpose, and the turn ends there — no proposal, no failure,
    /// and nothing sent back arguing with it.
    /// </summary>
    /// <remarks>
    /// A model may stop to ask a question instead of proposing a patch.
    /// </remarks>
    [Fact]
    public async Task A_model_that_stops_to_ask_something_is_left_to_stop()
    {
        var (events, sent) = await Run(
            Asking(Building),
            Prose("Should the field be grey, or did you want it to move?"));

        events.OfType<PatchEvent.Said>().Select(s => s.Text)
            .ShouldContain(text => text.Contains("did you want it to move", StringComparison.Ordinal));

        events.OfType<PatchEvent.Proposed>().ShouldBeEmpty();
        events.OfType<PatchEvent.Failed>().ShouldBeEmpty();

        // Two requests: the one that built and the one that asked. Nothing
        // followed the question, because the next thing to happen is the person
        // answering it.
        sent.Count.ShouldBe(2);
    }

    /// <summary>
    /// The same, without even a question. Stopping with nothing to show is the
    /// model's to do, and the transcript already carries whatever it said.
    /// </summary>
    [Fact]
    public async Task A_model_that_will_not_propose_is_not_argued_with()
    {
        var (events, sent) = await Run(
            Asking(Building),
            Prose("I would rather not."));

        events.OfType<PatchEvent.Proposed>().ShouldBeEmpty();
        events.OfType<PatchEvent.Failed>().ShouldBeEmpty();

        sent.Count.ShouldBe(2);
    }

    /// <summary>
    /// A turn whose whole content is prose still reaches the transcript. It is
    /// the only thing that turn produced, and with nothing arguing back it is
    /// the only thing the person has to answer.
    /// </summary>
    [Fact]
    public async Task What_it_said_before_stopping_still_reaches_the_transcript()
    {
        var (events, _) = await Run(Prose("Thinking about it."));

        events.OfType<PatchEvent.Said>().Select(s => s.Text).ShouldBe(["Thinking about it."]);
    }

    /// <summary>
    /// Prose in the middle of working is not stopping. A model that says what it
    /// is about to do and then does it carries on, because the turn ends on
    /// having nothing left to ask for rather than on having spoken.
    /// </summary>
    [Fact]
    public async Task Saying_something_while_still_working_does_not_end_the_turn()
    {
        var (events, sent) = await Run(
            Talking("Adding the field now.", Building),
            Asking(("propose", """{"summary":"a flat grey field"}""")));

        events.OfType<PatchEvent.Said>().ShouldHaveSingleItem();
        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();
        sent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_endpoint_that_refuses_costs_the_turn_rather_than_throwing()
    {
        var canned = new Canned(Refusing(
            HttpStatusCode.Unauthorized,
            """{"error":{"message":"Incorrect API key provided."}}"""));

        var events = await Drive(canned);

        events.OfType<PatchEvent.Failed>().ShouldHaveSingleItem()
            .Message.ShouldContain("Incorrect API key provided.");

        // Not worth a second go. A key that is wrong is wrong again in a second.
        canned.Sent.Count.ShouldBe(1);
    }

    // --- being told to wait -------------------------------------------------

    /// <summary>
    /// A conversation that resends a large briefing every turn hits the tokens
    /// per minute limit as a matter of course, and the endpoint says how long it
    /// wants — under a second, usually. Losing the whole turn over that wastes
    /// everything the run has already paid for.
    /// </summary>
    [Fact]
    public async Task A_rate_limit_is_waited_out_rather_than_failing_the_turn()
    {
        var canned = new Canned(
            Limited("Rate limit reached for gpt-4o ... Please try again in 916ms."),
            new Answer(Asking(Building)),
            new Answer(Asking(("propose", """{"summary":"a flat grey field"}"""))));

        var events = await Drive(canned);

        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem()
            .Summary.ShouldBe("a flat grey field");

        events.OfType<PatchEvent.Failed>().ShouldBeEmpty();

        // Three requests for two turns: the refused one was sent again, whole,
        // inside the turn that was refused rather than costing it.
        canned.Sent.Count.ShouldBe(3);
        canned.Sent[0].ToJsonString().ShouldBe(canned.Sent[1].ToJsonString());
    }

    [Fact]
    public async Task A_rate_limit_that_will_not_clear_is_reported_rather_than_waited_on_forever()
    {
        var canned = new Canned(Limited("Rate limit reached for gpt-4o."));

        var events = await Drive(canned);

        events.OfType<PatchEvent.Failed>().ShouldHaveSingleItem()
            .Message.ShouldContain("Rate limit reached");

        // Tried, but a bounded number of times.
        canned.Sent.Count.ShouldBe(5);
    }

    /// <summary>
    /// A limit that clears further out than the session will wait is a quota
    /// rather than a hiccup. Saying so beats a panel that sits still for a
    /// minute and then fails anyway.
    /// </summary>
    [Fact]
    public async Task A_wait_longer_than_the_session_will_sit_out_is_reported_at_once()
    {
        var canned = new Canned(new Answer(
            """{"error":{"message":"Rate limit reached."}}""",
            HttpStatusCode.TooManyRequests,
            [("retry-after-ms", "600000")]));

        var events = await Drive(canned);

        events.OfType<PatchEvent.Failed>().ShouldHaveSingleItem()
            .Message.ShouldContain("Rate limit reached");

        canned.Sent.Count.ShouldBe(1);
    }

    /// <summary>
    /// A sound goes to a second model on its own, and never into the
    /// conversation.
    /// </summary>
    /// <remarks>
    /// Both halves are the point. The models that take a sound require every
    /// request to carry one, so a conversation driven by one is refused on its
    /// first turn — before anything has been rendered to listen to — and they do
    /// not take a picture besides. Asked on its own, the ear answers one
    /// question about one sound, and the WAV is sent once rather than left in a
    /// history that is resent every turn.
    /// </remarks>
    [Fact]
    public async Task A_sound_goes_to_the_ear_alone_and_never_into_the_conversation()
    {
        var (events, sent) = await Listening(
            Asking(Sounding),
            Asking(("listen", """{"seconds":0.5}""")),
            Prose("A steady mid tone, clean, with no movement in it."),
            Asking(("propose", """{"summary":"a sine at 440"}""")));

        var heard = events.OfType<PatchEvent.Heard>().ShouldHaveSingleItem();
        heard.Wav.Length.ShouldBeGreaterThan(44);

        var toTheEar = sent.Where(r => r["model"]!.GetValue<string>() == Ears).ShouldHaveSingleItem();

        // Asked as a question rather than as a turn of a loop: no tools, so
        // nothing can come back but the words.
        toTheEar["tools"].ShouldBeNull();
        toTheEar["tool_choice"].ShouldBeNull();

        var sound = toTheEar["messages"]!.AsArray()
            .Select(m => m!["content"])
            .OfType<JsonArray>()
            .SelectMany(content => content)
            .Where(part => part!["type"]!.GetValue<string>() == "input_audio")
            .ToArray()
            .ShouldHaveSingleItem();

        // Bare base64, with no data URL around it. A picture is spelled the
        // other way and the two are not interchangeable.
        var data = sound!["input_audio"]!["data"]!.GetValue<string>();

        sound["input_audio"]!["format"]!.GetValue<string>().ShouldBe("wav");
        data.ShouldNotStartWith("data:");
        Convert.FromBase64String(data).Length.ShouldBe(heard.Wav.Length);

        // And not one byte of it in the conversation, on any turn.
        sent.Where(r => r["model"]!.GetValue<string>() != Ears)
            .ShouldAllBe(r => !r.ToJsonString().Contains("input_audio"));
    }

    /// <summary>
    /// What the ear said comes back as the answer to the tool call, because that
    /// is the only way the model driving this ever learns it.
    /// </summary>
    [Fact]
    public async Task What_the_ear_heard_is_what_answers_the_call()
    {
        var (events, sent) = await Listening(
            Asking(Sounding),
            Asking(("listen", """{"seconds":0.5}""")),
            Prose("A steady mid tone, clean, with no buzz on it."),
            Asking(("propose", """{"summary":"a sine at 440"}""")));

        var answer = sent[^1]["messages"]!.AsArray()
            .Where(m => m!["role"]!.GetValue<string>() == "tool")
            .Select(m => m!["content"]!.GetValue<string>())
            .Single(text => text.Contains("dBFS", StringComparison.Ordinal));

        // Both halves: what was measured here, and what was heard there.
        answer.ShouldContain("peak -6");
        answer.ShouldContain("no buzz on it");

        events.OfType<PatchEvent.Heard>().ShouldHaveSingleItem()
            .Caption.ShouldContain("no buzz on it");
    }

    /// <summary>
    /// The ear is told nothing about the patch — not what it is, not what the
    /// model was hoping to hear.
    /// </summary>
    /// <remarks>
    /// A model told what it is listening for can echo that expectation back
    /// regardless of the clip — asked to listen for a kickdrum, a hihat and a
    /// melody, it will duly report one even over three steady tones with no
    /// hit anywhere. A description that could not have come back wrong is not
    /// evidence, so the expectation stays on this side of the request.
    /// </remarks>
    [Fact]
    public async Task The_ear_is_never_told_what_it_is_supposed_to_hear()
    {
        var (_, sent) = await Listening(
            Asking(Sounding),
            Asking(("listen", """{"seconds":0.5,"note":"checking the kickdrum and the hihat"}""")),
            Prose("One steady low tone and nothing else."),
            Asking(("propose", """{"summary":"a sine at 440"}""")));

        var asked = sent.Single(r => r["model"]!.GetValue<string>() == Ears).ToJsonString();

        asked.ShouldNotContain("kickdrum");
        asked.ShouldNotContain("hihat");
    }

    /// <summary>
    /// Crest is the measurement that catches a listener agreeing with a
    /// description of drums over a clip that has none: a steady tone cannot
    /// measure like something with hits in it, whatever anybody says about it.
    /// </summary>
    [Fact]
    public async Task What_the_samples_say_goes_back_beside_what_the_ear_says()
    {
        var (events, _) = await Listening(
            Asking(Sounding),
            Asking(("listen", """{"seconds":0.5}""")),
            Prose("Thumping drums throughout."),
            Asking(("propose", """{"summary":"a sine at 440"}""")));

        var heard = events.OfType<PatchEvent.Heard>().ShouldHaveSingleItem();

        // A sine is about 3 dB of crest, and the reply says so beside the
        // figure — which is what makes "thumping drums" checkable from here.
        heard.Caption.ShouldContain("crest 3.");
        heard.Caption.ShouldContain("12 dB or more");
        heard.Caption.ShouldContain("Measured from the samples, not heard");
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

        var events = await Drive(canned, hearing: true);

        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();
        events.OfType<PatchEvent.Failed>().ShouldBeEmpty();

        var heard = events.OfType<PatchEvent.Heard>().ShouldHaveSingleItem();

        heard.Caption.ShouldContain("not available here");
        heard.Caption.ShouldContain("peak -6", Case.Sensitive);
    }

    /// <summary>
    /// Nothing asks for audio back, from either model. This is a tool loop, and
    /// the ear is asked what it hears rather than asked to speak.
    /// </summary>
    [Fact]
    public async Task Nothing_ever_asks_a_model_to_answer_in_sound()
    {
        var (_, sent) = await Listening(
            Asking(Sounding),
            Asking(("listen", """{"seconds":0.5}""")),
            Prose("A steady mid tone."),
            Asking(("propose", """{"summary":"a sine at 440"}""")));

        foreach (var request in sent)
        {
            request["modalities"].ShouldBeNull();
            request["audio"].ShouldBeNull();
        }
    }

    // --- driving it ---------------------------------------------------------

    private static async Task<(List<PatchEvent> Events, List<JsonNode> Sent)> Run(params string[] replies)
    {
        var canned = new Canned([.. replies.Select(r => new Answer(r))]);
        return (await Drive(canned), canned.Sent);
    }

    private static async Task<(List<PatchEvent> Events, List<JsonNode> Sent)> Listening(
        params string[] replies)
    {
        var canned = new Canned([.. replies.Select(r => new Answer(r))]);
        return (await Drive(canned, hearing: true), canned.Sent);
    }

    /// <summary>The model that listens, which is never the one being driven.</summary>
    private const string Ears = "some-ear";

    /// <summary>One message put to a session that may be asked another.</summary>
    private static async Task<List<PatchEvent>> Drain(IPatchSession session, string instruction)
    {
        var events = new List<PatchEvent>();

        await foreach (var happened in session.Ask(instruction, TestContext.Current.CancellationToken))
            events.Add(happened);

        return events;
    }

    private static async Task<List<PatchEvent>> Drive(Canned canned, bool hearing = false)
    {
        var workbench = new PatchWorkbench(
            NodeCatalog.BuiltIn,
            new Patch(),
            vision: false,
            hearing);

        using var session = new OpenAiSession(
            workbench,
            new AssistantConfig(
                "no-key-needed",
                "some-model",
                Hearing: hearing,
                EarModel: hearing ? Ears : null),
            "https://nowhere.invalid/v1",
            canned);

        var events = new List<PatchEvent>();

        await foreach (var happened in session.Ask("make something", TestContext.Current.CancellationToken))
            events.Add(happened);

        return events;
    }

    // --- canned replies -----------------------------------------------------

    private static string Prose(string text) => new JsonObject
    {
        ["choices"] = new JsonArray
        {
            new JsonObject
            {
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text },
            },
        },
    }.ToJsonString();

    private static string Asking(params (string Name, string Arguments)[] calls) =>
        Talking(null, calls);

    /// <summary>Tool calls with a word about them, which is how a model narrates.</summary>
    private static string Talking(string? said, params (string Name, string Arguments)[] calls)
    {
        var asked = new JsonArray();

        for (var i = 0; i < calls.Length; i++)
        {
            asked.Add(new JsonObject
            {
                ["id"] = "call_" + i,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = calls[i].Name,
                    ["arguments"] = calls[i].Arguments,
                },
            });
        }

        return new JsonObject
        {
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["message"] = new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = said,
                        ["tool_calls"] = asked,
                    },
                },
            },
        }.ToJsonString();
    }

    /// <summary>One canned answer, with whatever the endpoint would have said around it.</summary>
    private sealed record Answer(
        string Body,
        HttpStatusCode Status = HttpStatusCode.OK,
        (string Name, string Value)[]? Headers = null);

    /// <summary>
    /// Refused, and asking to be tried again almost immediately — the shape of a
    /// real 429, whose wait is routinely under the second that the standard
    /// header would have to round to. Milliseconds so the tests do not sleep.
    /// </summary>
    private static Answer Limited(string message) => new(
        new JsonObject { ["error"] = new JsonObject { ["message"] = message } }.ToJsonString(),
        HttpStatusCode.TooManyRequests,
        [("retry-after-ms", "1")]);

    private static Answer Refusing(HttpStatusCode status, string body) => new(body, status);

    /// <summary>
    /// Answers each request with the next canned one, and keeps what it was
    /// sent. The last answer repeats once they run out, so a test that wants a
    /// refusal to keep happening only has to say it once.
    /// </summary>
    private sealed class Canned(params Answer[] answers) : HttpMessageHandler
    {
        private int next;

        public List<JsonNode> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancel)
        {
            var body = await request.Content!.ReadAsStringAsync(cancel).ConfigureAwait(false);
            Sent.Add(JsonNode.Parse(body)!);

            var answer = answers[Math.Min(next++, answers.Length - 1)];
            var response = new HttpResponseMessage(answer.Status)
            {
                Content = new StringContent(answer.Body),
            };

            foreach (var (name, value) in answer.Headers ?? [])
                response.Headers.TryAddWithoutValidation(name, value);

            return response;
        }
    }
}
