using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.Gemini;

/// <summary>
/// One conversation over generateContent.
/// </summary>
/// <remarks>
/// The loop is written out rather than taken from a helper, because it has to sit
/// between the model asking for something and the workbench doing it: each edit
/// reaches the window as it happens, and a proposal is noticed the moment it is
/// made. Not streamed, since a turn is short and progress reaches the panel as
/// edits rather than words.
/// <para>
/// Effort is sent, unlike the other adapter: that one cannot know what endpoint it
/// is pointed at, where this one is pointed at a fixed place and knows what each
/// model's budget may be — see <see cref="GeminiAssistant"/>.
/// </para>
/// </remarks>
internal sealed class GeminiSession : IPatchSession
{
    /// <summary>
    /// How many times the model may be asked in one turn. The workbench caps
    /// tool calls too; this bounds the exchange around them.
    /// </summary>
    private const int MaxModelTurns = 40;

    /// <summary>
    /// What the person is told when a turn changed the patch and ended without
    /// offering it.
    /// </summary>
    /// <remarks>
    /// Nothing reaches the editor until <c>propose</c>, so such a turn leaves the
    /// person looking at the patch they started with while being told it was
    /// improved. Said to them and not back to the model, which has already given
    /// its answer.
    /// </remarks>
    private const string Unoffered =
        "This turn changed the patch but did not offer it, so the canvas still shows what was "
        + "there before. Ask for it to be applied if you want to see it.";

    /// <summary>How many times one request may be sent before the turn is given up on.</summary>
    private const int MaxAttempts = 5;

    /// <summary>
    /// The longest this will sit waiting for a refusal to clear. A limit that
    /// resets inside this is a hiccup worth absorbing; one that resets beyond it
    /// is a quota, and no amount of waiting is the answer to a quota.
    /// </summary>
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(20);

    private readonly PatchWorkbench workbench;
    private readonly AssistantChoices chosen;
    private readonly HttpClient http;
    private readonly string address;
    private readonly JsonObject? thinking;
    private readonly bool ownEars;
    private readonly JsonArray contents = [];

    /// <param name="workbench">The patch being built, and the only thing here that may touch it.</param>
    /// <param name="chosen">Model, endpoint and ear, as the provider read them off the form.</param>
    /// <param name="apiKey">The key, which the host holds and this never writes down.</param>
    /// <param name="fallbackBaseUrl">Where to send requests when the configuration names nowhere.</param>
    /// <param name="thinking">The effort setting as this endpoint spells it, or null to say nothing.</param>
    /// <param name="ownEars">
    /// Whether the model doing the building takes a sound, which decides where a
    /// clip goes. Not inferred from <see cref="AssistantChoices.EarModel"/> being
    /// null, because null there also means nobody was chosen — and playing a clip
    /// to a model that refuses one loses every turn from the first <c>listen</c>.
    /// </param>
    /// <param name="transport">
    /// Where the requests actually go, defaulting to the network. Named only so the
    /// loop can be driven by canned replies.
    /// </param>
    public GeminiSession(
        PatchWorkbench workbench,
        AssistantChoices chosen,
        string apiKey,
        string fallbackBaseUrl,
        JsonObject? thinking = null,
        bool ownEars = false,
        HttpMessageHandler? transport = null)
    {
        this.workbench = workbench;
        this.chosen = chosen;
        this.thinking = thinking;
        this.ownEars = ownEars;

        address = (chosen.BaseUrl ?? fallbackBaseUrl).TrimEnd('/');

        // A handler that was handed in belongs to whoever handed it in.
        http = transport is null ? new HttpClient() : new HttpClient(transport, disposeHandler: false);

        // A single turn at high effort is minutes, not seconds. Cancellation is
        // what actually stops this; the timeout is only a backstop for a
        // connection that has died without saying so.
        http.Timeout = TimeSpan.FromMinutes(10);

        // A header rather than the key= parameter the quickstarts use. A secret
        // in a query string is a secret in every log and proxy between here and
        // there, and this endpoint accepts both.
        http.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
    }

