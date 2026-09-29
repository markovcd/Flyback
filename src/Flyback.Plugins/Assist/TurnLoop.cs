using System.Runtime.CompilerServices;
using System.Text.Json;

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
    public static async IAsyncEnumerable<PatchEvent> Run(
        IModelConversation conversation,
        string instruction,
        [EnumeratorCancellation] CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        var workbench = conversation.Workbench;

        // Whatever was offered last time is not this turn's answer. The patch
        // itself stays: a conversation carries on from what it built.
        workbench.Reopen();

        // Pictures and clips from earlier turns go as a line each: every request of
        // this turn would otherwise carry them again, and the model can look again.
        conversation.Forget();
        conversation.Add(instruction);

        for (var exchange = 0; exchange < MaxExchanges; exchange++)
        {
            if (cancel.IsCancellationRequested) yield break;

            // The send is guarded and the yielding happens after it, because a
            // yield may not sit inside a catch.
            ModelReply? reply = null;
            string? failure = null;

            try
            {
                reply = await conversation.Send(cancel).ConfigureAwait(false);
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
                    string said;

                    try
                    {
                        outcome = await Answer(workbench, call, cancel).ConfigureAwait(false);
                        said = outcome.Text;

                        // Described only where the clip cannot reach this model. Where
                        // it can, a description by another would be a second opinion
                        // nobody asked for, paid for by a second request.
                        if (outcome.Wav is { } borrowed && !conversation.HearsItself)
                            said += "\n\n" + await Described(conversation, borrowed, cancel).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        answers.Add(new ToolAnswer(call, Stopped));
                        continue;
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
