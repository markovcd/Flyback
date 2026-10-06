using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.OpenAi;

/// <summary>
/// One conversation over the chat-completions format.
/// </summary>
/// <remarks>
/// Only the format is here: how the messages are written down and sent. The turn
/// itself is <see cref="TurnLoop"/>'s, which the host runs. Not streamed,
/// since a turn is short and the endpoints this is meant to reach vary more in how
/// they stream than in anything else. Effort is not sent, because the parameter
/// that carries it is model-specific and a wrong guess is a 400.
/// </remarks>
internal sealed class OpenAiSession : IModelConversation
{
    private readonly PatchWorkbench workbench;
    private readonly AssistantChoices chosen;
    private readonly IAssistantTransport transport;
    private readonly Uri endpoint;
    private readonly JsonArray messages = [];

    /// <param name="fallbackBaseUrl">Where to send requests when the configuration names nowhere.</param>
    /// <param name="transport">What to send over, which adds the key (<see cref="IAssistantTransport"/>).</param>
    /// <param name="workbench">The patch being built, and the only thing here that may touch it.</param>
    /// <param name="chosen">Model, endpoint and ear, as the provider read them off the form.</param>
    public OpenAiSession(
        PatchWorkbench workbench,
        AssistantChoices chosen,
        string fallbackBaseUrl,
        IAssistantTransport transport)
    {
        this.workbench = workbench;
        this.chosen = chosen;

        var address = (chosen.BaseUrl ?? fallbackBaseUrl).TrimEnd('/');
        endpoint = new Uri(address + "/chat/completions");

        this.transport = transport;

        messages.Add(Wire.System(workbench.Briefing));
    }

    /// <summary>The messages so far, without the briefing or the pictures — see <see cref="Wire.Kept"/>.</summary>
    public string Save() => Wire.Kept(messages).ToJsonString();

    /// <summary>
    /// Takes up the messages <see cref="Save"/> wrote, after this run's own
    /// briefing and before anything has been asked here. False for anything that
    /// is not a list of messages, which leaves this session as empty as it was.
    /// </summary>
    internal bool Take(string saved)
    {
        JsonArray kept;

        try
        {
            if (JsonNode.Parse(saved) is not JsonArray parsed) return false;

            kept = parsed;
        }
        catch (JsonException)
        {
            return false;
        }

        // Every message says whose it is, and none of them is a second briefing.
        // Either would be a 400 on the first request, which is a worse way to
        // find out than not carrying it on.
        if (kept.Any(message => Wire.Role(message) is null or "system")) return false;

        foreach (var message in kept) messages.Add(message!.DeepClone());

        return true;
    }

    PatchWorkbench IModelConversation.Workbench => workbench;

    string? IModelConversation.EarModel => chosen.EarModel;

    /// <summary>
    /// Never: the models this format reaches that take a sound require every request
    /// to carry one and take no picture, so they listen apart (ADR-0047).
    /// </summary>
    bool IModelConversation.HearsItself => false;

    void IModelConversation.Forget() => Wire.Forget(messages);

    void IModelConversation.Add(string instruction) => messages.Add(Wire.User(instruction));

    async Task<ModelReply> IModelConversation.Send(CancellationToken cancel)
    {
        // The conversation is copied into the request rather than handed to it:
        // a JSON node belongs to one parent, and this one has to survive being
        // sent again on the next exchange.
        var body = Wire.Request(chosen.Model, (JsonArray)messages.DeepClone(), workbench.Tools)
            .ToJsonString();

        var reply = Wire.Parse(await Post(body, cancel).ConfigureAwait(false));

        messages.Add(reply.RawMessage ?? new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = reply.Text ?? string.Empty,
        });

        return new ModelReply(
            reply.Text,
            [.. reply.Calls.Select(call => new ToolCall(call.Id, call.Name, call.Arguments))],
            reply.Input,
            reply.Cached,
            reply.Output)
        {
            Model = reply.Model,
        };
    }

    /// <summary>
    /// What rides over the pictures. A user message reads as the person speaking
    /// unless it says otherwise, and a model that thinks it was shown them waits for
    /// them to say more instead of proposing.
    /// </summary>
    internal const string Shown = "[From Flyback, not the person: the frames you rendered.]";

    /// <summary>
    /// A tool message for each answer, then the pictures as a user message: a tool
    /// message in this format carries a string and nothing else.
    /// </summary>
    void IModelConversation.Add(IReadOnlyList<ToolAnswer> answers)
    {
        foreach (var answer in answers) messages.Add(Wire.ToolResult(answer.Call.Id, answer.Text));

        List<byte[]> seen = [.. answers.Select(answer => answer.Png).OfType<byte[]>()];

        if (seen.Count > 0) messages.Add(Wire.UserWithPictures(Shown, seen));
    }

    /// <summary>
    /// A separate request rather than a turn of the conversation: the models that take
    /// a sound require every request to carry one and do not take a picture.
    /// </summary>
    async Task<string?> IModelConversation.Listen(string model, string briefing, byte[] wav, CancellationToken cancel)
    {
        var body = Wire.Request(
            model,
            [Wire.System(briefing), Wire.UserWithMedia("Here is the clip.", [], [wav])],
            []);

        return Wire.Parse(await Post(body.ToJsonString(), cancel).ConfigureAwait(false)).Text;
    }

    private async Task<JsonNode?> Post(string body, CancellationToken cancel) =>
        JsonNode.Parse(await AssistantPost
            .Send(transport, endpoint, body, Wire.Wait, Wire.Complaint, cancel)
            .ConfigureAwait(false));

    public void Dispose()
    {
    }
}
