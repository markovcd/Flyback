using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.OpenAi.Tests;

/// <summary>
/// The translation to and from the wire. Everything here fails at run time with
/// a rejected request rather than at compile time, which is exactly why it is
/// worth pinning without a network.
/// </summary>
public class WireTests
{
    private static readonly PatchTool Adder = new(
        "add_module",
        "Places a module.",
        """{ "properties": { "type_id": { "type": "string" } }, "required": ["type_id"] }""");

    // --- what goes out ------------------------------------------------------

    [Fact]
    public void A_tool_becomes_a_function_with_a_usable_schema()
    {
        var request = Wire.Request("some-model", [Wire.User("hello")], [Adder]);

        var function = request["tools"]![0]!["function"]!;

        request["model"]!.GetValue<string>().ShouldBe("some-model");
        function["name"]!.GetValue<string>().ShouldBe("add_module");
        function["description"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();

        // The schema body is declared as an object here rather than in every
        // tool, so this is where that has to be true.
        function["parameters"]!["type"]!.GetValue<string>().ShouldBe("object");
        function["parameters"]!["properties"]!["type_id"].ShouldNotBeNull();
        function["parameters"]!["required"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public void Every_tool_the_workbench_offers_survives_the_trip()
    {
        var tools = new PatchWorkbench(Core.Graph.NodeCatalog.BuiltIn, new Core.Graph.Patch()).Tools;

        var request = Wire.Request("some-model", [Wire.User("hello")], tools);

        request["tools"]!.AsArray().Count.ShouldBe(tools.Count);

        foreach (var declared in request["tools"]!.AsArray())
        {
            var parameters = declared!["function"]!["parameters"]!;

            parameters["type"]!.GetValue<string>().ShouldBe("object");
            parameters["properties"].ShouldNotBeNull();

            // It has to be re-readable as JSON, because that is what the far end
            // will do with it.
            Should.NotThrow(() => JsonDocument.Parse(parameters.ToJsonString()));
        }
    }

    /// <summary>
    /// A schema that will not parse must not take the request down with it. The
    /// workbench refuses the call with a message the model can read; a 400
    /// would end the conversation instead.
    /// </summary>
    [Fact]
    public void A_tool_with_a_broken_schema_still_produces_a_valid_request()
    {
        var broken = new PatchTool("odd", "does something", "{ not json");

        var parameters = Wire.Request("m", [Wire.User("x")], [broken])["tools"]![0]!["function"]!["parameters"]!;

        parameters["type"]!.GetValue<string>().ShouldBe("object");
        parameters["properties"].ShouldNotBeNull();
    }

    [Fact]
    public void A_conversation_can_be_sent_more_than_once()
    {
        // A JSON node belongs to one parent, so a request that adopted the
        // conversation would poison the next one. This is that bug as a test.
        JsonArray conversation = [Wire.System("rules"), Wire.User("hello")];

        Should.NotThrow(() =>
        {
            Wire.Request("m", (JsonArray)conversation.DeepClone(), [Adder]);
            Wire.Request("m", (JsonArray)conversation.DeepClone(), [Adder]);
        });
    }

    /// <summary>
    /// A tool message in this format carries a string and nothing else, so a
    /// rendered frame has to arrive as a user turn afterwards. Getting this
    /// wrong is a rejected request, not a missing picture.
    /// </summary>
    [Fact]
    public void A_picture_travels_as_a_user_turn_not_a_tool_result()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47];

        var message = Wire.UserWithPictures("here", [png]);
        var content = message["content"]!.AsArray();

        message["role"]!.GetValue<string>().ShouldBe("user");
        content[0]!["type"]!.GetValue<string>().ShouldBe("text");
        content[1]!["type"]!.GetValue<string>().ShouldBe("image_url");
        content[1]!["image_url"]!["url"]!.GetValue<string>()
            .ShouldStartWith("data:image/png;base64,");
    }

    /// <summary>
    /// A sound is spelled quite differently from a picture, and neither spelling
    /// is negotiable: bare base64 and a separate format, where a picture is a
    /// data URL. This is the part that fails as a 400 naming the parameter
    /// rather than as anything visible from here.
    /// </summary>
    [Fact]
    public void A_sound_travels_as_bare_base64_with_its_format_beside_it()
    {
        byte[] wav = [0x52, 0x49, 0x46, 0x46];

        var content = Wire.UserWithMedia("here", [], [wav])["content"]!.AsArray();

        content[0]!["type"]!.GetValue<string>().ShouldBe("text");
        content[1]!["type"]!.GetValue<string>().ShouldBe("input_audio");
        content[1]!["input_audio"]!["format"]!.GetValue<string>().ShouldBe("wav");
        content[1]!["input_audio"]!["data"]!.GetValue<string>().ShouldBe(Convert.ToBase64String(wav));
    }

    /// <summary>
    /// The two spellings side by side, which is the only place they can be
    /// compared. Nothing sends both at once — a picture goes to the model being
    /// driven and a sound goes to the ear — but one method spells both, and a
    /// method that muddled them would put a WAV in an <c>image_url</c>.
    /// </summary>
    [Fact]
    public void A_picture_and_a_sound_are_spelled_differently_in_the_same_message()
    {
        var content = Wire.UserWithMedia("here", [[0x89, 0x50]], [[0x52, 0x49]])["content"]!.AsArray();

        content.Count.ShouldBe(3);
        content[1]!["type"]!.GetValue<string>().ShouldBe("image_url");
        content[2]!["type"]!.GetValue<string>().ShouldBe("input_audio");
    }

    [Fact]
    public void A_tool_result_names_the_call_it_answers()
    {
        var message = Wire.ToolResult("call_7", "ok. no issues.");

        message["role"]!.GetValue<string>().ShouldBe("tool");
        message["tool_call_id"]!.GetValue<string>().ShouldBe("call_7");
        message["content"]!.GetValue<string>().ShouldBe("ok. no issues.");
    }

    // --- what comes back ----------------------------------------------------

    [Fact]
    public void Plain_prose_is_read_back()
    {
        var reply = Wire.Parse(JsonNode.Parse("""
            {"choices":[{"message":{"role":"assistant","content":"Building it now."}}],
             "usage":{"prompt_tokens":120,"completion_tokens":8,"prompt_tokens_details":{"cached_tokens":100}}}
            """));

        reply.Text.ShouldBe("Building it now.");
        reply.Calls.ShouldBeEmpty();
        reply.Input.ShouldBe(120);
        reply.Cached.ShouldBe(100);
        reply.Output.ShouldBe(8);
    }

    [Fact]
    public void Tool_calls_are_read_back_with_their_arguments()
    {
        var reply = Wire.Parse(JsonNode.Parse("""
            {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[
              {"id":"call_1","type":"function","function":{"name":"add_module","arguments":"{\"type_id\":\"osc.sine\"}"}}
            ]}}]}
            """));

        reply.Text.ShouldBeNull();

        var call = reply.Calls.ShouldHaveSingleItem();
        call.Id.ShouldBe("call_1");
        call.Name.ShouldBe("add_module");

        JsonDocument.Parse(call.Arguments).RootElement
            .GetProperty("type_id").GetString().ShouldBe("osc.sine");
    }

    [Fact]
    public void The_assistant_turn_comes_back_whole_so_it_can_be_sent_again()
    {
        var reply = Wire.Parse(JsonNode.Parse("""
            {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[
              {"id":"call_1","type":"function","function":{"name":"reset","arguments":"{}"}}
            ]}}]}
            """));

        // It is echoed back into the conversation verbatim; a rebuilt one would
        // lose the call ids the next tool results have to match.
        reply.RawMessage.ShouldNotBeNull();
        reply.RawMessage!["tool_calls"]![0]!["id"]!.GetValue<string>().ShouldBe("call_1");

        Should.NotThrow(() => new JsonArray().Add(reply.RawMessage));
    }

    /// <summary>
    /// Compatible endpoints differ in what they bother to send. Anything absent
    /// has to read as absent rather than as a failure.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"role":"assistant"}}]}""")]
    [InlineData("""{"choices":[{"message":{"content":"hi"}}],"usage":{}}""")]
    [InlineData("""{"choices":[{"message":{"content":"hi","tool_calls":[{"id":"x"}]}}]}""")]
    public void A_thin_response_is_read_rather_than_refused(string json)
    {
        var reply = Should.NotThrow(() => Wire.Parse(JsonNode.Parse(json)));

        reply.Calls.ShouldNotBeNull();
        reply.Input.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Nothing_at_all_is_read_rather_than_refused()
    {
        var reply = Should.NotThrow(() => Wire.Parse(null));

        reply.Text.ShouldBeNull();
        reply.Calls.ShouldBeEmpty();
    }

    // --- when it goes wrong -------------------------------------------------

    [Fact]
    public void An_endpoints_complaint_is_repeated_rather_than_its_status()
    {
        var said = Wire.Complaint(401, """{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error"}}""");

        said.ShouldContain("401");
        said.ShouldContain("Incorrect API key provided.");
    }

    [Fact]
    public void A_complaint_that_is_not_json_still_says_something()
    {
        Wire.Complaint(502, "<html>Bad Gateway</html>").ShouldContain("502");
        Wire.Complaint(500, new string('x', 4000)).ShouldContain("500");
    }

    // --- being told to wait -------------------------------------------------

    [Theory]
    [InlineData(429, true)]
    [InlineData(408, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(529, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(422, false)]
    public void Only_a_refusal_that_might_pass_is_worth_sending_again(int status, bool again) =>
        Wire.Retryable(status).ShouldBe(again);

    /// <summary>
    /// The precise one wins. A real 429 asks for well under a second, which is
    /// what <c>Retry-After</c> would have to round up to a whole one.
    /// </summary>
    [Fact]
    public void The_millisecond_header_is_preferred_to_the_standard_one()
    {
        using var response = Refused([("retry-after-ms", "916"), ("Retry-After", "1")]);

        Wire.RetryAfter(response).ShouldBe(TimeSpan.FromMilliseconds(916));
    }

    [Fact]
    public void The_standard_header_is_read_when_it_is_the_only_one()
    {
        using var response = Refused([("Retry-After", "2")]);

        Wire.RetryAfter(response).ShouldBe(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// The reset headers carry a duration written the way a person would. The
    /// longer of the two wins: they say when each bucket refills and there is no
    /// telling from out here which of them was hit.
    /// </summary>
    [Fact]
    public void The_reset_headers_are_read_as_written_and_the_longer_wins()
    {
        using var response = Refused([
            ("x-ratelimit-reset-tokens", "916ms"),
            ("x-ratelimit-reset-requests", "1m30s"),
        ]);

        Wire.RetryAfter(response).ShouldBe(TimeSpan.FromSeconds(90));
    }

    [Theory]
    [InlineData("916ms", 0.916)]
    [InlineData("1.5s", 1.5)]
    [InlineData("2m", 120)]
    [InlineData("1m30s", 90)]
    [InlineData("1h", 3600)]
    public void A_reset_is_read_however_it_is_written(string written, double seconds)
    {
        using var response = Refused([("x-ratelimit-reset-tokens", written)]);

        Wire.RetryAfter(response)!.Value.TotalSeconds.ShouldBe(seconds, 0.001);
    }

    /// <summary>
    /// An endpoint that says nothing about waiting must read as saying nothing,
    /// so the caller falls back to its own backoff rather than to zero.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("unknown")]
    public void A_reset_that_says_nothing_useful_is_no_answer(string written)
    {
        using var response = Refused([("x-ratelimit-reset-tokens", written)]);

        Wire.RetryAfter(response).ShouldBeNull();
    }

    [Fact]
    public void An_endpoint_that_does_not_say_how_long_is_not_guessed_at() =>
        Wire.RetryAfter(Refused([])).ShouldBeNull();

    private static HttpResponseMessage Refused((string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        foreach (var (name, value) in headers)
            response.Headers.TryAddWithoutValidation(name, value);

        return response;
    }
}
