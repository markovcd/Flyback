using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flyback.Plugins.Decide;

/// <summary>
/// The <c>POST /v1/systemone</c> format Jev and Laya both speak: a request of a state and
/// its questions, and an answer of one probability-bearing answer per question.
/// </summary>
/// <remarks>The wire calls a yes-no question <c>noul</c>. Readers never throw; they say what was wrong.</remarks>
public static class SystemOneWire
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string YesNoType = "noul", ChoiceType = "choice", ScoreType = "score";

    /// <summary>The request body for <paramref name="request"/>, naming <paramref name="model"/> where there is one.</summary>
    public static string Request(DecisionRequest request, string? model)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new JsonObject { ["state"] = request.State };

        if (!string.IsNullOrWhiteSpace(model)) body["model"] = model;

        body["questions"] = Questions(request.Questions);

        return body.ToJsonString(Options);
    }

    /// <summary>The questions object of a request, as a request spells it.</summary>
    private static JsonObject Questions(IReadOnlyDictionary<string, Question> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);

        var asked = new JsonObject();

        foreach (var (id, question) in questions)
        {
            var spelled = new JsonObject { ["type"] = TypeOf(question), ["instructions"] = question.Instructions };

            switch (question)
            {
                case Question.Choice choice:
                    var criteria = new JsonObject();
                    foreach (var option in choice.Options) criteria[option.Label] = option.Description;
                    spelled["criteria"] = criteria;
                    break;
                case Question.Score score:
                    spelled["criteria"] = new JsonArray([.. score.Levels.Select(l => (JsonNode?)l)]);
                    break;
            }

            asked[id] = spelled;
        }

        return asked;
    }

    /// <summary>The answer body for <paramref name="decision"/>, as an endpoint sends it.</summary>
    internal static string Answer(Decision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var answers = new JsonObject();

        foreach (var (id, answer) in decision.Answers)
        {
            answers[id] = answer switch
            {
                Decide.Answer.YesNo yes => new JsonObject { ["type"] = YesNoType, [YesNoType] = yes.Probability },
                Decide.Answer.Chosen chosen => new JsonObject
                {
                    ["type"] = ChoiceType,
                    [ChoiceType] = chosen.Option,
                    ["probabilities"] = Map(chosen.Probabilities),
                    ["confidence"] = chosen.Confidence,
                },
                Decide.Answer.Scored scored => new JsonObject
                {
                    ["type"] = ScoreType,
                    [ScoreType] = scored.Score,
                    ["legend"] = Indexed(scored.Levels.Select(l => (JsonNode?)l)),
                    ["probabilities"] = Indexed(scored.Probabilities.Select(p => (JsonNode?)p)),
                    ["confidence"] = scored.Confidence,
                },
                _ => throw new ArgumentException($"Answer '{id}' is of no kind the wire has.", nameof(decision)),
            };
        }

        return new JsonObject
        {
            ["model"] = decision.Model,
            ["answers"] = answers,
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = decision.Usage.InputTokens,
                ["output_tokens"] = decision.Usage.OutputTokens,
            },
        }.ToJsonString(Options);
    }

    /// <summary>
    /// The questions in <paramref name="json"/>: a request's <c>questions</c> object, or that
    /// object alone. Null, with <paramref name="problem"/> saying why, where it is not one.
    /// </summary>
    internal static IReadOnlyDictionary<string, Question>? ReadQuestions(string json, out string? problem)
    {
        if (Parse(json, out problem) is not { } root) return null;

        var asked = root["questions"] is JsonObject inner ? inner : root;
        var questions = new Dictionary<string, Question>(StringComparer.Ordinal);

        foreach (var (id, node) in asked)
        {
            if (node is not JsonObject spelled)
                return Fail<IReadOnlyDictionary<string, Question>>(out problem, $"Question '{id}' is not an object.");

            var instructions = Text(spelled["instructions"]);

            if (instructions is null) return Fail<IReadOnlyDictionary<string, Question>>(out problem, $"Question '{id}' has no instructions.");

            Question? question = Text(spelled["type"]) switch
            {
                YesNoType => new Question.YesNo(instructions),
                ChoiceType when spelled["criteria"] is JsonObject criteria && criteria.All(c => Text(c.Value) is not null) =>
                    new Question.Choice(instructions, [.. criteria.Select(c => new ChoiceOption(c.Key, Text(c.Value)!))]),
                ScoreType when spelled["criteria"] is JsonArray levels && levels.All(l => Text(l) is not null) =>
                    new Question.Score(instructions, [.. levels.Select(l => Text(l)!)]),
                _ => null,
            };

            if (question is null)
                return Fail<IReadOnlyDictionary<string, Question>>(out problem, $"Question '{id}' is not a noul, a choice with a criteria object, or a score with a criteria list.");

            questions[id] = question;
        }

        problem = null;
        return questions;
    }

    /// <summary>The decision in an endpoint's answer, or null, with <paramref name="problem"/> saying why, where it is not one.</summary>
    public static Decision? ReadDecision(string json, out string? problem)
    {
        if (Parse(json, out problem) is not { } root) return null;

        if (root["answers"] is not JsonObject answers) return Fail<Decision>(out problem, "The answer has no answers object.");

        var read = new Dictionary<string, Answer>(StringComparer.Ordinal);

        foreach (var (id, node) in answers)
        {
            Answer? answer = node is not JsonObject spelled ? null : Text(spelled["type"]) switch
            {
                YesNoType => Number(spelled[YesNoType]) is { } p ? new Decide.Answer.YesNo(p) : null,
                ChoiceType => ReadChosen(spelled),
                ScoreType => ReadScored(spelled),
                _ => null,
            };

            if (answer is null) return Fail<Decision>(out problem, $"Answer '{id}' is not one the wire has.");

            read[id] = answer;
        }

        var usage = root["usage"] as JsonObject;

        problem = null;
        return new Decision(
            Text(root["model"]) ?? "",
            read,
            new DecisionUsage(Count(usage?["input_tokens"]), Count(usage?["output_tokens"])));
    }

    private static Answer.Chosen? ReadChosen(JsonObject spelled)
    {
        if (Text(spelled[ChoiceType]) is not { } option) return null;

        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);

        if (spelled["probabilities"] is JsonObject given)
            foreach (var (label, p) in given)
                if (Number(p) is { } value) probabilities[label] = value;

        return new Answer.Chosen(option, probabilities, Number(spelled["confidence"]) ?? 0);
    }

    private static Answer.Scored? ReadScored(JsonObject spelled)
    {
        if (Number(spelled[ScoreType]) is not { } score) return null;

        var legend = OrderedText(spelled["legend"] as JsonObject);
        var probabilities = OrderedNumbers(spelled["probabilities"] as JsonObject);

        return new Answer.Scored(score, legend, probabilities, Number(spelled["confidence"]) ?? 0);
    }

    /// <summary>The values of an object keyed <c>"0"</c>, <c>"1"</c> and on, in that order, stopping at the first gap.</summary>
    private static List<string> OrderedText(JsonObject? indexed)
    {
        var values = new List<string>();

        for (var i = 0; indexed is not null && Text(indexed[Key(i)]) is { } value; i++) values.Add(value);

        return values;
    }

    /// <inheritdoc cref="OrderedText"/>
    private static List<double> OrderedNumbers(JsonObject? indexed)
    {
        var values = new List<double>();

        for (var i = 0; indexed is not null && Number(indexed[Key(i)]) is { } value; i++) values.Add(value);

        return values;
    }

    private static string Key(int index) => index.ToString(CultureInfo.InvariantCulture);

    private static JsonObject Map(IReadOnlyDictionary<string, double> values)
    {
        var map = new JsonObject();
        foreach (var (key, value) in values) map[key] = value;
        return map;
    }

    private static JsonObject Indexed(IEnumerable<JsonNode?> values)
    {
        var map = new JsonObject();
        var i = 0;
        foreach (var value in values) map[Key(i++)] = value;
        return map;
    }

    private static string TypeOf(Question question) => question switch
    {
        Question.YesNo => YesNoType,
        Question.Choice => ChoiceType,
        Question.Score => ScoreType,
        _ => throw new ArgumentException("A question of no kind the wire has.", nameof(question)),
    };

    private static JsonObject? Parse(string json, out string? problem)
    {
        try
        {
            if (JsonNode.Parse(json) is JsonObject root)
            {
                problem = null;
                return root;
            }

            problem = "It is not a JSON object.";
        }
        catch (Exception ex) when (ex is JsonException or ArgumentNullException)
        {
            problem = $"It is not JSON: {ex.Message}";
        }

        return null;
    }

    private static T? Fail<T>(out string? problem, string why) where T : class
    {
        problem = why;
        return null;
    }

    private static string? Text(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    private static double? Number(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<double>(out var value) && double.IsFinite(value) ? value : null;

    private static int Count(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.Number && node.AsValue().TryGetValue<int>(out var value) && value >= 0 ? value : 0;
}
