using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.OpenAi;

/// <summary>
/// One conversation over the chat-completions format.
/// </summary>
/// <remarks>
/// <para>
/// The loop is written out rather than taken from a helper. A tool runner would
/// invert control, and this needs to sit between the model asking for something
/// and the workbench doing it — to hand each edit to the window as it happens,
/// and to notice the moment a patch has been proposed.
/// </para>
/// <para>
/// Not streamed. A turn here is short — the model asks for a tool, the workbench
/// answers — so progress reaches the panel as edits rather than as words, and
/// the endpoints this is meant to reach vary more in how they stream than in
/// anything else they do.
/// </para>
/// <para>
/// Effort is not sent. The parameter that carries it is model-specific and a
/// wrong guess is a 400 from the endpoint rather than a shrug, which is a poor
/// trade for a setting the person can express by choosing a different model.
/// </para>
/// </remarks>
internal sealed class OpenAiSession : IPatchSession
{
    /// <summary>
    /// How many times the model may be asked in one turn. The workbench caps
    /// tool calls too; this bounds the exchange around them, including the
    /// requests that carry a picture back and cost nothing in tool calls.
    /// </summary>
    /// <remarks>
    /// Reached only by a model that never stops asking for things. One that
    /// stops — with a patch, with a question, or with nothing at all — ends its
    /// own turn, and none of those is this program's business to argue with.
    /// </remarks>
    private const int MaxModelTurns = 40;

    /// <summary>
    /// How many times one request may be sent before the turn is given up on.
    /// </summary>
    private const int MaxAttempts = 5;

    /// <summary>
    /// The longest this will sit waiting for a refusal to clear. A rate limit
    /// that resets inside this is a hiccup worth absorbing; one that resets
    /// beyond it is a quota, and no amount of waiting is the answer to a quota.
    /// </summary>
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(20);

    private readonly PatchWorkbench workbench;
    private readonly AssistantConfig config;
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly JsonArray messages = [];

    /// <param name="fallbackBaseUrl">Where to send requests when the configuration names nowhere.</param>
    /// <param name="transport">
    /// Where the requests actually go, defaulting to the network. Named only so
    /// the loop can be driven by canned replies: how a turn ends is this class's
    /// whole job, and it should not take an endpoint to find out that it ends
    /// wrongly.
    /// </param>
    /// <param name="workbench">The patch being built, and the only thing here that may touch it.</param>
    /// <param name="config">Model, key and effort, as the panel has them.</param>
    public OpenAiSession(
        PatchWorkbench workbench,
        AssistantConfig config,
        string fallbackBaseUrl,
        HttpMessageHandler? transport = null)
    {
        this.workbench = workbench;
        this.config = config;

        var address = (config.BaseUrl ?? fallbackBaseUrl).TrimEnd('/');
        endpoint = new Uri(address + "/chat/completions");

        // A handler that was handed in belongs to whoever handed it in.
        http = transport is null ? new HttpClient() : new HttpClient(transport, disposeHandler: false);

        // A single turn at high effort is minutes, not seconds. Cancellation is
        // what actually stops this; the timeout is only a backstop for a
        // connection that has died without saying so.
        http.Timeout = TimeSpan.FromMinutes(10);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        messages.Add(Wire.System(workbench.Briefing));
    }

    public async IAsyncEnumerable<PatchEvent> Ask(
        string instruction,
        [EnumeratorCancellation] CancellationToken cancel)
    {
        // Whatever was offered last time is not this turn's answer. The patch
        // itself stays: a conversation carries on from what it built.
        workbench.Reopen();

        messages.Add(Wire.User(instruction));

        for (var turn = 0; turn < MaxModelTurns; turn++)
        {
            if (cancel.IsCancellationRequested) yield break;

            // The send is guarded and the yielding happens after it, because a
            // yield may not sit inside a catch.
            Reply? reply = null;
            string? failure = null;

            try
            {
                reply = await Send(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }

            if (reply is null)
            {
                yield return new PatchEvent.Failed(failure ?? "the endpoint said nothing at all.");
                yield break;
            }

            if (reply.Text is { } text) yield return new PatchEvent.Said(text);
            if (reply.Input > 0 || reply.Output > 0)
                yield return new PatchEvent.Cost(reply.Input, reply.Cached, reply.Output);

            messages.Add(reply.RawMessage ?? new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = reply.Text ?? string.Empty,
            });

            if (reply.Calls.Count > 0)
            {
                var seen = new List<byte[]>();

                foreach (var call in reply.Calls)
                {
                    var outcome = await Answer(call, cancel).ConfigureAwait(false);
                    var said = outcome.Text;

                    // A sound is described before it is answered for, because
                    // what goes back to this model is the description: the ear
                    // is a different model and this one may well not have one.
                    if (outcome.Wav is { } wav)
                        said += "\n\n" + await Described(wav, Note(call), cancel).ConfigureAwait(false);

                    // Every call gets exactly one reply, refusals included. One
                    // left unanswered makes the whole next request a 400, which
                    // would end the conversation rather than the call.
                    messages.Add(Wire.ToolResult(call.Id, said));

                    if (outcome.Png is { } png)
                    {
                        seen.Add(png);
                        yield return new PatchEvent.Saw(png, said);
                    }
                    else if (outcome.Wav is { } sound)
                    {
                        yield return new PatchEvent.Heard(sound, said);
                    }
                    else
                    {
                        yield return new PatchEvent.Did(said);
                    }
                }

                if (seen.Count > 0)
                    messages.Add(Wire.UserWithPictures("Here is what that looked like.", seen));
            }

            // Asked for last, so a proposal is noticed whether it arrived among
            // this turn's calls or the model simply stopped after making one.
            if (workbench.HasProposal)
            {
                yield return new PatchEvent.Proposed(workbench.Snapshot(), workbench.ProposalSummary);
                yield break;
            }

            // It has stopped asking for things and has not proposed anything,
            // which is an ordinary way for a turn to end rather than a failure.
            // A question needs an answer, and a model that has said it needs one
            // more instruction is not stuck — the conversation is multi-turn and
            // whatever it said is already in the transcript, so the next thing
            // to happen is the person typing. Anything said here instead would
            // be this program arguing with an answer it was given.
            if (reply.Calls.Count == 0) yield break;
        }

        // Only a model that kept asking for things until the fuse ran out gets
        // here. One that stopped of its own accord has already returned above,
        // and this says nothing about whether a patch was proposed.
        yield return new PatchEvent.Failed(
            $"stopped after {MaxModelTurns} exchanges in one turn, which is as many as there are.");
    }

