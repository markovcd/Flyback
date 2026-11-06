using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;

namespace Flyback.Editor.Assist;

/// <summary>
/// The workbench a program driving the editor calls tools on: its own copy of the patch on
/// the canvas, which only <c>propose</c> puts on the canvas, as one edit.
/// </summary>
/// <remarks>
/// Kept from one call to the next, so the handles the client chose still name their modules.
/// While nothing is waiting to be proposed, a canvas changed by anything else, a hand edit or
/// another patch opened, is taken up afresh, keeping the handles of the modules still there.
/// Once the bench has edits of its own the canvas is left alone until <c>propose</c>, which
/// treats changes made meanwhile as the assistant's column does: knobs turned are carried
/// where the program left them alone, and modules or wires changed are replaced.
/// </remarks>
internal sealed class DrivenBench(
    IAssistantEditor editor,
    ModuleCatalog modules,
    Func<ProsePolicy> prose,
    Func<IReadOnlyList<PatchPreset>> presets)
{
    private PatchWorkbench? bench;

    /// <summary>The canvas as the bench last took it up or put a proposal on it, as patch JSON.</summary>
    private string? seen;

    /// <summary>Whether the bench holds edits the canvas has not been given.</summary>
    private bool unproposed;

    /// <summary>The summary of what the last call put on the canvas, or null where it put nothing.</summary>
    public string? Proposed { get; private set; }

    /// <summary>The briefing, written from the modules the editor has loaded.</summary>
    public string Briefing => Bench().Briefing;

    public async Task<ToolOutcome> Call(string tool, JsonElement arguments, CancellationToken cancel)
    {
        Proposed = null;

        var bench = Bench();

        bench.Reopen();

        var outcome = await bench.InvokeAsync(tool, arguments, cancel).ConfigureAwait(true);

        if (outcome.Ok && bench.Edits > 0) unproposed = true;

        return bench.HasProposal ? Deliver(bench, outcome) : outcome;
    }

    /// <summary>Puts the proposal on the canvas, with whatever the person changed meanwhile.</summary>
    private ToolOutcome Deliver(PatchWorkbench bench, ToolOutcome outcome)
    {
        var was = PatchIO.Read(seen!, modules).Patch;
        var now = editor.Current;
        var proposal = bench.Snapshot();
        var text = bench.Text;

        var overwrote = !PatchShape.Of(was).Matches(now);
        IReadOnlyList<Retuned> carried = overwrote ? [] : Retuning.Carry(was, now, proposal);

        // A carried knob is not in the bench's text, so the canvas writes its own.
        if (carried.Any(change => change.Kept)) text = null;

        editor.Apply(proposal, text);

        Proposed = bench.ProposalSummary;
        Over(bench.Save().Handles);

        return ToolOutcome.Fine($"{outcome.Text} It is on the person's canvas now. {Applied(overwrote, carried)}");
    }

    private static string Applied(bool overwrote, IReadOnlyList<Retuned> carried)
    {
        if (overwrote) return "It replaced modules or wires the person changed since you last looked; Ctrl+Z puts theirs back.";

        var kept = carried.Count(change => change.Kept);
        var overruled = carried.Count - kept;

        var keeping = kept == 0 ? string.Empty : $"It keeps {TranscriptView.Tally(kept, "setting")} the person changed meanwhile. ";
        var standing = overruled == 0 ? string.Empty : $"Your value stands for {TranscriptView.Tally(overruled, "setting")} they also changed. ";

        return $"{keeping}{standing}Ctrl+Z puts the patch back as it was.".Trim();
    }

    /// <summary>The workbench, taking the canvas up afresh where it changed and nothing waits to be proposed.</summary>
    private PatchWorkbench Bench()
    {
        if (bench is not null && (unproposed || PatchIO.ToJson(editor.Current, modules) == seen)) return bench;

        return Over(bench?.Save().Handles);
    }

    /// <summary>A workbench over the canvas as it stands, which is also what <c>reset</c> goes back to.</summary>
    private PatchWorkbench Over(IReadOnlyDictionary<string, Guid>? handles)
    {
        seen = PatchIO.ToJson(editor.Current, modules);
        unproposed = false;

        bench = new PatchWorkbench(
            modules,
            PatchIO.Read(seen, modules).Patch,
            vision: true,
            hearing: Listener.None,
            samples: editor.Samples,
            pictures: editor.Pictures,
            prose: prose(),
            presets: presets(),
            text: editor.Text,
            delivery: Delivery.Proposal);

        if (handles is not null)
            bench.Restore(new WorkbenchState(seen, seen, handles, 0, 0) { Text = editor.Text });

        return bench;
    }
}
