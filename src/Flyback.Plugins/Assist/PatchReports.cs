using System.Text;
using Flyback.Core.Compile;
using Flyback.Engine.Language;

namespace Flyback.Plugins.Assist;

/// <summary>
/// What the model is told of the working patch: the whole of it on
/// <c>describe_patch</c>, and what the compiler says of it after each edit.
/// </summary>
internal sealed class PatchReports(WorkingPatch bench)
{
    /// <summary>What <see cref="Issues"/> last said, so the next report says only what is new.</summary>
    private readonly HashSet<string> told = new(StringComparer.Ordinal);

    public string Describe()
    {
        var working = bench.Patch;
        var modules = bench.Modules;

        if (working.Nodes.Count == 0) return "the patch is empty. " + Issues();

        var text = new StringBuilder();

        // The patch in the same language write_patch takes, and under the same
        // handles the editing tools answer to — so what is read here can be
        // pointed at by set_knobs and connect, and written back wholesale
        // without translating between two notations.
        text.AppendLine(PatchPrinter.Print(working, modules, bench.Handles));

        // A module this build cannot place has no syntax, so it is said in
        // prose rather than left out of the picture entirely.
        foreach (var node in working.Nodes.Where(n => modules.Get(n.TypeId) is null))
            text.Append(bench.Handle(node)).Append(" = ").Append(node.TypeId).AppendLine(" (unknown module)");

        if (Normalled() is { Length: > 0 } carried)
            text.Append("carrying a signal with no wire: ").AppendLine(carried);

        text.Append(working.Nodes.Count).Append(" modules, ")
            .Append(working.Connections.Count).AppendLine(" wires.");

        var video = bench.CompileForVideo();
        var audio = bench.CompileForAudio();

        text.Append("video: ").Append(video.Program.Ops.Length).Append(" ops");
        if (video.Issues.Count > 0)
            text.Append(", ").Append(string.Join(" | ", video.Issues.Select(Named)));

        text.Append(". audio: ").Append(audio.Program.Ops.Length).Append(" ops");
        if (audio.Issues.Count > 0)
            text.Append(", ").Append(string.Join(" | ", audio.Issues.Select(Named)));

        return text.Append('.').ToString();
    }

    /// <summary>
    /// What the compiler says of the patch after an edit: in full where it is new,
    /// and counted where an earlier edit already said it.
    /// </summary>
    public string Issues()
    {
        // Both programs, for the reason 'propose' asks after both: the video
        // pass stops at the first line when there is no screen, so a patch built
        // for the speakers was reported flawless whatever was wrong with it.
        // That is precisely what a silent patch looks like from this side — an
        // assistant told "No issues." after every edit has no way left to find
        // out, because it cannot hear the thing either.
        var issues = bench.CompileForVideo().Issues
            .Concat(bench.CompileForAudio().Issues)
            .ToArray();

        // Distinct, because a module both sinks reach is compiled twice and
        // would otherwise be complained about twice.
        var faults = issues.Where(i => i.Severity == IssueSeverity.Error)
            .Select(Named).Distinct().ToArray();

        var notes = issues.Where(i => i.Severity != IssueSeverity.Error)
            .Select(Named).Distinct().ToArray();

        var said = told.ToHashSet(StringComparer.Ordinal);

        told.Clear();
        told.UnionWith(faults);
        told.UnionWith(notes);

        if (issues.Length == 0) return "No issues.";

        var newFaults = faults.Where(fault => !said.Contains(fault)).ToArray();
        var newNotes = notes.Where(note => !said.Contains(note)).ToArray();

        var text = new StringBuilder();

        if (newFaults.Length > 0) text.Append("Issues: ").Append(string.Join(" | ", newFaults)).Append('.');

        // Kept apart from the faults rather than listed with them. A warning is
        // something to know, not something to clear — an assistant that cannot
        // tell the two apart will spend the run clearing them instead of
        // finishing, which is what a run that never proposed anything looks
        // like from out here.
        if (newNotes.Length > 0)
        {
            if (text.Length > 0) text.Append(' ');
            text.Append("Worth knowing: ").Append(string.Join(" | ", newNotes)).Append('.');
        }

        var oldFaults = faults.Length - newFaults.Length;
        var oldNotes = notes.Length - newNotes.Length;

        if (oldFaults + oldNotes > 0)
        {
            if (text.Length > 0) text.Append(' ');

            text.Append(text.Length == 0 ? "Nothing new; still standing as said before: " : "Still standing as said before: ")
                .Append(Count(oldFaults, "issue")).Append(oldFaults > 0 && oldNotes > 0 ? " and " : "")
                .Append(Count(oldNotes, "warning")).Append('.');
        }

        return text.ToString();

        static string Count(int count, string noun) =>
            count == 0 ? string.Empty : count == 1 ? $"1 {noun}" : $"{count} {noun}s";
    }

    /// <summary>
    /// The sockets that are carrying something with nothing patched into them, named
    /// so that a reader knows what is driving them.
    /// </summary>
    /// <remarks>
    /// The language has no syntax for this, and correctly — leaving a socket out is
    /// how a patch says "let the normal drive it" (ADR-0050) — but an assistant that
    /// could not see it would go on wiring a clock into every oscillator it placed.
    /// </remarks>
    private string Normalled()
    {
        var carried = new List<string>();

        foreach (var node in bench.Patch.Nodes)
        {
            if (bench.Modules.Get(node.TypeId) is not { } def) continue;

            for (var port = 0; port < def.Inputs.Count; port++)
            {
                if (bench.Patch.IncomingTo(node.Id, port) is not null) continue;
                if (bench.Modules.Normalled(def.Inputs[port]) is not { } driver) continue;

                carried.Add($"{bench.Handle(node)}.{def.Inputs[port].Name.Replace(' ', '_')} <- {driver}");
            }
        }

        return string.Join(", ", carried);
    }

    /// <summary>An issue under the handle of the module it is about, which the compiler's own wording cannot name.</summary>
    private string Named(CompileIssue issue) =>
        bench.Patch.Find(issue.NodeId ?? Guid.Empty) is { } node ? $"{bench.Handle(node)}: {issue.Message}" : issue.Message;
}
