using System.Text.Json;
using Flyback.Assist;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;

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

    /// <summary>What the person changed on the canvas since the last call, for the next answer to open with.</summary>
    private string? meanwhile;

    /// <summary>The summary of what the last call put on the canvas, or null where it put nothing.</summary>
    public string? Proposed { get; private set; }

    /// <summary>The briefing, written from the modules the editor has loaded.</summary>
    public string Briefing => Bench().Briefing;

    public async Task<ToolOutcome> Call(string tool, JsonElement arguments, CancellationToken cancel)
    {
        Proposed = null;

        var current = Bench();

        current.Reopen();

        var outcome = await current.InvokeAsync(tool, arguments, cancel).ConfigureAwait(true);

        if (outcome.Ok && current.Edits > 0) unproposed = true;

        if (meanwhile is { } changed)
        {
            meanwhile = null;
            outcome = outcome with { Text = $"{changed}\n\n{outcome.Text}" };
        }

        return current.HasProposal ? Deliver(current, outcome) : outcome;
    }

    /// <summary>Puts the proposal on the canvas, with whatever the person changed meanwhile.</summary>
    private ToolOutcome Deliver(PatchWorkbench working, ToolOutcome outcome)
    {
        var merged = MergedProposal.Of(PatchIO.Read(seen!, modules).Patch, editor.Current, working.Snapshot(), working.Text);

        editor.Apply(merged.Patch, merged.Text);

        Proposed = working.ProposalSummary;

        // A canvas that owns its patch keeps no text, and the bench's still says exactly what it holds.
        Over(working.Save().Handles, merged.Text ?? editor.Text);

        return ToolOutcome.Fine($"{outcome.Text} It is on the person's canvas now. {Applied(merged)}");
    }

    private static string Applied(MergedProposal merged)
    {
        if (merged.Overwrote) return "It replaced modules or wires the person changed since you last looked; Ctrl+Z puts theirs back.";

        var keeping = merged.Kept == 0 ? string.Empty : $"It keeps {TranscriptView.Tally(merged.Kept, "setting")} the person changed meanwhile. ";
        var standing = merged.Overruled == 0 ? string.Empty : $"Your value stands for {TranscriptView.Tally(merged.Overruled, "setting")} they also changed. ";

        return $"{keeping}{standing}Ctrl+Z puts the patch back as it was.".Trim();
    }

    /// <summary>The workbench, taking the canvas up afresh where it changed and nothing waits to be proposed.</summary>
    private PatchWorkbench Bench()
    {
        if (bench is not null && (unproposed || PatchIO.ToJson(editor.Current, modules) == seen)) return bench;

        if (bench is not null) meanwhile = Changed(bench);

        return Over(bench?.Save().Handles, editor.Text);
    }

    /// <summary>What the person changed on the canvas since <paramref name="working"/> took it up, in its handles.</summary>
    private string Changed(PatchWorkbench working)
    {
        var was = PatchIO.Read(seen!, modules).Patch;
        var now = editor.Current;

        if (!PatchShape.Of(was).Matches(now))
            return "The person changed modules or wires on the canvas since your last call, or opened another patch; "
                + "describe_patch shows it as it stands now.";

        var carried = Retuning.Carry(was, now, Retuning.Copy(was));

        return carried.Count == 0
            ? "The person moved or changed something on the canvas since your last call; describe_patch shows it as it stands now."
            : $"The person changed on the canvas since your last call: {working.Told(carried)}.";
    }

    /// <summary>A workbench over the canvas as it stands, written in <paramref name="text"/>, which is also what <c>reset</c> goes back to.</summary>
    private PatchWorkbench Over(IReadOnlyDictionary<string, Guid>? handles, string? text)
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
            text: text,
            delivery: Delivery.Proposal);

        if (handles is not null)
            bench.Restore(new WorkbenchState(seen, seen, handles, 0, 0) { Text = text });

        return bench;
    }
}
