using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;

namespace Flyback.Editor.Assist;

/// <summary>
/// The workbench a program driving the editor calls tools on, over the patch on the canvas.
/// Each call that changes the patch puts it on the canvas as one edit.
/// </summary>
/// <remarks>
/// Kept from one call to the next while the canvas is as the last call left it, so the
/// handles the client chose still name their modules, and a failed edit still holds
/// <c>propose</c> back. A canvas changed by anything else, a hand edit or another patch
/// opened, is taken up afresh, keeping the handles of the modules still there, and becomes
/// what <c>reset</c> goes back to; so does a patch proposed.
/// </remarks>
internal sealed class DrivenBench(
    IAssistantEditor editor,
    ModuleCatalog modules,
    Func<ProsePolicy> prose,
    Func<IReadOnlyList<PatchPreset>> presets)
{
    private PatchWorkbench? bench;
    private string? left;

    public async Task<ToolOutcome> Call(string tool, JsonElement arguments, CancellationToken cancel)
    {
        var bench = Bench();

        bench.Reopen();

        var outcome = await bench.InvokeAsync(tool, arguments, cancel).ConfigureAwait(true);

        if (outcome.Ok && bench.Edits > 0) editor.Apply(bench.Snapshot(), bench.Text);

        if (bench.HasProposal) bench.Settle();

        left = PatchIO.ToJson(editor.Current, modules);

        return outcome;
    }

    /// <summary>The workbench over the canvas as it stands.</summary>
    private PatchWorkbench Bench()
    {
        var now = PatchIO.ToJson(editor.Current, modules);

        if (bench is not null && now == left) return bench;

        var handles = bench?.Save().Handles;

        bench = new PatchWorkbench(
            modules,
            PatchIO.Read(now, modules).Patch,
            vision: true,
            hearing: Listener.None,
            samples: editor.Samples,
            pictures: editor.Pictures,
            prose: prose(),
            presets: presets(),
            text: editor.Text,
            live: true);

        if (handles is not null)
            bench.Restore(new WorkbenchState(now, now, handles, 0, 0) { Text = editor.Text });

        return bench;
    }
}
