using System.Runtime.CompilerServices;
using System.Text.Json;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;

namespace Flyback.App.Assist;

/// <summary>
/// One conversation with an assistant, and everything around it that is not a
/// control: the workbench it edits, the session it speaks through, what it has
/// proposed, and what to do when it will not stop.
/// </summary>
/// <remarks>
/// No Avalonia type appears here, on purpose — the same seam
/// <see cref="Audio.AudioEngine"/> is, which leaves the window with nothing but
/// controls and a loop that paints events. The patch that was open is never
/// touched: the workbench takes a copy, so accepting a proposal is one assignment
/// and rejecting one costs nothing.
/// </remarks>
public sealed class AssistantRun : IDisposable
{
    /// <summary>How many turns a conversation may have before another has to be started.</summary>
    public const int TurnLimit = 12;

    private readonly IPatchSession session;
    private readonly int maxTurns;

    /// <summary>Who this is with and what they were set to, which a saved conversation is checked against.</summary>
    private readonly string provider;

    private readonly AssistantValues values;

    private int startingNodes;
    private int startingWires;

    private CancellationTokenSource? working;
    private bool spent;

    /// <param name="assistant"></param>
    /// <param name="config"></param>
    /// <param name="modules"></param>
    /// <param name="startingPoint">
    /// The patch on the canvas, which is what the conversation is about and what
    /// an edit underneath is noticed against — for a conversation carried on as
    /// much as for a new one.
    /// </param>
    /// <param name="maxTurns"></param>
    /// <param name="limits"></param>
    /// <param name="samples"></param>
    /// <param name="pictures"></param>
    /// <param name="resuming">A conversation saved with that patch, to carry on rather than start afresh.</param>
    public AssistantRun(
        IPatchAssistant assistant,
        AssistantConfig config,
        ModuleCatalog modules,
        Patch startingPoint,
        int maxTurns = TurnLimit,
        WorkbenchLimits? limits = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        SavedConversation? resuming = null)
    {
        Before = startingPoint;
        this.maxTurns = maxTurns;

        provider = assistant.Id;
        values = config.Values;

        startingNodes = startingPoint.Nodes.Count;
        startingWires = startingPoint.Connections.Count;

        // What the workbench may offer is the provider's answer rather than
        // this one's. The shell knows nothing about which model was chosen —
        // the boundary ADR-0025 drew, ADR-0047 kept and ADR-0069 finished — so
        // it asks in terms of what happens: may a frame be shown, and who is
        // played the sound.
        var senses = assistant.Senses(config.Values);

        var restored = resuming is null ? null : Restored(resuming, modules, senses, limits, samples, pictures);

        Workbench = restored ?? new PatchWorkbench(
            modules, startingPoint, senses.Vision, senses.Hearing, limits, samples, pictures);

        if (restored is null)
        {
            session = assistant.Start(Workbench, config);
            return;
        }

        Turns = resuming!.Turns;

        session = PickUp(assistant, config, resuming.History) ?? assistant.Start(Workbench, config);
    }

