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
/// handles the client chose still name their modules. A canvas edited by hand in between is
/// taken up afresh, keeping the handles of the modules still there.
/// </remarks>
internal sealed class DrivenBench(
    IAssistantEditor editor,
    ModuleCatalog modules,
    Func<ProsePolicy> prose,
    Func<IReadOnlyList<PatchPreset>> presets)
{
    private PatchWorkbench? bench;
    private string? start;
    private string? left;

    public async Task<ToolOutcome> Call(string tool, JsonElement arguments, CancellationToken cancel)
    {
        var bench = Bench();

        bench.Reopen();

        var outcome = await bench.InvokeAsync(tool, arguments, cancel).ConfigureAwait(true);

        if (outcome.Ok && bench.Edits > 0) editor.Apply(bench.Snapshot());

        left = PatchIO.ToJson(editor.Current, modules);

        return outcome;
    }

    /// <summary>The workbench over the canvas as it stands.</summary>
    private PatchWorkbench Bench()
    {
        var now = PatchIO.ToJson(editor.Current, modules);

        if (bench is not null && now == left) return bench;

        var handles = bench?.Save().Handles;

        start ??= now;

        bench = new PatchWorkbench(
            modules,
            PatchIO.Read(start, modules).Patch,
            vision: true,
            hearing: Listener.None,
            samples: editor.Samples,
            pictures: editor.Pictures,
            prose: prose(),
            presets: presets());

        bench.Restore(new WorkbenchState(start, now, handles ?? new Dictionary<string, Guid>(), 0, 0));

        return bench;
    }
}
