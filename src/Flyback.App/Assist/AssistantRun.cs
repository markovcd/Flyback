using System.Runtime.CompilerServices;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;

namespace Flyback.App.Assist;

/// <summary>
/// One conversation with an assistant, and everything around it that is not a
/// control: the workbench it edits, the session it speaks through, what it has
/// proposed, and what to do when it will not stop.
/// </summary>
/// <remarks>
/// <para>
/// No Avalonia type appears here, on purpose. This is the same seam
/// <see cref="Audio.AudioEngine"/> is — the part of the shell that can be driven
/// by a test with a fake on the far side, leaving the window with nothing but
/// controls and a loop that paints events.
/// </para>
/// <para>
/// The patch that was open is never touched. The workbench takes a copy, so
/// accepting a proposal is one assignment and rejecting one costs nothing —
/// which is the whole of why an application with no undo can afford this
/// feature.
/// </para>
/// </remarks>
public sealed class AssistantRun : IDisposable
{
    private readonly IPatchSession session;
    private readonly int maxTurns;
    private readonly int startingNodes;
    private readonly int startingWires;

    private CancellationTokenSource? working;
    private bool spent;

    public AssistantRun(
        IPatchAssistant assistant,
        AssistantConfig config,
        ModuleCatalog modules,
        Patch startingPoint,
        int maxTurns = 12,
        WorkbenchLimits? limits = null)
    {
        Before = startingPoint;
        this.maxTurns = maxTurns;

        startingNodes = startingPoint.Nodes.Count;
        startingWires = startingPoint.Connections.Count;

        Workbench = new PatchWorkbench(modules, startingPoint, config.Vision, limits);
        session = assistant.Start(Workbench, config);
    }

    /// <summary>
    /// The patch as it was before any of this, still exactly as it was. Putting
    /// it back is assigning this to the editor.
    /// </summary>
    public Patch Before { get; }

    public PatchWorkbench Workbench { get; }

    /// <summary>The last patch offered, or null when nothing has been.</summary>
    public Patch? Proposal { get; private set; }

    public string ProposalSummary { get; private set; } = string.Empty;

    public bool Running => working is not null;

    public int Turns { get; private set; }

    /// <summary>
    /// Whether the person edited the patch themselves while this was running.
    /// A proposal replaces whatever they did, so the caller is expected to say
    /// so — which is all it need do, now that applying one is an edit somebody
    /// can take back rather than a document that arrived in place of theirs.
    /// </summary>
    public bool EditedUnderneath(Patch current) =>
        !ReferenceEquals(current, Before)
        || current.Nodes.Count != startingNodes
        || current.Connections.Count != startingWires;

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
