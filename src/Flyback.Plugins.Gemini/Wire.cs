using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.Gemini;

/// <summary>One tool call the model asked for.</summary>
/// <remarks>
/// No id, unlike the chat-completions spelling. A <c>functionCall</c> carries a
/// name and nothing to match a reply to, so a turn that asked for the same tool
/// twice is answered by order: the <c>functionResponse</c> parts go back in the
/// order the calls arrived, which is the whole of the association.
/// </remarks>
/// <param name="Arguments">Already a JSON object here, where the other format sends a string of one.</param>
internal sealed record Call(string Name, JsonNode? Arguments);

/// <summary>What came back from one request.</summary>
internal sealed record Reply(
    string? Text,
    IReadOnlyList<Call> Calls,
    JsonNode? RawContent,
    int Input,
    int Cached,
    int Output);

/// <summary>
/// The generateContent wire format, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Pure functions over JSON for the reason the other adapter's are: this is the
/// part that fails at run time with a 400, so it is the part worth testing
/// without a network. Everything that decides <em>what</em> to say lives in
/// <see cref="GeminiSession"/>.
/// </para>
/// <para>
/// Almost nothing here is shaped like chat completions, which is why it is a
/// second adapter rather than another base url. Turns are <c>contents</c> of
/// <c>parts</c>; the roles are <c>user</c> and <c>model</c>; the briefing is a
/// <c>systemInstruction</c> beside the conversation rather than the first turn
/// of it; the model is named in the path rather than the body; a tool call is a
/// part rather than a field on the message; and a tool result is a part of a
/// user turn rather than a message of its own. About the only thing the two
/// formats agree on is that an error carries <c>error.message</c>.
/// </para>
/// </remarks>
internal static class Wire
{
    /// <param name="contents">The conversation so far, which does not include the briefing.</param>
    /// <param name="briefing">
    /// The handbook, as its own field. Not a turn — a system turn does not exist
    /// in this format, and putting it in as a user one would make the first
    /// thing the model reads look like something somebody asked for.
    /// </param>
    /// <param name="tools">
    /// What the model may call. An empty list leaves the field out altogether
    /// rather than sending an empty array — the ear is asked a question with no
    /// tools at all, and an empty declaration list beside a mode of AUTO is a
    /// contradiction worth a 400.
    /// </param>
    /// <param name="thinking">
    /// How hard to think, or null to leave it to the model. Null is absence
    /// rather than a neutral value written out: every model has its own range
    /// and a budget outside it is refused.
    /// </param>
    public static JsonObject Request(
        JsonArray contents,
        string briefing,
        IReadOnlyList<PatchTool> tools,
        JsonObject? thinking)
    {
        var request = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = briefing } },
            },
            ["contents"] = contents,
        };

        if (thinking is not null)
            request["generationConfig"] = new JsonObject { ["thinkingConfig"] = thinking };

        if (tools.Count == 0) return request;

        var declared = new JsonArray();

        foreach (var tool in tools)
        {
            var declaration = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
            };

            // Omitted rather than sent empty for a tool that takes nothing. An
            // object schema with no properties is not a function that takes no
            // arguments here — it is a schema that fails validation.
            if (Parameters(tool.Schema) is { } parameters) declaration["parameters"] = parameters;

            declared.Add(declaration);
        }

        request["tools"] = new JsonArray { new JsonObject { ["functionDeclarations"] = declared } };
        request["toolConfig"] = new JsonObject
        {
            ["functionCallingConfig"] = new JsonObject { ["mode"] = "AUTO" },
        };

        return request;
    }

    /// <summary>
    /// A tool's schema as this endpoint will take it, or null where there is
    /// nothing to declare.
    /// </summary>
    /// <remarks>
    /// The schemas are written once, in the workbench, in ordinary JSON Schema.
    /// What is accepted here is a subset of OpenAPI instead, and two differences
    /// actually bite.
    /// <para>
    /// An object with no properties is refused, so a tool that takes no
    /// arguments must declare no parameters at all rather than an empty object.
    /// <c>describe_patch</c> and <c>reset</c> are both that tool.
    /// </para>
    /// <para>
    /// Every property must say what type it is. One that deliberately does not —
    /// <c>set_extra</c>'s <c>value</c>, which takes a number, a boolean or a
    /// choice's id depending on the field it is aimed at — becomes an
    /// <c>anyOf</c> over the three, which is the same statement in the spelling
    /// this accepts. Left alone it is the kind of thing that fails as a 400
    /// naming a field nobody reading the workbench would think to blame.
    /// </para>
    /// </remarks>
    public static JsonNode? Parameters(string schema)
    {
        JsonObject body;

        try
        {
            body = JsonNode.Parse(schema) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return null;
        }

        if (body["properties"] is not JsonObject properties || properties.Count == 0) return null;

        body["type"] = "object";

        Typed(properties);

        return body;
    }

    /// <summary>
    /// Gives a type to anything that has none, all the way down.
    /// </summary>
    /// <remarks>
    /// Recursive because the schemas are authored somewhere else and a nested
    /// object arriving without a type would fail the same way. The workbench is
    /// where tools are described, and it does not know which endpoint is reading
    /// them.
    /// </remarks>
    private static void Typed(JsonObject properties)
    {
        foreach (var (_, value) in properties)
        {
            if (value is not JsonObject property) continue;

            if (property["properties"] is JsonObject nested) Typed(nested);
            if (property["items"]?["properties"] is JsonObject deeper) Typed(deeper);

            if (property["type"] is not null || property["anyOf"] is not null) continue;

            property["anyOf"] = new JsonArray
            {
                new JsonObject { ["type"] = "string" },
                new JsonObject { ["type"] = "number" },
                new JsonObject { ["type"] = "boolean" },
            };
        }
    }

    public static JsonObject User(string text) => new()
    {
        ["role"] = "user",
        ["parts"] = new JsonArray { new JsonObject { ["text"] = text } },
    };

    /// <summary>
    /// One user turn answering everything the model asked for, carrying whatever
    /// those answers produced that is not words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One turn rather than several, and its parts are deliberately of mixed
    /// kinds. Every call is answered in the order it was made, because a
    /// <c>functionResponse</c> carries a name and no id and order is all there
    /// is to tell two calls of the same tool apart. Then the pictures and the
    /// sounds, which are what those answers actually were.
    /// </para>
    /// <para>
    /// This is where the second adapter earns its place. A sound is an ordinary
    /// part here — the same slot a picture goes in, in the same turn, to the
    /// same model — so the clip reaches the model building the patch instead of
    /// a second one hired to describe it. ADR-0047 tried this exact shape
    /// against chat completions and was refused; nothing about it was wrong
    /// except the endpoint.
    /// </para>
    /// </remarks>
    /// <param name="caption">What the media is, or null when there is none.</param>
    public static JsonObject Answers(
        JsonArray answers,
        string? caption,
        IEnumerable<byte[]> pictures,
        IEnumerable<byte[]> sounds)
    {
        if (caption is not null) answers.Add(new JsonObject { ["text"] = caption });

        foreach (var picture in pictures) answers.Add(Inline("image/png", picture));

        // The file WavWriter already writes for headless export, at the
        // workbench's listening rate rather than the speakers'.
        foreach (var sound in sounds) answers.Add(Inline("audio/wav", sound));

        return new JsonObject { ["role"] = "user", ["parts"] = answers };
    }

    /// <summary>A user turn that is words and one piece of media — what the ear is sent.</summary>
    public static JsonObject UserWithMedia(string text, string type, byte[] media) => new()
    {
        ["role"] = "user",
        ["parts"] = new JsonArray { new JsonObject { ["text"] = text }, Inline(type, media) },
    };

    public static JsonObject FunctionResponse(string name, string text) => new()
    {
        ["functionResponse"] = new JsonObject
        {
            ["name"] = name,

            // An object, always: a bare string is refused. What it is put under
            // is the caller's to choose, and this is the name the examples use.
            ["response"] = new JsonObject { ["result"] = text },
        },
    };

    private static JsonObject Inline(string type, byte[] bytes) => new()
    {
        ["inlineData"] = new JsonObject
        {
            ["mimeType"] = type,
            ["data"] = Convert.ToBase64String(bytes),
        },
    };

    /// <summary>
    /// Reads one response. Anything missing is treated as absent rather than as
    /// a failure — a candidate that stopped for safety carries no parts at all,
    /// and that is an empty turn rather than a broken one.
    /// </summary>
    public static Reply Parse(JsonNode? response)
    {
        var candidates = response?["candidates"] as JsonArray;
        var content = candidates is { Count: > 0 } ? candidates[0]?["content"] : null;
        var usage = response?["usageMetadata"];

        var calls = new List<Call>();
        var words = new List<string>();

        if (content?["parts"] is JsonArray parts)
        {
            foreach (var part in parts)
            {
                // Thinking comes back as ordinary text parts flagged as thought.
                // They are not the answer, and showing them would put reasoning
                // in a transcript meant to hold what was decided and done.
                if (part?["thought"]?.GetValueKind() == JsonValueKind.True) continue;

                if (Blank(part?["text"]) is { } text) words.Add(text);

                if (part?["functionCall"] is { } asked
                    && asked["name"]?.GetValueKind() == JsonValueKind.String)
                {
                    calls.Add(new Call(asked["name"]!.GetValue<string>(), asked["args"]?.DeepClone()));
                }
            }
        }

        return new Reply(
            words.Count == 0 ? null : string.Join("\n\n", words),
            calls,
            content?.DeepClone(),
            Count(usage?["promptTokenCount"]),
            Count(usage?["cachedContentTokenCount"]),

            // Thinking is billed as output and is not in the candidate count, so
            // a turn at high effort would otherwise report a fraction of what it
            // actually spent — and PatchEvent.Cost is the only place anybody
            // sees it.
            Count(usage?["candidatesTokenCount"]) + Count(usage?["thoughtsTokenCount"]));
    }

    /// <summary>
    /// Whether a status is worth sending the same request for a second time.
    /// </summary>
    /// <remarks>
    /// Much the same set the other adapter retries, for the same reasons. 503 is
    /// the one that earns its place here rather than there: it routinely means
    /// the model is overloaded, which is a queue rather than a fault and clears
    /// in seconds.
    /// </remarks>
    public static bool Retryable(int status) =>
        status is 408 or 429 or 500 or 502 or 503 or 504;

    /// <summary>
    /// How long the endpoint asked to be left alone, or null when it did not say.
    /// </summary>
    /// <remarks>
    /// The body, not the headers, which is the difference worth writing down. A
    /// 429 here carries a <c>RetryInfo</c> among <c>error.details</c> holding a
    /// duration like <c>24s</c>, and there is usually no <c>Retry-After</c>
    /// beside it. The header is still read first where one turns up, because a
    /// gateway in front of this may add one and the nearer answer wins.
    /// </remarks>
    public static TimeSpan? RetryAfter(HttpResponseMessage response, string body)
    {
        if (response.Headers.RetryAfter is { } asked)
        {
            if (asked.Delta is { } delta) return delta;
            if (asked.Date is { } when) return when - DateTimeOffset.UtcNow;
        }

        try
        {
            if (JsonNode.Parse(body)?["error"]?["details"] is not JsonArray details) return null;

            foreach (var detail in details)
            {
                if (Blank(detail?["retryDelay"]) is { } delay && Seconds(delay) is { } wait) return wait;
            }
        }
        catch (JsonException)
        {
            // Not JSON, so it said nothing about waiting.
        }

        return null;
    }

    /// <summary>
    /// A protobuf duration, which is a number of seconds with an <c>s</c> after
    /// it — <c>24s</c>, <c>0.5s</c>.
    /// </summary>
    private static TimeSpan? Seconds(string text)
    {
        var digits = text.AsSpan().TrimEnd('s');

        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? TimeSpan.FromSeconds(value)
            : null;
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
            // Not JSON. A gateway answering in HTML is not worth showing.
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