    /// <summary>
    /// What the ear is told before it is played anything. Deliberately about
    /// listening rather than about synthesis: it is being asked what a sound
    /// <em>is</em>, and a model told what the patch was meant to do will hear
    /// what it was told rather than what came out.
    /// </summary>
    private const string Ear = """
        You are listening on behalf of somebody building a sound on a modular
        synthesiser, who cannot hear it. Describe what you actually hear, in
        three or four sentences: pitch and whether it is steady, timbre, how it
        moves over the clip, and anything wrong with it — clicks, a tearing
        buzz, distortion, a tail that cuts off. Say plainly if it is a plain
        tone and nothing more. Do not guess at how it was made, do not
        speculate about what it was for, and do not offer advice.
        """;

    /// <summary>
    /// What one model heard, as words for the model that cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate request rather than a turn of the conversation, and that is
    /// forced rather than chosen. The models that take a sound require every
    /// request to carry one — a conversation driven by one 400s on its first
    /// turn, before anything has been rendered to listen to — and they do not
    /// take a picture, so driving with one would trade the patch's eyes for its
    /// ears. Asked on its own, the ear answers one question about one sound and
    /// the loop is driven by whatever model is best at driving it.
    /// </para>
    /// <para>
    /// The WAV is sent once and is never part of the conversation, which is the
    /// other thing this buys: the alternative left a few hundred kilobytes of
    /// base64 in the history to be paid for again on every turn that followed.
    /// </para>
    /// <para>
    /// A failure here is a sentence in the tool result rather than the end of
    /// the turn. The sound was rendered and the levels are already known; not
    /// being able to describe it is worth saying and worth carrying on from.
    /// </para>
    /// </remarks>
    private async Task<string> Described(byte[] wav, string? listeningFor, CancellationToken cancel)
    {
        var ear = config.EarModel;

        if (string.IsNullOrWhiteSpace(ear))
            return "No model is set to listen with, so nobody has heard this.";

        var asked = listeningFor is { Length: > 0 }
            ? $"Here is the sound. The person building it is listening for: {listeningFor}"
            : "Here is the sound.";

        var body = Wire.Request(
            ear,
            [Wire.System(Ear), Wire.UserWithMedia(asked, [], [wav])],
            []);

        try
        {
            var heard = Wire.Parse(await Post(body.ToJsonString(), cancel).ConfigureAwait(false));

            return heard.Text is { Length: > 0 } text
                ? $"{ear} listened to it and says: {text}"
                : $"{ear} was played it and said nothing.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"It could not be played to {ear}: {ex.Message} The levels above are still measured "
                + "from the sound itself, so use those and say you have not heard it.";
        }
    }

    /// <summary>What the model said it was listening for, or null when it did not say.</summary>
    private static string? Note(Call call)
    {
        try
        {
            return JsonNode.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments)
                ?["note"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<ToolOutcome> Answer(Call call, CancellationToken cancel)
    {
        JsonElement arguments;

        try
        {
            arguments = JsonSerializer.Deserialize<JsonElement>(
                string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
        }
        catch (JsonException ex)
        {
            return ToolOutcome.Refused($"those arguments were not valid JSON: {ex.Message}");
        }

        return await workbench.InvokeAsync(call.Name, arguments, cancel).ConfigureAwait(false);
    }

    private async Task<Reply> Send(CancellationToken cancel)
    {
        // The conversation is copied into the request rather than handed to it:
        // a JSON node belongs to one parent, and this one has to survive being
        // sent again on the next exchange.
        var body = Wire.Request(config.Model, (JsonArray)messages.DeepClone(), workbench.Tools)
            .ToJsonString();

        return Wire.Parse(await Post(body, cancel).ConfigureAwait(false));
    }

    /// <summary>
    /// One request, retried where the endpoint asked to be. Shared by the
    /// conversation and by the ear, so a rate limit is absorbed the same way
    /// whichever of them met it.
    /// </summary>
    private async Task<JsonNode?> Post(string body, CancellationToken cancel)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Built inside the loop: an HttpContent that has been sent once
            // cannot be sent again, and this is the one thing being retried.
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(endpoint, content, cancel).ConfigureAwait(false);

            var said = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return JsonNode.Parse(said);

            var status = (int)response.StatusCode;
            var wait = Wire.RetryAfter(response) ?? Backoff(attempt);

            // Waiting is silent — there is no event for it, and a hiccup of
            // under a second is not worth a line in the transcript. A limit that
            // clears further out than LongestWait is not a hiccup but a quota,
            // and the person is told rather than left watching a still panel.
            if (attempt >= MaxAttempts || !Wire.Retryable(status) || wait > LongestWait)
                throw new HttpRequestException(Wire.Complaint(status, said));

            await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, cancel).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// What to wait when the endpoint refused without saying how long for.
    /// Doubling from a second, which spends about fifteen across the attempts.
    /// </summary>
    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

    public void Dispose() => http.Dispose();
}
