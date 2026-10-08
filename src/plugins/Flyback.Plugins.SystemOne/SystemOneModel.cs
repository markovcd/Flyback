using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.SystemOne;

/// <summary>
/// A decision model behind <c>POST /v1/systemone</c> at whatever server somebody gives it,
/// a laya-serve on this machine unless they say otherwise.
/// </summary>
public sealed class SystemOneModel : IDecisionModel
{
    /// <summary>Where laya-serve listens when started with its defaults.</summary>
    public const string LocalEndpoint = "http://localhost:8000";

    public const string EndpointKey = "endpoint", ModelKey = "model";

    public string Id => "systemone";

    public string Name => "Decision server";

    /// <summary>Below a model that runs here, which answers without sending anything anywhere.</summary>
    public int Priority => 10;

    public AssistantCredential? Credential { get; } = new(
        "DECISION_SERVER_KEY",
        "Only for a server that asks for one; a laya-serve needs none.");

    public IReadOnlyList<SettingField> Form(SettingValues values) =>
    [
        new SettingField.Text(EndpointKey, "Endpoint", LocalEndpoint, LocalEndpoint) { Note = "The address of a server that speaks POST /v1/systemone; a laya-serve listens here unless told otherwise." },
        new SettingField.Text(ModelKey, "Model", Placeholder: "the endpoint's own") { Note = "Left empty, the endpoint chooses; a laya-serve by the text's language." },
    ];

    public Uri? Endpoint(SettingValues values) =>
        Uri.TryCreate(Base(values), UriKind.Absolute, out var root) && root.Scheme is "http" or "https"
            ? new Uri(root.AbsoluteUri.TrimEnd('/') + "/v1/systemone")
            : null;

    public string? Unavailable(DecisionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return Endpoint(config.Values) is null ? $"'{Base(config.Values)}' is not an http or https address." : null;
    }

    public async Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(config);

        if (request.Problem() is { } problem) throw new ArgumentException(problem, nameof(request));

        var endpoint = Endpoint(config.Values)
            ?? throw new InvalidOperationException($"'{Base(config.Values)}' is not an http or https address.");

        var body = SystemOneWire.Request(request, config.Values.Text(ModelKey));

        var answer = await AssistantPost.Send(
            config.Transport,
            endpoint,
            body,
            response => response.Headers.RetryAfter?.Delta,
            Complaint,
            cancel).ConfigureAwait(false);

        return SystemOneWire.ReadDecision(answer, out var unreadable)
            ?? throw new InvalidOperationException($"{endpoint.Host} answered with something that is not a decision. {unreadable}");
    }

    private static string Base(SettingValues values) => values.Text(EndpointKey, LocalEndpoint).Trim();

    /// <summary>A refusal as a sentence: what the status means here, then whatever the endpoint said.</summary>
    internal static string Complaint(int status, string body)
    {
        var meaning = status switch
        {
            401 => "The key was refused.",
            422 => "The endpoint could not read the questions.",
            429 => "Too many questions too quickly, or the account is out of credit.",
            529 => "The endpoint is overloaded.",
            _ => $"The endpoint answered {status}.",
        };

        return Said(body) is { } said ? $"{meaning} It said: {said}" : meaning;
    }

    /// <summary>The message in a refusal's body, or null where there is none worth showing.</summary>
    private static string? Said(string body)
    {
        try
        {
            var root = JsonNode.Parse(body);
            var said = root?["detail"] ?? root?["error"]?["message"] ?? root?["error"] ?? root?["message"];

            if (said?.GetValueKind() == JsonValueKind.String) return said.GetValue<string>();
            if (said is not null) return said.ToJsonString();
        }
        catch (JsonException)
        {
            // Not JSON; a gateway's HTML is not worth showing.
        }

        return null;
    }
}
