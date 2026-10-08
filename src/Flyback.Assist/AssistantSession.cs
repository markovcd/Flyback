using System.Runtime.CompilerServices;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;

namespace Flyback.Assist;

/// <summary>
/// A conversation as a host keeps it from one message to the next: the run, its
/// log, and what the transcript is told when one begins and on every turn.
/// </summary>
/// <remarks>
/// The editor's column and <c>flyback-cli ask</c> both drive this. Each decides
/// for itself when a conversation has to start again, since only the editor has a
/// canvas edited underneath it, and makes the run, since only it knows the patch.
/// </remarks>
/// <param name="logFolder">Somewhere other than the usual place for the log, for the tests.</param>
/// <param name="decisions">What reads a message before it is sent and the proposal once it comes back; none reads nothing.</param>
internal sealed class AssistantSession(ITranscript transcript, string? logFolder = null, Decisions? decisions = null) : IDisposable
{
    private ConversationLog log = ConversationLog.Start(false, string.Empty);

    private readonly TurnReading? reading = decisions is null ? null : new TurnReading(decisions);

    /// <summary>The conversation going, or null before the first message.</summary>
    public AssistantRun? Run { get; private set; }

    /// <summary>Who it is with, and what they were set to.</summary>
    public IPatchAssistant? Assistant { get; private set; }

    public AssistantConfig? Config { get; private set; }

    /// <summary>
    /// Why the conversation going cannot take the next message with these
    /// settings, or null where it can or where none is going.
    /// </summary>
    public string? Spent(IPatchAssistant with, AssistantConfig config)
    {
        if (Run is null) return null;
        if (Run.Exhausted) return AssistantRun.Grown;

        return !ReferenceEquals(Assistant, with) || Config != config
            ? "The settings changed, so this is a new conversation."
            : null;
    }

    /// <summary>
    /// Takes <paramref name="run"/> as the conversation, in place of whatever went
    /// before, and says so: where it carries on a saved one, only whether the
    /// provider remembered; where it is new, why the transcript was cleared, and
    /// the briefing it was handed.
    /// </summary>
    /// <param name="resumed">Whether <paramref name="run"/> carries on a conversation saved with the patch.</param>
    /// <param name="because">Why a new one was started, or empty to say nothing.</param>
    public void Begin(
        AssistantRun run,
        IPatchAssistant with,
        AssistantConfig config,
        AssistantSettings settings,
        bool resumed,
        string? because)
    {
        Run?.Dispose();
        Run = run;
        Assistant = with;
        Config = config;

        log.Dispose();
        log = ConversationLog.Start(settings.LogConversations, with.Id, logFolder);

        if (resumed)
        {
            // Nothing is cleared: the transcript showing is this conversation's.
            if (!run.PickedUp)
                Put(Voice.Note, "note", $"{with.Name} could not pick up what was said before, so it starts again from the patch it had built.");

            return;
        }

        // Said only where there was something to lose: the transcript emptying is
        // otherwise the only sign the correspondent has just been replaced.
        if (!transcript.IsEmpty)
        {
            transcript.Clear();

            if (because is { Length: > 0 }) Put(Voice.Note, "note", because);
        }

        // Not saved with the patch: it is rebuilt for every conversation.
        var briefing = $"The briefing it was handed:{Environment.NewLine}{run.Workbench.Briefing}";

        transcript.Put(new Spoken(new TranscriptLine(Voice.Briefing, briefing), "briefing", run.Workbench.Briefing, Keep: false));

        if (settings.ShowBriefing) log.Write("briefing", briefing);
    }

    /// <summary>One turn, written to the transcript and the log as it happens, and handed on.</summary>
    public async IAsyncEnumerable<PatchEvent> Ask(string message, [EnumeratorCancellation] CancellationToken cancel = default)
    {
        var run = Run ?? throw new InvalidOperationException("Begin a conversation before asking anything in it.");
        var scrubbed = Config?.Transport is KeyedTransport keyed ? keyed.Scrubbed(message)! : message;

        transcript.Put(new Spoken(new TranscriptLine(Voice.You, message), "you", scrubbed));
        log.Write("you", scrubbed);

        if (run.Unsaid is { } told) Put(Voice.Aside, "told", $"Told it what changed on the canvas: {told}.", told);

        var sent = message;

        if (reading is not null && decisions?.Chosen is not null
            && await reading.Read(message, IssueTriage.Summary(run.Workbench.Snapshot(), NodeCatalog.Current), cancel).ConfigureAwait(true) is { } read)
        {
            log.Write("intent", $"Read as {read.Said} ({read.Probability:0.00}).");

            if (read.Asks)
            {
                Put(Voice.Aside, "intent", $"Read as {read.Said} ({read.Probability:0.00}), so it was asked to answer rather than build.");
                sent = TurnReading.Answering + Environment.NewLine + Environment.NewLine + message;
            }
        }

        // On the caller's context, since the transcript may be a control.
        await foreach (var happened in run.Ask(sent, cancel).ConfigureAwait(true))
        {
            // Ended or replaced while it ran: what is still on its way belongs to
            // no conversation the transcript shows.
            if (!ReferenceEquals(Run, run)) yield break;

            var spoken = Spoken.Of(happened);

            transcript.Put(spoken);
            log.Write(spoken.Kind, spoken.Text);

            yield return happened;
        }

        if (reading is not null && decisions?.Chosen is not null && ReferenceEquals(Run, run) && run.Proposal is { } proposal
            && await reading.Does(message, run.ProposalSummary, cancel).ConfigureAwait(true) is { } does
            && does < TurnReading.Doubted)
        {
            Put(Voice.Aside, "doubt", $"This may not be what was asked for ({does:0.00} that it is). Ctrl+Z puts the patch back.");
        }
    }

    /// <summary>The conversation as it stands, for saving with the patch. Between turns.</summary>
    public SavedConversation Save() =>
        (Run ?? throw new InvalidOperationException("There is no conversation to save.")).Save(transcript.Lines);

    /// <summary>Lets the conversation go, leaving none.</summary>
    public void End()
    {
        Run?.Dispose();
        Run = null;
        Assistant = null;
        Config = null;

        log.Dispose();
        log = ConversationLog.Start(false, string.Empty);
    }

    private void Put(Voice voice, string kind, string shown, string? text = null)
    {
        transcript.Put(new Spoken(new TranscriptLine(voice, shown), kind, text ?? shown));
        log.Write(kind, text ?? shown);
    }

    public void Dispose() => End();
}
