using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Assist;

/// <summary>
/// One conversation with an assistant, and everything around it that is not a
/// control: the workbench it edits, the session it speaks through, what it has
/// proposed, and what to do when it will not stop.
/// </summary>
/// <remarks>
/// The editor's panel and <c>flyback-cli ask</c> both drive this, so neither holds
/// anything but its controls or its console. The patch that was open is never
/// touched: the workbench takes a copy, so accepting a proposal is one assignment
/// and rejecting one costs nothing.
/// </remarks>
internal sealed class AssistantRun : IDisposable
{
    /// <summary>How many turns a conversation may have before another has to be started, until a setting says otherwise.</summary>
    public const int TurnLimit = AssistantSettings.DefaultTurnLimit;

    private readonly IPatchSession session;

    /// <summary>
    /// How many turns this conversation may have. Settable rather than fixed at
    /// the start, so a limit saved in the settings reaches the conversation
    /// already going rather than only the next one.
    /// </summary>
    public int MaxTurns { get; set; }

    /// <summary>Who this is with and what they were set to, which a saved conversation is checked against.</summary>
    private readonly string provider;

    private readonly SettingValues values;

    /// <summary>What sends this conversation, which knows the key well enough to keep it out of it.</summary>
    private readonly KeyedTransport? keyed;

    /// <summary>
    /// The canvas as the workbench last took it in: a copy, since the editor edits
    /// its patch in place.
    /// </summary>
    private Patch seen;

    private PatchShape shape;

