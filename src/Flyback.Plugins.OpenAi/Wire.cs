using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.OpenAi;

/// <summary>One tool call the model asked for.</summary>
internal sealed record Call(string Id, string Name, string Arguments);

/// <summary>What came back from one request.</summary>
internal sealed record Reply(
    string? Text,
    IReadOnlyList<Call> Calls,
    JsonNode? RawMessage,
    int Input,
    int Cached,
    int Output);

/// <summary>
/// The chat-completions wire format, and nothing else.
/// </summary>
/// <remarks>
/// Pure functions over JSON on purpose: this is the part that fails at run time
/// with a 400 rather than at compile time, so it is the part worth testing
/// without a network. Everything that decides <em>what</em> to say lives in
/// <see cref="OpenAiSession"/>; this only decides how to say it.
/// </remarks>
internal static class Wire
{
    public static JsonObject Request(string model, JsonArray messages, IReadOnlyList<PatchTool> tools)
    {
        var declared = new JsonArray();

        foreach (var tool in tools)
        {
            declared.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = Parameters(tool.Schema),
                },
            });
        }

        return new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["tools"] = declared,
            ["tool_choice"] = "auto",
        };
    }

    /// <summary>
    /// A tool's schema is the body of an object schema, so the type is added
    /// here rather than repeated in every tool. An unparseable one becomes an
    /// object with no properties, which is refused by the workbench with a
    /// message rather than by the endpoint with a 400.
    /// </summary>
    private static JsonNode Parameters(string schema)
    {
        JsonObject body;

        try
        {
            body = JsonNode.Parse(schema) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            body = [];
        }

        body["type"] = "object";
        body["properties"] ??= new JsonObject();

        return body;
    }

    public static JsonObject System(string text) => new()
    {
        ["role"] = "system",
        ["content"] = text,
    };

    public static JsonObject User(string text) => new()
    {
        ["role"] = "user",
        ["content"] = text,
    };

    /// <summary>
    /// A picture, as a user turn.
    /// </summary>
    /// <remarks>
    /// It cannot go where it belongs. A <c>tool</c> message in this format
    /// carries a string and nothing else, so a rendered frame is answered with
    /// words and then shown on the next turn. Every tool call still gets exactly
    /// one reply, which is the rule that matters — an unanswered one makes the
    /// whole next request a 400.
    /// </remarks>
    public static JsonObject UserWithPictures(string text, IEnumerable<byte[]> pictures)
    {
        var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } };

        foreach (var picture in pictures)
        {
            content.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject
                {
                    ["url"] = "data:image/png;base64," + Convert.ToBase64String(picture),
                },
            });
        }

        return new JsonObject { ["role"] = "user", ["content"] = content };
    }

    public static JsonObject ToolResult(string id, string text) => new()
    {
        ["role"] = "tool",
        ["tool_call_id"] = id,
        ["content"] = text,
    };

    /// <summary>
    /// Reads one response. Anything missing is treated as absent rather than as
    /// a failure: compatible endpoints differ in what they bother to send, and
    /// the parts this needs are the parts they all agree on.
    /// </summary>
    public static Reply Parse(JsonNode? response)
    {
        // Indexed through the array rather than straight at [0]: an endpoint that
        // answers with no choices at all is answering nothing, and reaching past
        // the end of a JSON array throws rather than coming back empty-handed.
        var choices = response?["choices"] as JsonArray;
        var message = choices is { Count: > 0 } ? choices[0]?["message"] : null;
        var usage = response?["usage"];

        var calls = new List<Call>();

        if (message?["tool_calls"] is JsonArray asked)
        {
            foreach (var call in asked)
            {
                var id = call?["id"]?.GetValue<string>();
                var name = call?["function"]?["name"]?.GetValue<string>();

                if (id is null || name is null) continue;

                calls.Add(new Call(id, name, call?["function"]?["arguments"]?.GetValue<string>() ?? "{}"));
            }
        }

        return new Reply(
            Blank(message?["content"]),
            calls,
            message?.DeepClone(),
            Count(usage?["prompt_tokens"]),
            Count(usage?["prompt_tokens_details"]?["cached_tokens"]),
            Count(usage?["completion_tokens"]));
    }

    /// <summary>
    /// Whether a status is worth sending the same request for a second time.
    /// </summary>
    /// <remarks>
    /// 429 is the one that matters. A rate limit on a conversation that resends
    /// a large stable briefing every turn is an ordinary event rather than a
    /// fault, and the endpoint usually says how long it wants — losing a whole
    /// turn of work over a wait of under a second would be absurd. The 5xx are
    /// here because a gateway that is briefly unwell says the same thing twice
    /// as often as it means it. Everything else in 4xx is a request that will
    /// still be wrong in a second's time.
    /// </remarks>
    public static bool Retryable(int status) =>
        status is 408 or 429 or 500 or 502 or 503 or 504 or 529;

    /// <summary>
    /// How long the endpoint asked to be left alone, or null when it did not say.
    /// </summary>
    /// <remarks>
    /// Three places, because the endpoints this reaches disagree about which one
    /// to use. <c>Retry-After</c> is the standard and carries whole seconds or a
    /// date. <c>retry-after-ms</c> is what OpenAI and Azure actually send on a
    /// 429, and it is the precise one — the wait is routinely under the second
    /// that the standard header would have to round to. The reset headers are
    /// the fallback, and the longer of the two wins: they say when each bucket
    /// refills and there is no telling from here which of them was hit.
    /// </remarks>
    public static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var headers = response.Headers;

        if (headers.TryGetValues("retry-after-ms", out var precise)
            && double.TryParse(
                precise.FirstOrDefault(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var milliseconds))
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        if (headers.RetryAfter is { } asked)
        {
            if (asked.Delta is { } delta) return delta;
            if (asked.Date is { } when) return when - DateTimeOffset.UtcNow;
        }

        string[] buckets = ["x-ratelimit-reset-tokens", "x-ratelimit-reset-requests"];
        TimeSpan? longest = null;

        foreach (var bucket in buckets)
        {
            if (!headers.TryGetValues(bucket, out var values)) continue;
            if (Duration(values.FirstOrDefault()) is not { } reset) continue;

            if (longest is null || reset > longest) longest = reset;
        }

        return longest;
    }

    private static readonly Regex Parts = new(
        @"\d+(?:\.\d+)?(?:ms|s|m|h)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A duration written the way a person would — <c>916ms</c>, <c>1.5s</c>,
    /// <c>1m30s</c> — which is how the reset headers carry one. Parts are summed,
    /// so the compound forms read correctly.
    /// </summary>
    private static TimeSpan? Duration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var total = TimeSpan.Zero;
        var found = false;

        foreach (var part in Parts.EnumerateMatches(text))
        {
            var match = text.AsSpan(part.Index, part.Length);
            var split = match.Length;

            while (split > 0 && !char.IsAsciiDigit(match[split - 1])) split--;

            if (!double.TryParse(
                match[..split],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
            {
                continue;
            }

            total += match[split..] switch
            {
                "ms" => TimeSpan.FromMilliseconds(value),
                "s" => TimeSpan.FromSeconds(value),
                "m" => TimeSpan.FromMinutes(value),
                "h" => TimeSpan.FromHours(value),
                _ => TimeSpan.Zero,
            };

            found = true;
        }

        return found ? total : null;
    }

    /// <summary>Whatever the endpoint said went wrong, or the status on its own.</summary>
    public static string Complaint(int status, string body)
    {
        try
        {
            if (JsonNode.Parse(body)?["error"] is { } error)
            {
                var message = error["message"]?.GetValue<string>() ?? error.ToString();
                return $"{status}: {message}";
            }
        }
        catch (JsonException)
        {
            // Not JSON. Some gateways answer in HTML, which is not worth showing.
        }

        return body.Length is > 0 and < 300 ? $"{status}: {body}" : $"the endpoint answered {status}.";
    }

    private static string? Blank(JsonNode? node)
    {
        var text = node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int Count(JsonNode? node)
    {
        try
        {
            return node?.GetValueKind() == JsonValueKind.Number ? node.GetValue<int>() : 0;
        }
        catch (FormatException)
        {
            return 0;
        }
    }
}
