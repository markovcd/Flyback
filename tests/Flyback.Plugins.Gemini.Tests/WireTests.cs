using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Gemini.Tests;

/// <summary>
/// The translation to and from generateContent, checked without a network.
/// </summary>
/// <remarks>
/// Everything here fails the same way in the wild — a 400 naming a field, from
/// an endpoint that cannot say which of the twenty tools it was reading. That is
/// the whole argument for testing the JSON rather than the conversation.
/// </remarks>
public class WireTests
{
    /// <summary>
    /// A tool that takes nothing declares no parameters at all. An object schema
    /// with no properties is not "takes no arguments" here — it is a schema that
    /// fails validation, and it would take every tool down with it.
    /// </summary>
    [Fact]
    public void A_tool_that_takes_nothing_declares_no_parameters()
    {
        Wire.Parameters("{}").ShouldBeNull();
        Wire.Parameters("""{ "properties": {} }""").ShouldBeNull();
    }

    /// <summary>Nonsense is treated as nothing rather than thrown over.</summary>
    [Fact]
    public void A_schema_that_will_not_parse_declares_nothing()
    {
        Wire.Parameters("{ not json").ShouldBeNull();
    }

    [Fact]
    public void A_schema_is_declared_as_an_object()
    {
        var parameters = Wire.Parameters("""{ "properties": { "handle": { "type": "string" } } }""");

        parameters!["type"]!.GetValue<string>().ShouldBe("object");
        parameters["properties"]!["handle"]!["type"]!.GetValue<string>().ShouldBe("string");
    }

    /// <summary>
    /// <c>set_extra</c>'s <c>value</c> takes a number, a boolean or a choice's
    /// id, and says so by carrying no type at all. Everything here must have
    /// one, so the same statement is made in the spelling this accepts.
    /// </summary>
    [Fact]
    public void A_property_with_no_type_becomes_a_choice_of_the_three()
    {
        var parameters = Wire.Parameters("""
            { "properties": { "value": { "description": "a number, a boolean or an id" } } }
            """);

        var choices = parameters!["properties"]!["value"]!["anyOf"]!.AsArray()
            .Select(choice => choice!["type"]!.GetValue<string>())
            .ToArray();

        choices.ShouldBe(["string", "number", "boolean"]);
    }

    /// <summary>
    /// Down through an array's items too, because the schemas are written in the
    /// workbench and it does not know which endpoint is reading them.
    /// </summary>
    [Fact]
    public void A_nested_property_with_no_type_is_reached()
    {
        var parameters = Wire.Parameters("""
            {
              "properties": {
                "knobs": {
                  "type": "array",
                  "items": { "type": "object", "properties": { "value": { "description": "either" } } }
                }
              }
            }
            """);

        parameters!["properties"]!["knobs"]!["items"]!["properties"]!["value"]!["anyOf"]
            .ShouldNotBeNull();
    }

