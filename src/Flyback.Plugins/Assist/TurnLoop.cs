using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

namespace Flyback.Plugins.Assist;

/// <summary>
/// One turn of a conversation: the model is asked, the workbench does what it asked
/// for, and so on until it proposes, stops or runs out.
/// </summary>
/// <remarks>
/// Written once, and run by the host, because what a turn promises is the host's:
/// every call is answered, a proposal ends the turn, an edit never offered is said,
/// pictures and clips last a turn. Each edit reaches the window as it happens, since
/// this yields as it goes. A provider supplies only its format, through
/// <see cref="IModelConversation"/> (ADR-0161).
/// </remarks>
public static class TurnLoop
{
    /// <summary>
    /// How many times the model may be asked in one turn. The workbench caps tool
    /// calls too; this bounds the exchange around them.
    /// </summary>
    public const int MaxExchanges = 40;

    /// <summary>
    /// What the person is told when a turn changed the patch and ended without
    /// offering it: the canvas still shows the patch they started with. Said to them
    /// and not to the model, which has given its answer.
    /// </summary>
    internal const string Unoffered =
        "This turn changed the patch but did not offer it, so the canvas still shows what was "
        + "there before. Ask for it to be applied if you want to see it.";

    /// <summary>The shortest wait for a refusal worth a line in the transcript.</summary>
    internal static readonly TimeSpan ToldWait = TimeSpan.FromSeconds(1);

    /// <summary>The waits told of, with how long each was, which are lines from the host rather than anything the assistant did.</summary>
    private static readonly ConditionalWeakTable<PatchEvent, object> waits = [];

    /// <summary>Whether <paramref name="happened"/> is a wait for a refusal rather than anything the assistant did.</summary>
    internal static bool IsWait(PatchEvent happened) => waits.TryGetValue(happened, out _);

    /// <summary>How long <paramref name="happened"/> waited for a refusal, or null where it is not a wait.</summary>
    internal static TimeSpan? Waited(PatchEvent happened) =>
        waits.TryGetValue(happened, out var wait) ? (TimeSpan)wait : null;

    /// <summary>What the model is told, once, when it changed the patch and stopped without offering it.</summary>
    internal const string Unproposed =
        "[From Flyback, not the person: you changed the patch this turn and did not propose it, so the "
        + "person still sees what was there before. If it does what was asked, call propose now. If it "
        + "does not, say in a line what is left, and do not call it done.]";

    /// <summary>The answer to a call the person stopped the turn before it ran.</summary>
    internal const string Stopped = "not run: the person stopped this turn.";

    /// <summary>The answer to a call that came after a proposal in the same batch.</summary>
    internal const string AfterProposal = "not run: you had already proposed the patch, and proposing ends the turn.";

