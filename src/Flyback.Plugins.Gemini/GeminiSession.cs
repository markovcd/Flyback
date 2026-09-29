using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.Gemini;

/// <summary>
/// One conversation over generateContent.
/// </summary>
/// <remarks>
/// Only the format is here: how the turns are written down and sent. The turn itself
/// is <see cref="TurnLoop"/>'s, which the host runs. Not streamed, since a
/// turn is short and progress reaches the panel as edits rather than words.
/// <para>
/// Effort is sent, unlike the other adapter: that one cannot know what endpoint it
/// is pointed at, where this one is pointed at a fixed place and knows what each
/// model's budget may be — see <see cref="GeminiAssistant"/>.
/// </para>
/// </remarks>
internal sealed class GeminiSession : IModelConversation
{
    private readonly PatchWorkbench workbench;
    private readonly AssistantChoices chosen;
    private readonly IAssistantTransport transport;
    private readonly string address;
    private readonly JsonObject? thinking;
    private readonly bool ownEars;
    private readonly JsonArray contents = [];

    /// <param name="workbench">The patch being built, and the only thing here that may touch it.</param>
    /// <param name="chosen">Model, endpoint and ear, as the provider read them off the form.</param>
    /// <param name="fallbackBaseUrl">Where to send requests when the configuration names nowhere.</param>
    /// <param name="transport">What to send over, which adds the key (<see cref="IAssistantTransport"/>).</param>
    /// <param name="thinking">The effort setting as this endpoint spells it, or null to say nothing.</param>
    /// <param name="ownEars">
    /// Whether the model doing the building takes a sound, which decides where a
    /// clip goes. Not inferred from <see cref="AssistantChoices.EarModel"/> being
    /// null, because null there also means nobody was chosen — and playing a clip
    /// to a model that refuses one loses every turn from the first <c>listen</c>.
    /// </param>
    public GeminiSession(
        PatchWorkbench workbench,
        AssistantChoices chosen,
        string fallbackBaseUrl,
        IAssistantTransport transport,
        JsonObject? thinking = null,
        bool ownEars = false)
    {
        this.workbench = workbench;
        this.chosen = chosen;
        this.thinking = thinking;
        this.ownEars = ownEars;
        this.transport = transport;

        address = (chosen.BaseUrl ?? fallbackBaseUrl).TrimEnd('/');
    }

    PatchWorkbench IModelConversation.Workbench => workbench;

    string? IModelConversation.EarModel => chosen.EarModel;

    bool IModelConversation.HearsItself => ownEars;

    void IModelConversation.Forget() => Wire.Forget(contents);

    void IModelConversation.Add(string instruction) => contents.Add(Wire.User(instruction));

    async Task<ModelReply> IModelConversation.Send(CancellationToken cancel)
    {
        // The conversation is copied into the request rather than handed to it:
        // a JSON node belongs to one parent, and this one has to survive being
        // sent again on the next exchange.
        var body = Wire
            .Request((JsonArray)contents.DeepClone(), workbench.Briefing, workbench.Tools, thinking)
            .ToJsonString();

        var reply = Wire.Parse(await Post(chosen.Model, body, cancel).ConfigureAwait(false));

        contents.Add(reply.RawContent ?? new JsonObject
        {
            ["role"] = "model",
            ["parts"] = new JsonArray { new JsonObject { ["text"] = reply.Text ?? string.Empty } },
        });

        // No id: an answer is tied to its call by order alone in this format.
        return new ModelReply(
            reply.Text,
            [.. reply.Calls.Select(call => new ToolCall(string.Empty, call.Name, call.Arguments?.ToJsonString() ?? "{}"))],
            reply.Input,
            reply.Cached,
            reply.Output);
    }

    /// <summary>
    /// The answers as one user turn of function responses, in the order the calls
    /// arrived, with the pictures and clips after them.
    /// </summary>
    void IModelConversation.Add(IReadOnlyList<ToolAnswer> answers)
    {
        var responses = new JsonArray();

        foreach (var answer in answers) responses.Add(Wire.FunctionResponse(answer.Call.Name, answer.Text));

        List<byte[]> seen = [.. answers.Select(answer => answer.Png).OfType<byte[]>()];
        List<byte[]> played = [.. answers.Select(answer => answer.Wav).OfType<byte[]>()];

        contents.Add(Wire.Answers(responses, Caption(seen.Count, played.Count), seen, played));
    }

    /// <summary>
    /// Reached only by a model nobody here has written down — every model in the
    /// schema takes a sound, so this is the path for a name typed ahead of the table,
    /// which at a fixed endpoint is almost always a newer model.
    /// </summary>
    async Task<string?> IModelConversation.Listen(string model, string briefing, byte[] wav, CancellationToken cancel)
    {
        var body = Wire.Request(
            [Wire.UserWithMedia("Here is the clip.", "audio/wav", wav)],
            briefing,
            [],
            thinking: null);

        return Wire.Parse(await Post(model, body.ToJsonString(), cancel).ConfigureAwait(false)).Text;
    }

    /// <summary>
    /// What to say over the media riding back with the tool answers, or null when
    /// there is none. One sentence for both, because a turn that rendered and
    /// listened produced one set of observations about one patch.
    /// </summary>
    private static string? Caption(int pictures, int sounds) => (pictures, sounds) switch
    {
        (0, 0) => null,
        (> 0, 0) => "Here is what that looked like.",
        (0, > 0) => "Here is what that sounded like.",
        _ => "Here is what that looked and sounded like.",
    };

    /// <summary>
    /// One request. The model is in the path rather than the body, which is what lets
    /// the ear be a different model over the same transport.
    /// </summary>
    private async Task<JsonNode?> Post(string model, string body, CancellationToken cancel)
    {
        var endpoint = new Uri($"{address}/models/{Uri.EscapeDataString(model)}:generateContent");

        return JsonNode.Parse(await AssistantPost
            .Send(transport, endpoint, body, response => Wire.RetryAfter(response.Headers, response.Body), Wire.Complaint, cancel)
            .ConfigureAwait(false));
    }

    /// <summary>The turns so far, without the pictures and clips — see <see cref="Wire.Kept"/>.</summary>
    public string Save() => Wire.Kept(contents).ToJsonString();

    /// <summary>
    /// Takes up the turns <see cref="Save"/> wrote, before anything has been asked
    /// here. False for anything that is not a list of turns, which leaves this
    /// session as empty as it was.
    /// </summary>
    internal bool Take(string saved)
    {
        JsonArray turns;

        try
        {
            if (JsonNode.Parse(saved) is not JsonArray parsed) return false;

            turns = parsed;
        }
        catch (JsonException)
        {
            return false;
        }

        // Every turn says whose it is. One that does not is a 400 on the first
        // request, which is a worse way to find out than not carrying it on.
        if (turns.Any(turn => turn?["role"]?.GetValueKind() != JsonValueKind.String)) return false;

        foreach (var turn in turns) contents.Add(turn!.DeepClone());

        return true;
    }

    public void Dispose()
    {
    }
}