    /// <summary>
    /// A workbench put back as a saved conversation left it, or null where what was
    /// saved will not read as a patch.
    /// </summary>
    /// <remarks>
    /// Built over the patch the conversation began on, which is what <c>reset</c>
    /// goes back to — not over the one it is being carried on from. Null leaves a
    /// conversation that starts again over the patch on the canvas, which is better
    /// than one that cannot start at all.
    /// </remarks>
    private static PatchWorkbench? Restored(
        SavedConversation saved,
        ModuleCatalog modules,
        AssistantSenses senses,
        WorkbenchLimits? limits,
        ISampleLibrary? samples,
        IImageLibrary? pictures)
    {
        try
        {
            var bench = new PatchWorkbench(
                modules,
                PatchIO.Read(saved.Bench.Start, modules).Patch,
                senses.Vision,
                senses.Hearing,
                limits,
                samples,
                pictures);

            bench.Restore(saved.Bench);

            return bench;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The patch as it was before any of this, still exactly as it was. Putting
    /// it back is assigning this to the editor.
    /// </summary>
    public Patch Before { get; private set; }

    /// <summary>Whether this conversation has had all the turns it may have.</summary>
    public bool Exhausted => Turns >= maxTurns;

    public PatchWorkbench Workbench { get; }

    /// <summary>The last patch offered, or null when nothing has been.</summary>
    public Patch? Proposal { get; private set; }

    public string ProposalSummary { get; private set; } = string.Empty;

    public bool Running => working is not null;

    public int Turns { get; private set; }

    /// <summary>
    /// Whether the person edited the patch themselves while this was running.
    /// A proposal replaces whatever they did, so the caller is expected to say
    /// so — which is all it need do, because applying one is an edit somebody
    /// can take back.
    /// </summary>
    public bool EditedUnderneath(Patch current) =>
        !ReferenceEquals(current, Before)
        || current.Nodes.Count != startingNodes
        || current.Connections.Count != startingWires;

    /// <summary>
    /// Takes the patch just applied as the new starting point, so what this run put
    /// in the editor does not read as somebody editing behind it.
    /// </summary>
    /// <remarks>
    /// What lets a conversation carry on after a proposal is applied: without it the
    /// next message would conclude the person had changed the patch underneath and
    /// start again, throwing away the history that produced what they accepted.
    /// </remarks>
    public void Rebase(Patch applied)
    {
        Before = applied;
        startingNodes = applied.Nodes.Count;
        startingWires = applied.Connections.Count;
    }

    /// <summary>
    /// Whether the provider took back what was said, for a conversation carried on
    /// from a saved one. False where it could not — which starts again from the
    /// patch it had built — and for every conversation that was not carried on.
    /// </summary>
    public bool PickedUp { get; private set; }

    /// <summary>
    /// The conversation as it stands, for saving with the patch. Asked between
    /// turns, because the provider's account is read out of a session that must
    /// not be running.
    /// </summary>
    public SavedConversation Save(IReadOnlyList<TranscriptLine> transcript)
    {
        string? history;

        try
        {
            history = session.Save();
        }
        catch
        {
            // A plugin that throws here costs the model its memory next time,
            // not the save.
            history = null;
        }

        return new SavedConversation(
            provider,
            SavedConversation.SettingsOf(values),
            Turns,
            Workbench.Save(),
            history,
            [.. transcript]);
    }

    private IPatchSession? PickUp(IPatchAssistant assistant, AssistantConfig config, string? history)
    {
        if (history is null) return null;

        try
        {
            var picked = assistant.Resume(Workbench, config, history);

            PickedUp = picked is not null;

            return picked;
        }
        catch
        {
            // As Guarded: a plugin runs with full trust, and one that throws on
            // the way back in has only failed to remember.
            return null;
        }
    }

    /// <summary>Asks for the current turn to stop. It ends at the next thing the assistant does.</summary>
    public void Stop() => working?.Cancel();

    /// <summary>
    /// One turn, as a sequence of things that happened.
    /// </summary>
    /// <remarks>
    /// Consumed with <c>await foreach</c> on the dispatcher, so every iteration
    /// resumes there and the caller can update controls by plain assignment —
    /// which is why the shell still has no <c>Dispatcher.UIThread.Post</c> in it.
    /// </remarks>
    public async IAsyncEnumerable<PatchEvent> Ask(
        string instruction,
        [EnumeratorCancellation] CancellationToken cancel = default)
    {
        if (Running)
        {
            yield return new PatchEvent.Failed("this assistant is already working on something.");
            yield break;
        }

        if (Turns >= maxTurns)
        {
            yield return new PatchEvent.Failed(
                $"this conversation has had its {maxTurns} turns. Start another one.");
            yield break;
        }

        Turns++;

        // Last turn's patch is not this turn's answer. A conversation that
        // carries on past a proposal would otherwise leave one standing, and the
        // caller — which applies whatever is here when a turn ends — would put
        // the same patch in the editor again for a message that never asked for
        // one.
        Proposal = null;
        ProposalSummary = string.Empty;

        using var mine = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        working = mine;

        try
        {
            await foreach (var happened in Guarded(instruction, mine.Token).ConfigureAwait(false))
            {
                if (happened is PatchEvent.Proposed proposed)
                {
                    Proposal = proposed.Patch;
                    ProposalSummary = proposed.Summary;
                }

                yield return happened;
            }
        }
        finally
        {
            working = null;
        }
    }

    /// <summary>
    /// The session's own sequence, with anything it throws turned into a
    /// <see cref="PatchEvent.Failed"/>. The contract says a provider failure is
    /// already an event rather than an exception — this is here because a plugin
    /// runs in-process with full trust and a bug in one must still cost the turn
    /// rather than the window.
    /// </summary>
    private async IAsyncEnumerable<PatchEvent> Guarded(
        string instruction,
        [EnumeratorCancellation] CancellationToken cancel)
    {
        IAsyncEnumerator<PatchEvent>? events = null;
        string? failure = null;

        try
        {
            events = session.Ask(instruction, cancel).GetAsyncEnumerator(cancel);
        }
        catch (Exception ex)
        {
            failure = Excuse(ex);
        }

        if (events is null)
        {
            yield return new PatchEvent.Failed(failure ?? "the assistant would not start.");
            yield break;
        }

        try
        {
            while (true)
            {
                // A yield may not sit inside a catch, so each turn of this loop
                // moves the sequence on under guard and hands the result over
                // afterwards, rather than doing both in one breath.
                PatchEvent? happened = null;

                try
                {
                    if (await events.MoveNextAsync().ConfigureAwait(false)) happened = events.Current;
                }
                catch (OperationCanceledException)
                {
                    // Asked to stop. Not a failure, and the workbench is a copy,
                    // so there is nothing to put back.
                }
                catch (Exception ex)
                {
                    failure = Excuse(ex);
                }

                if (happened is null) break;

                yield return happened;
            }
        }
        finally
        {
            await events.DisposeAsync().ConfigureAwait(false);
        }

        if (failure is not null) yield return new PatchEvent.Failed(failure);
    }

    private static string Excuse(Exception ex) => $"the assistant stopped: {ex.Message}";

    public void Dispose()
    {
        if (spent) return;
        spent = true;

        working?.Cancel();

        try
        {
            session.Dispose();
        }
        catch
        {
            // Disposing is the last thing that happens to a run. A plugin that
            // throws on the way out has nothing left to break.
        }
    }
}