    public async IAsyncEnumerable<PatchEvent> Ask(
        string instruction,
        [EnumeratorCancellation] CancellationToken cancel)
    {
        // Whatever was offered last time is not this turn's answer. The patch
        // itself stays: a conversation carries on from what it built.
        workbench.Reopen();

        contents.Add(Wire.User(instruction));

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

            contents.Add(reply.RawContent ?? new JsonObject
            {
                ["role"] = "model",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = reply.Text ?? string.Empty } },
            });

            if (reply.Calls.Count > 0)
            {
                var answers = new JsonArray();
                var seen = new List<byte[]>();
                var played = new List<byte[]>();

                foreach (var call in reply.Calls)
                {
                    var outcome = await Answer(call, cancel).ConfigureAwait(false);
                    var said = outcome.Text;

                    // Only where the clip cannot reach this model. Where it can,
                    // the sound goes into the turn below and a description
                    // written by somebody else would be a second opinion nobody
                    // asked for, paid for by a second request.
                    if (outcome.Wav is { } borrowed && !ownEars)
                        said += "\n\n" + await Described(borrowed, cancel).ConfigureAwait(false);

                    // Every call gets exactly one answer, refusals included, in
                    // the order the calls arrived — which is the only thing
                    // tying an answer to its call in this format.
                    answers.Add(Wire.FunctionResponse(call.Name, said));

                    if (outcome.Png is { } png)
                    {
                        seen.Add(png);
                        yield return new PatchEvent.Saw(png, said);
                    }
                    else if (outcome.Wav is { } wav)
                    {
                        if (ownEars) played.Add(wav);

                        yield return new PatchEvent.Heard(wav, said);
                    }
                    else
                    {
                        yield return new PatchEvent.Did(said);
                    }
                }

                contents.Add(Wire.Answers(answers, Caption(seen.Count, played.Count), seen, played));
            }

            // Asked for last, so a proposal is noticed whether it arrived among
            // this turn's calls or the model simply stopped after making one.
            if (workbench.HasProposal)
            {
                yield return new PatchEvent.Proposed(workbench.Snapshot(), workbench.ProposalSummary);
                yield break;
            }

            if (reply.Calls.Count == 0)
            {
                // It has stopped asking for things and has not proposed anything,
                // which is an ordinary way for a turn to end: whatever it said is
                // in the transcript, and the next thing to happen is the person
                // typing. A turn that changed the patch and did not offer it is an
                // ending nobody can see, which is the case worth saying something
                // about.
                if (workbench.Edits > 0) yield return new PatchEvent.Did(Unoffered);

                yield break;
            }
        }

        // Only a model that kept asking for things until the fuse ran out gets
        // here. One that stopped of its own accord has already returned above.
        yield return new PatchEvent.Failed(
            $"stopped after {MaxModelTurns} exchanges in one turn, which is as many as there are.");
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
    /// What the ear is told before it is played anything. Deliberately about
    /// listening rather than about synthesis: it is being asked what a sound
    /// <em>is</em>, and a model told what the patch was meant to do will hear
    /// what it was told rather than what came out.
    /// </summary>
    private const string Ear = """
        You are listening on behalf of somebody who cannot hear this clip. You
        are not told what it is or what it was meant to be, and you should not
        try to work it out — describe only what is there.

        Answer in three or four sentences, covering: how many separate things
        you can hear and roughly what pitch each sits at; whether the clip is
        continuous or has separate hits in it, and if it has hits, how often;
        whether anything changes over the clip or it stays as it starts; and
        anything wrong with it — clicks, a tearing buzz, distortion, a tail
        that cuts off.

        Most of what you will be sent is plain and unmusical, and saying so is
        the useful answer: "two steady tones, one low and one high, and nothing
        else" is worth far more than a generous reading. Do not name instruments
        unless what you hear genuinely sounds like one. Do not guess at how it
        was made, and do not offer advice.
        """;

    /// <summary>
    /// What one model heard, as words for a model that cannot.
    /// </summary>
    /// <remarks>
    /// Reached only by a model nobody here has written down — every model in the
    /// schema takes a sound, so this is the path for a name typed ahead of the
    /// table, which at a fixed endpoint is almost always a newer model. A failure
    /// is a sentence in the tool result rather than the end of the turn: the sound
    /// was rendered and the levels are known.
    /// </remarks>
    private async Task<string> Described(byte[] wav, CancellationToken cancel)
    {
        var ear = chosen.EarModel;

        if (string.IsNullOrWhiteSpace(ear))
            return "No model is set to listen with, so nobody has heard this.";

        var body = Wire.Request(
            [Wire.UserWithMedia("Here is the clip.", "audio/wav", wav)],
            Ear,
            [],
            thinking: null);

        try
        {
            var heard = Wire.Parse(await Post(ear, body.ToJsonString(), cancel).ConfigureAwait(false));

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

    /// <summary>
    /// Hands one call to the workbench. The arguments arrive as JSON rather than a
    /// string of JSON, which is the one place this format is kinder than the other:
    /// there is no parse to fail. A call with no arguments is an empty object.
    /// </summary>
    private async Task<ToolOutcome> Answer(Call call, CancellationToken cancel)
    {
        var arguments = JsonSerializer.Deserialize<JsonElement>(
            call.Arguments?.ToJsonString() ?? "{}");

        return await workbench.InvokeAsync(call.Name, arguments, cancel).ConfigureAwait(false);
    }

    private async Task<Reply> Send(CancellationToken cancel)
    {
        // The conversation is copied into the request rather than handed to it:
        // a JSON node belongs to one parent, and this one has to survive being
        // sent again on the next exchange.
        var body = Wire
            .Request((JsonArray)contents.DeepClone(), workbench.Briefing, workbench.Tools, thinking)
            .ToJsonString();

        return Wire.Parse(await Post(chosen.Model, body, cancel).ConfigureAwait(false));
    }

    /// <summary>
    /// One request, retried where the endpoint asked to be. The model is in the path
    /// rather than the body, so it is an argument to this rather than a field of
    /// what it sends — which is what lets the ear be a different model over the same
    /// client.
    /// </summary>
    private async Task<JsonNode?> Post(string model, string body, CancellationToken cancel)
    {
        var endpoint = new Uri($"{address}/models/{Uri.EscapeDataString(model)}:generateContent");

        for (var attempt = 1; ; attempt++)
        {
            // Built inside the loop: an HttpContent that has been sent once
            // cannot be sent again, and this is the one thing being retried.
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(endpoint, content, cancel).ConfigureAwait(false);

            var said = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return JsonNode.Parse(said);

            var status = (int)response.StatusCode;
            var wait = Wire.RetryAfter(response, said) ?? Backoff(attempt);

            // Waiting is silent — a hiccup of under a second is not worth a line
            // in the transcript. A limit that clears further out than
            // LongestWait is a quota rather than a hiccup, and the person is
            // told rather than left watching a still panel.
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

    /// <summary>The turns so far, without the pictures and clips — see <see cref="Wire.Kept"/>.</summary>
    public string? Save() => Wire.Kept(contents).ToJsonString();

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

    public void Dispose() => http.Dispose();
}
