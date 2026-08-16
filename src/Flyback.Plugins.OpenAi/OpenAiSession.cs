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
    /// tool calls too; this catches the other runaway, where it talks without
    /// ever doing anything.
    /// </summary>
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

    /// <summary>
    /// What to say to a model that has stopped talking without offering a patch.
    /// Worth spending a turn on: from the panel's side, stopping short looks
    /// exactly like working — prose in the transcript, edits listed above it —
    /// and the only sign is an Apply button that never lights up.
    /// </summary>
    private const string Unfinished =
        "You stopped without calling 'propose', so none of that reaches the person's editor "
        + "and there is nothing for them to apply. Call 'propose' now with a one-line summary, "
        + "or say plainly what is stopping you from proposing.";

    private readonly PatchWorkbench workbench;
    private readonly AssistantConfig config;
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private readonly JsonArray messages = [];

    /// <param name="transport">
    /// Where the requests actually go, defaulting to the network. Named only so
    /// the loop can be driven by canned replies: how a turn ends is this class's
    /// whole job, and it should not take an endpoint to find out that it ends
    /// wrongly.
    /// </param>
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
        messages.Add(Wire.User(instruction));

        var nudged = false;

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

                    // Every call gets exactly one reply, refusals included. One
                    // left unanswered makes the whole next request a 400, which
                    // would end the conversation rather than the call.
                    messages.Add(Wire.ToolResult(call.Id, outcome.Text));

                    if (outcome.Png is { } png)
                    {
                        seen.Add(png);
                        yield return new PatchEvent.Saw(png, outcome.Text);
                    }
                    else
                    {
                        yield return new PatchEvent.Did(outcome.Text);
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

            if (reply.Calls.Count > 0) continue;

            // It has stopped asking for things without having offered anything.
            // Ending here is what leaves the panel with an Apply button that
            // will not light up and no word of why, so it gets told once.
            if (nudged)
            {
                yield return new PatchEvent.Failed(
                    "stopped without proposing a patch, so there is nothing to apply.");
                yield break;
            }

            nudged = true;
            messages.Add(Wire.User(Unfinished));
        }

        yield return new PatchEvent.Failed(
            $"stopped after {MaxModelTurns} exchanges without a patch being proposed.");
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

        for (var attempt = 1; ; attempt++)
        {
            // Built inside the loop: an HttpContent that has been sent once
            // cannot be sent again, and this is the one thing being retried.
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(endpoint, content, cancel).ConfigureAwait(false);

            var said = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return Wire.Parse(JsonNode.Parse(said));

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