    /// <summary>Settings carried in from the canvas that the model has not been told of yet.</summary>
    private readonly List<Retuned> unsaid = [];

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
    /// <param name="prose">How much of the catalog's prose the briefing carries.</param>
    /// <param name="presets">The presets the model may read for ideas, and the shipped ones where nobody said.</param>
    internal AssistantRun(
        IPatchAssistant assistant,
        AssistantConfig config,
        ModuleCatalog modules,
        Patch startingPoint,
        int maxTurns = TurnLimit,
        WorkbenchLimits? limits = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        SavedConversation? resuming = null,
        ProsePolicy? prose = null,
        IReadOnlyList<PatchPreset>? presets = null)
    {
        See(startingPoint);
        MaxTurns = maxTurns;

        provider = assistant.Id;
        values = config.Values;
        keyed = config.Transport as KeyedTransport;

        // What the workbench may offer is the provider's answer rather than
        // this one's. The shell knows nothing about which model was chosen —
        // the boundary ADR-0025 drew, ADR-0047 kept and ADR-0069 finished — so
        // it asks in terms of what happens: may a frame be shown, and who is
        // played the sound.
        var senses = assistant.Senses(config.Values);

        var restored = resuming is null ? null : Restored(resuming, modules, senses, limits, samples, pictures, prose, presets);

        Workbench = restored ?? new PatchWorkbench(
            modules, startingPoint, senses.Vision, senses.Hearing, limits, samples, pictures, prose, presets);

        if (restored is null)
        {
            session = assistant.Start(Workbench, config);
            return;
        }

        Turns = resuming!.Turns;

        // The patch may have been saved again after this conversation's last turn,
        // with knobs the workbench never saw.
        Remember(Canvas(resuming.Canvas) is { } was
            ? Workbench.Follow(was, startingPoint)
            : Workbench.Follow(startingPoint));

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
        IImageLibrary? pictures,
        ProsePolicy? prose,
        IReadOnlyList<PatchPreset>? presets)
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
                pictures,
                prose,
                presets);

            bench.Restore(saved.Bench);

            return bench;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether this conversation has had all the turns it may have.</summary>
    public bool Exhausted => Turns >= MaxTurns;

    public PatchWorkbench Workbench { get; }

    /// <summary>The last patch offered, or null when nothing has been.</summary>
    public Patch? Proposal { get; private set; }

    public string ProposalSummary { get; private set; } = string.Empty;

    public bool Running => working is not null;

    public int Turns { get; private set; }

    /// <summary>
    /// Whether the canvas gained or lost a module or a wire since the workbench last
    /// took it in, which makes it a different patch. Settings changed alone do not.
    /// </summary>
    public bool Reshaped(Patch current) => !shape.Matches(current);

    /// <summary>
    /// Takes the patch just applied as the canvas, so what this run put in the
    /// editor does not read as somebody editing behind it.
    /// </summary>
    public void Rebase(Patch applied) => See(applied);

    /// <summary>
    /// Carries settings changed on the canvas since the workbench last looked into
    /// it, for the next message to mention. Between turns.
    /// </summary>
    public void CatchUp(Patch current)
    {
        Remember(Workbench.Follow(seen, current));
        See(current);
    }

    /// <summary>
    /// Carries settings changed on the canvas while the turn ran into its proposal
    /// and into the workbench, except where the assistant changed the same one.
    /// </summary>
    /// <returns>Every setting changed on the canvas, and whether each was kept.</returns>
    internal IReadOnlyList<Retuned> Merge(Patch current)
    {
        if (Proposal is null) return [];

        var carried = Retuning.Carry(seen, current, Proposal);

        Remember(Workbench.Follow(seen, current));

        return carried;
    }

    /// <summary>
    /// The settings the next message tells the model were changed on the canvas, or
    /// null where there are none.
    /// </summary>
    public string? Unsaid => unsaid.Count == 0 ? null : Workbench.Told(unsaid);

    [MemberNotNull(nameof(seen), nameof(shape))]
    private void See(Patch canvas)
    {
        seen = Retuning.Copy(canvas);
        shape = PatchShape.Of(canvas);
    }

    private void Remember(IEnumerable<Retuned> carried)
    {
        foreach (var change in carried.Where(change => change.Kept))
        {
            unsaid.RemoveAll(change.SameSetting);
            unsaid.Add(change);
        }
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
    internal SavedConversation Save(IReadOnlyList<TranscriptLine> transcript)
    {
        string? history;

        try
        {
            history = keyed is null ? session.Save() : keyed.Scrubbed(session.Save());
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
            [.. transcript.Select(line => keyed?.Holds(line.Text) == true ? line with { Text = keyed.Scrubbed(line.Text)! } : line)],
            PatchIO.ToJson(seen));
    }

    /// <summary>The canvas a saved conversation last saw, or null where it did not say or will not read.</summary>
    private static Patch? Canvas(string? saved)
    {
        if (saved is null) return null;

        try
        {
            return PatchIO.Read(saved).Patch;
        }
        catch (JsonException)
        {
            return null;
        }
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

        // Sent, it would reach the model and whatever the conversation is saved into.
        if (keyed?.Holds(instruction) == true)
        {
            yield return new PatchEvent.Failed("that message has your key in it, so it was not sent. Take the key out and send it again.");
            yield break;
        }

        if (Turns >= MaxTurns)
        {
            yield return new PatchEvent.Failed(
                $"this conversation has had its {MaxTurns} turns. Start another one.");
            yield break;
        }

        Turns++;

        List<Retuned> telling = [.. unsaid];

        // One line ahead of the message rather than a new conversation: the
        // history and the provider's cache of it stay good.
        if (Unsaid is { } told)
        {
            instruction = $"[Changed on the canvas since your last turn, and already on your workbench: {told}.]"
                + Environment.NewLine + Environment.NewLine + instruction;

            unsaid.Clear();
        }

        // Last turn's patch is not this turn's answer. A conversation that
        // carries on past a proposal would otherwise leave one standing, and the
        // caller — which applies whatever is here when a turn ends — would put
        // the same patch in the editor again for a message that never asked for
        // one.
        Proposal = null;
        ProposalSummary = string.Empty;

        using var mine = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        working = mine;

        var answered = false;

        try
        {
            await foreach (var happened in Guarded(instruction, mine.Token).ConfigureAwait(false))
            {
                if (happened is not PatchEvent.Failed) answered = true;

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

            // A turn that failed before anything came back, a rate limit or a
            // refused key, is not one of the conversation's turns, and what it
            // was to say about the canvas is said with the next one.
            if (!answered)
            {
                Turns--;

                foreach (var change in telling.Where(change => !unsaid.Any(change.SameSetting))) unsaid.Add(change);
            }
        }
    }

    /// <summary>
    /// The turn's sequence, with anything it throws turned into a
    /// <see cref="PatchEvent.Failed"/>. The contract says a provider failure is
    /// already an event rather than an exception — this is here because a plugin
    /// runs in-process with full trust and a bug in one must still cost the turn
    /// rather than the window.
    /// </summary>
    /// <remarks>
    /// A session that is an <see cref="IModelConversation"/> has its turn run here,
    /// by <see cref="TurnLoop"/>, whatever its own <c>Ask</c> would do: what a turn
    /// promises the panel is this side's to keep.
    /// </remarks>
    private async IAsyncEnumerable<PatchEvent> Guarded(
        string instruction,
        [EnumeratorCancellation] CancellationToken cancel)
    {
        IAsyncEnumerator<PatchEvent>? events = null;
        string? failure = null;

        try
        {
            var turn = session is IModelConversation conversation
                ? TurnLoop.Run(conversation, instruction, cancel)
                : session.Ask(instruction, cancel);

            events = turn.GetAsyncEnumerator(cancel);
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

    /// <summary>Why <paramref name="assistant"/> cannot be asked with <paramref name="config"/>, or null when it can.</summary>
    public static string? Unready(IPatchAssistant assistant, AssistantConfig config)
    {
        try
        {
            return Credentials.Elsewhere(assistant, config) ?? assistant.Unavailable(config);
        }
        catch (Exception ex)
        {
            // Answering this must not throw. One that does has said no.
            return $"{assistant.Name} could not say whether it is ready: {ex.Message}";
        }
    }

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
