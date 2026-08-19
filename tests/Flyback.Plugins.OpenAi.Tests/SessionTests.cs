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
/// The workbench is a copy, so an assistant that stops without calling
/// <c>propose</c> has changed nothing anyone can see: the editor is untouched,
/// the transcript is full of edits that went nowhere, and Apply stays grey. That
/// is indistinguishable from working, which is why it is pinned here.
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
    /// The bug this was written for. The model built a patch, wrote a summary in
    /// prose and asked for nothing further — and the loop returned in silence,
    /// which reaches the window as an Apply button that never lights up.
    /// </summary>
    [Fact]
    public async Task A_model_that_stops_short_is_told_that_nothing_has_reached_the_editor()
    {
        var (events, sent) = await Run(
            Asking(Building),
            Prose("There you go — a flat grey field."),
            Asking(("propose", """{"summary":"a flat grey field"}""")));

        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem()
            .Summary.ShouldBe("a flat grey field");

        // It was asked to finish rather than left to stop, and asked as a user
        // turn: there is no tool call outstanding to answer at that point.
        var asked = sent[^1]["messages"]!.AsArray()
            .Where(m => m!["role"]!.GetValue<string>() == "user")
            .Select(m => m!["content"]!.ToString())
            .ToArray();

        asked.ShouldContain(text => text.Contains("propose", StringComparison.Ordinal)
            && text.Contains("editor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_that_will_not_propose_says_so_rather_than_going_quiet()
    {
        var (events, sent) = await Run(
            Asking(Building),
            Prose("There you go."),
            Prose("I would rather not."));

        events.OfType<PatchEvent.Proposed>().ShouldBeEmpty();

        // The turn ends with a reason, so the panel has something to show for a
        // button it is about to leave disabled.
        events.OfType<PatchEvent.Failed>().ShouldHaveSingleItem()
            .Message.ShouldContain("nothing to apply");

        // Nudged once, not once per turn. Prodding it repeatedly would spend the
        // whole turn budget arguing.
        sent.Count.ShouldBe(3);
    }

    /// <summary>
    /// The prose it wrote on the way to stopping is still worth showing; the
    /// nudge adds to a turn rather than replacing it.
    /// </summary>
    [Fact]
    public async Task What_it_said_before_stopping_still_reaches_the_transcript()
    {
        var (events, _) = await Run(
            Prose("Thinking about it."),
            Prose("Still thinking."));

        events.OfType<PatchEvent.Said>().Select(s => s.Text)
            .ShouldBe(["Thinking about it.", "Still thinking."]);
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

    // --- driving it ---------------------------------------------------------

    private static async Task<(List<PatchEvent> Events, List<JsonNode> Sent)> Run(params string[] replies)
    {
        var canned = new Canned([.. replies.Select(r => new Answer(r))]);
        return (await Drive(canned), canned.Sent);
    }

    private static async Task<List<PatchEvent>> Drive(Canned canned)
    {
        var workbench = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false);

        using var session = new OpenAiSession(
            workbench,
            new AssistantConfig("no-key-needed", "some-model"),
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

    private static string Asking(params (string Name, string Arguments)[] calls)
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
                        ["content"] = null,
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