    /// <summary>
    /// What the ear is told before it is played anything. About listening rather than
    /// synthesis: a model told what the patch was meant to do hears what it was told
    /// rather than what came out.
    /// </summary>
    internal const string Ear = """
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

    /// <summary>Asks <paramref name="instruction"/> of <paramref name="conversation"/>, over its workbench.</summary>
    public static IAsyncEnumerable<PatchEvent> Run(
        IModelConversation conversation,
        string instruction,
        CancellationToken cancel = default) => Run(conversation, instruction, int.MaxValue, cancel);

    /// <summary>
    /// Asks <paramref name="instruction"/> of <paramref name="conversation"/>, and
    /// stops before a request once the last one sent <paramref name="contextLimit"/>
    /// tokens or more.
    /// </summary>
    internal static async IAsyncEnumerable<PatchEvent> Run(
        IModelConversation conversation,
        string instruction,
        int contextLimit,
        [EnumeratorCancellation] CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        var workbench = conversation.Workbench;

        // Waits a request tells of while it is still waiting, so the transcript is
        // not silent for the minute a rate limit can take.
        var told = Channel.CreateUnbounded<PatchEvent>();

        // Whatever was offered last time is not this turn's answer. The patch
        // itself stays: a conversation carries on from what it built.
        workbench.Reopen();

        // Pictures and clips from earlier turns go as a line each: every request of
        // this turn would otherwise carry them again, and the model can look again.
        conversation.Forget();

        if (!workbench.Hears && Unheard.Asked(instruction))
        {
            yield return new PatchEvent.Did(Unheard.Told);
            instruction = Unheard.Note + Environment.NewLine + Environment.NewLine + instruction;
        }

        conversation.Add(instruction);

        var nudged = false;
        var sent = 0;

        for (var exchange = 0; exchange < MaxExchanges; exchange++)
        {
            if (cancel.IsCancellationRequested) yield break;

            if (sent >= contextLimit)
            {
                yield return new PatchEvent.Failed(Grown(sent, contextLimit));
                yield break;
            }

            // The send is guarded and the yielding happens after it, because a
            // yield may not sit inside a catch.
            ModelReply? reply = null;
            string? failure = null;

            var sending = Telling(() => conversation.Send(cancel), told.Writer);

            await foreach (var wait in Until(sending, told.Reader).ConfigureAwait(false)) yield return wait;

            try
            {
                reply = await sending.ConfigureAwait(false);
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

            sent = reply.Input;

            if (reply.Text is { } text) yield return new PatchEvent.Said(text);
            if (reply.Input > 0 || reply.Output > 0)
                yield return new PatchEvent.Cost(reply.Input, reply.Cached, reply.Output);

            if (reply.Calls.Count > 0)
            {
                var answers = new List<ToolAnswer>();

                foreach (var call in reply.Calls)
                {
                    // Every call gets exactly one answer, refusals included: one left
                    // unanswered makes every later request a 400, which ends the
                    // conversation rather than the call. So the calls a stop or a
                    // proposal cut off are answered too.
                    if (workbench.HasProposal || cancel.IsCancellationRequested)
                    {
                        answers.Add(new ToolAnswer(call, workbench.HasProposal ? AfterProposal : Stopped));
                        continue;
                    }

                    ToolOutcome outcome;

                    try
                    {
                        outcome = await Answer(workbench, call, cancel).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        answers.Add(new ToolAnswer(call, Stopped));
                        continue;
                    }

                    var said = outcome.Text;

                    // Described only where the clip cannot reach this model. Where
                    // it can, a description by another would be a second opinion
                    // nobody asked for, paid for by a second request.
                    if (outcome.Wav is { } borrowed && !conversation.HearsItself)
                    {
                        var describing = Telling(() => Described(conversation, borrowed, cancel), told.Writer);

                        await foreach (var wait in Until(describing, told.Reader).ConfigureAwait(false)) yield return wait;

                        try
                        {
                            said += "\n\n" + await describing.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            answers.Add(new ToolAnswer(call, Stopped));
                            continue;
                        }
                    }

                    answers.Add(new ToolAnswer(call, said, outcome.Png, conversation.HearsItself ? outcome.Wav : null));

                    if (outcome.Png is { } png) yield return new PatchEvent.Saw(png, said);
                    else if (outcome.Wav is { } wav) yield return new PatchEvent.Heard(wav, said);
                    else if (outcome.Reference) yield return new PatchEvent.Read(said);
                    else yield return new PatchEvent.Did(said);
                }

                conversation.Add(answers);

                if (cancel.IsCancellationRequested && !workbench.HasProposal) yield break;
            }

            // Asked for last, so a proposal is noticed whether it arrived among
            // this exchange's calls or the model simply stopped after making one.
            if (workbench.HasProposal)
            {
                yield return new PatchEvent.Proposed(workbench.Snapshot(), workbench.ProposalSummary);
                yield break;
            }

            if (reply.Calls.Count == 0)
            {
                // A model that built something and stopped short of offering it is
                // asked once, unless it stopped on a question, which the person
                // answers.
                if (workbench.Edits > 0 && !nudged && !Asks(reply.Text))
                {
                    nudged = true;
                    conversation.Add(Unproposed);
                    continue;
                }

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
            $"stopped after {MaxExchanges} exchanges in one turn, which is as many as there are.");
    }

    /// <summary>Why a conversation whose last request sent <paramref name="sent"/> tokens takes no more.</summary>
    internal static string Grown(int sent, int contextLimit) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"this conversation has grown to {sent:N0} tokens, past its limit of {contextLimit:N0}. Start another one.");

    /// <summary>
    /// Runs <paramref name="request"/>, writing each wait it tells of that is at
    /// least <see cref="ToldWait"/> to <paramref name="told"/> as a line.
    /// </summary>
    private static async Task<T> Telling<T>(Func<Task<T>> request, ChannelWriter<PatchEvent> told)
    {
        AssistantPost.Waiting = (wait, status) =>
        {
            if (wait >= ToldWait) told.TryWrite(Wait(wait, status));
        };

        return await request().ConfigureAwait(false);
    }

    /// <summary>The line a wait of <paramref name="wait"/> for a refusal with <paramref name="status"/> is told as.</summary>
    internal static PatchEvent Wait(TimeSpan wait, int status)
    {
        var seconds = (int)Math.Ceiling(wait.TotalSeconds);

        PatchEvent line = new PatchEvent.Did(status == 429
            ? $"waiting {seconds}s for the rate limit"
            : $"waiting {seconds}s: the endpoint answered {status}");

        waits.Add(line, wait);

        return line;
    }

    /// <summary>What <paramref name="told"/> is handed until <paramref name="request"/> is done, as it arrives.</summary>
    private static async IAsyncEnumerable<PatchEvent> Until(Task request, ChannelReader<PatchEvent> told)
    {
        while (!request.IsCompleted)
        {
            await Task.WhenAny(request, told.WaitToReadAsync().AsTask()).ConfigureAwait(false);

            while (told.TryRead(out var line)) yield return line;
        }

        while (told.TryRead(out var line)) yield return line;
    }

    /// <summary>Whether <paramref name="said"/> ends on a question, which is for the person to answer.</summary>
    private static bool Asks(string? said) =>
        said?.TrimEnd(' ', '\t', '\r', '\n', '*', '_', '"', ')').EndsWith('?') == true;

    /// <summary>Hands one call to the workbench, with arguments that will not read refused rather than thrown.</summary>
    private static async Task<ToolOutcome> Answer(PatchWorkbench workbench, ToolCall call, CancellationToken cancel)
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

    /// <summary>
    /// What the ear heard, as words for a model that cannot hear. A failure is a
    /// sentence in the answer rather than the end of the turn: the sound was
    /// rendered and its levels are already known.
    /// </summary>
    private static async Task<string> Described(IModelConversation conversation, byte[] wav, CancellationToken cancel)
    {
        var ear = conversation.EarModel;

        if (string.IsNullOrWhiteSpace(ear))
            return "No model is set to listen with, so nobody has heard this.";

        try
        {
            return await conversation.Listen(ear, Ear, wav, cancel).ConfigureAwait(false) is { Length: > 0 } heard
                ? $"{ear} listened to it and says: {heard}"
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
}