    /// <summary>
    /// Every tool the workbench actually ships, put through the declaration this
    /// sends. A schema this cannot state is a run that fails on its first
    /// request rather than on the call that needed it.
    /// </summary>
    [Fact]
    public void Every_tool_the_workbench_offers_can_be_declared()
    {
        var workbench = new PatchWorkbench(
            NodeCatalog.BuiltIn,
            new Patch(),
            vision: true,
            hearing: Listener.Itself);

        var request = Wire.Request([], "briefing", workbench.Tools, thinking: null);
        var declared = request["tools"]![0]!["functionDeclarations"]!.AsArray();

        declared.Count.ShouldBe(workbench.Tools.Count);

        foreach (var declaration in declared)
        {
            declaration!["name"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
            declaration["description"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();

            // Present or absent, but never an object that says nothing.
            if (declaration["parameters"] is { } parameters)
            {
                parameters["type"]!.GetValue<string>().ShouldBe("object");
                parameters["properties"]!.AsObject().Count.ShouldBeGreaterThan(0);
            }
        }
    }

    /// <summary>
    /// The briefing is a field beside the conversation rather than its first
    /// turn — there is no system role here, and a user one would make the
    /// handbook read as something somebody asked for.
    /// </summary>
    [Fact]
    public void The_briefing_travels_as_a_system_instruction()
    {
        var request = Wire.Request([Wire.User("make it blue")], "the handbook", [], thinking: null);

        request["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>().ShouldBe("the handbook");
        request["contents"]!.AsArray().Count.ShouldBe(1);
        request["contents"]![0]!["role"]!.GetValue<string>().ShouldBe("user");
    }

    /// <summary>
    /// A question with no tools sends no tool fields at all. An empty
    /// declaration list beside a calling mode of AUTO is a contradiction, and
    /// the ear is asked exactly that kind of question.
    /// </summary>
    [Fact]
    public void A_request_with_no_tools_leaves_both_tool_fields_out()
    {
        var request = Wire.Request([], "briefing", [], thinking: null);

        request["tools"].ShouldBeNull();
        request["toolConfig"].ShouldBeNull();
    }

    [Fact]
    public void Effort_is_left_out_entirely_when_there_is_nothing_safe_to_say()
    {
        Wire.Request([], "briefing", [], thinking: null)["generationConfig"].ShouldBeNull();

        var asked = Wire.Request(
            [],
            "briefing",
            [],
            new JsonObject { ["thinkingBudget"] = 128 });

        asked["generationConfig"]!["thinkingConfig"]!["thinkingBudget"]!.GetValue<int>().ShouldBe(128);
    }

    /// <summary>
    /// One user turn: every answer in the order its call arrived, then what
    /// those answers produced. The order is the whole of the association — a
    /// functionResponse carries a name and no id, so two calls of the same tool
    /// are told apart by nothing else.
    /// </summary>
    [Fact]
    public void Answers_go_back_in_order_with_the_media_after_them()
    {
        JsonArray answers =
        [
            Wire.FunctionResponse("render", "a grey field"),
            Wire.FunctionResponse("listen", "peak -6 dBFS"),
        ];

        var turn = Wire.Answers(answers, "Here is what that looked and sounded like.", [[1, 2]], [[3, 4]]);
        var parts = turn["parts"]!.AsArray();

        turn["role"]!.GetValue<string>().ShouldBe("user");

        parts[0]!["functionResponse"]!["name"]!.GetValue<string>().ShouldBe("render");
        parts[1]!["functionResponse"]!["name"]!.GetValue<string>().ShouldBe("listen");
        parts[2]!["text"]!.GetValue<string>().ShouldBe("Here is what that looked and sounded like.");
        parts[3]!["inlineData"]!["mimeType"]!.GetValue<string>().ShouldBe("image/png");
        parts[4]!["inlineData"]!["mimeType"]!.GetValue<string>().ShouldBe("audio/wav");
    }

    /// <summary>A tool result is an object, always. A bare string is refused.</summary>
    [Fact]
    public void A_tool_result_is_wrapped_in_an_object()
    {
        var part = Wire.FunctionResponse("describe_patch", "two modules");

        part["functionResponse"]!["response"]!["result"]!.GetValue<string>().ShouldBe("two modules");
    }

    [Fact]
    public void Words_and_calls_are_read_out_of_the_parts()
    {
        var reply = Wire.Parse(JsonNode.Parse("""
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [
                    { "text": "Adding an oscillator." },
                    { "functionCall": { "name": "add_module", "args": { "type_id": "osc.sine" } } }
                  ]
                }
              }]
            }
            """));

        reply.Text.ShouldBe("Adding an oscillator.");
        reply.Calls.ShouldHaveSingleItem().Name.ShouldBe("add_module");
        reply.Calls[0].Arguments!["type_id"]!.GetValue<string>().ShouldBe("osc.sine");
    }

    /// <summary>
    /// Reasoning comes back as text parts flagged as thought. It is not the
    /// answer, and putting it in the transcript would fill a panel meant for
    /// what was decided with the working out.
    /// </summary>
    [Fact]
    public void Thinking_is_not_mistaken_for_something_it_said()
    {
        var reply = Wire.Parse(JsonNode.Parse("""
            {
              "candidates": [{
                "content": {
                  "parts": [
                    { "text": "The user probably wants...", "thought": true },
                    { "text": "Here you go." }
                  ]
                }
              }]
            }
            """));

        reply.Text.ShouldBe("Here you go.");
    }

    /// <summary>
    /// Thinking is billed as output and is not counted in the candidates, so a
    /// turn at high effort would report a fraction of what it spent — and the
    /// cost line is the only place anybody would notice.
    /// </summary>
    [Fact]
    public void What_it_thought_is_counted_as_what_it_spent()
    {
        var reply = Wire.Parse(JsonNode.Parse("""
            {
              "candidates": [{ "content": { "parts": [{ "text": "done" }] } }],
              "usageMetadata": {
                "promptTokenCount": 9000,
                "cachedContentTokenCount": 8000,
                "candidatesTokenCount": 40,
                "thoughtsTokenCount": 900
              }
            }
            """));

        reply.Input.ShouldBe(9000);
        reply.Cached.ShouldBe(8000);
        reply.Output.ShouldBe(940);
    }

    /// <summary>
    /// A candidate that stopped for safety carries no parts at all. That is an
    /// empty turn, which the loop ends on, rather than something to throw over.
    /// </summary>
    [Fact]
    public void A_reply_with_nothing_in_it_is_read_as_nothing()
    {
        var reply = Wire.Parse(JsonNode.Parse("""
            { "candidates": [{ "finishReason": "SAFETY" }] }
            """));

        reply.Text.ShouldBeNull();
        reply.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void An_answer_with_no_candidates_at_all_is_not_reached_past()
    {
        Should.NotThrow(() => Wire.Parse(JsonNode.Parse("""{ "candidates": [] }""")));
        Should.NotThrow(() => Wire.Parse(JsonNode.Parse("{}")));
    }

    /// <summary>
    /// How long to wait is in the body here, not in a header. A 429 carries a
    /// RetryInfo among the error details and usually nothing else.
    /// </summary>
    [Fact]
    public void A_rate_limit_says_how_long_it_wants_in_the_body()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var wait = Wire.RetryAfter(response, """
            {
              "error": {
                "code": 429,
                "message": "Quota exceeded.",
                "details": [
                  { "@type": "type.googleapis.com/google.rpc.QuotaFailure" },
                  { "@type": "type.googleapis.com/google.rpc.RetryInfo", "retryDelay": "24s" }
                ]
              }
            }
            """);

        wait.ShouldBe(TimeSpan.FromSeconds(24));
    }

    /// <summary>A header still wins where a gateway in front of this adds one.</summary>
    [Fact]
    public void A_header_is_read_before_the_body()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));

        Wire.RetryAfter(response, """{"error":{"details":[{"retryDelay":"24s"}]}}""")
            .ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void A_refusal_that_says_nothing_about_waiting_says_nothing()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);

        Wire.RetryAfter(response, """{"error":{"message":"bad request"}}""").ShouldBeNull();
        Wire.RetryAfter(response, "<html>no</html>").ShouldBeNull();
    }

    [Fact]
    public void A_complaint_names_what_the_endpoint_said()
    {
        Wire.Complaint(400, """{"error":{"message":"Unknown name \"foo\"."}}""")
            .ShouldBe("400: Unknown name \"foo\".");

        Wire.Complaint(500, new string('x', 4000)).ShouldBe("the endpoint answered 500.");
    }
}
