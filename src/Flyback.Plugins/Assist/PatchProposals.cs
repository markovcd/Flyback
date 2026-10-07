using System.Text.Json;
using Flyback.Core.Compile;
using static Flyback.Plugins.Assist.ToolArguments;

namespace Flyback.Plugins.Assist;

/// <summary><c>propose</c>: offering the working patch to the person, once it is worth offering.</summary>
internal sealed class PatchProposals(WorkingPatch bench, PatchSenses senses, WorkbenchLimits limits)
{
    public async Task<ToolOutcome> ProposeAsync(JsonElement arguments, CancellationToken cancel)
    {
        if (!Text(arguments, "summary", out var summary))
            return ToolOutcome.Refused("'summary' is required: one line saying what this patch does.");

        // Both programs, because either may be the one that was built for. A
        // patch offered for its sound still has to have compiled its sound, and
        // the video pass never reaches a node only the ear does.
        var audio = bench.CompileForAudio();

        var errors = bench.CompileForVideo().Issues
            .Concat(audio.Issues)
            .Where(i => i.Severity == IssueSeverity.Error)
            .Select(i => i.Message)
            .Distinct()
            .ToArray();

        if (errors.Length > 0)
            return ToolOutcome.Refused(
                "this patch does not compile cleanly yet, so it is not worth proposing: "
                + string.Join(" | ", errors));

        // Warnings do not block, but an Output nothing reaches is not a patch:
        // nothing is watching and nothing is listening, so there is nothing to
        // offer whatever the modules add up to. The sink itself is always there,
        // so what has to be checked is whether anything arrives at it.
        if (!bench.Patch.Connections.Any(c => c.TargetNode == bench.Patch.Output.Id))
        {
            return ToolOutcome.Refused(
                "nothing is wired into the Output, so nothing this patch does comes out anywhere. "
                + $"Patch something into {bench.Handle(bench.Patch.Output)}'s 'color' or its 'left' before proposing.");
        }

        // A sound that is wired was meant to be heard, and a model with no ear
        // cannot find out that it is silent any other way.
        var (picture, sound) = bench.Patch.Reaches();
        var startsSilent = arguments.TryGetProperty("starts_silent", out var flag) && flag.ValueKind == JsonValueKind.True;

        if (sound && !startsSilent && await senses.SilentAsync(audio, cancel).ConfigureAwait(false))
        {
            return ToolOutcome.Refused(
                $"the sound is silence for its first {Number(limits.LatestTime)}s: nothing above -66 dBFS "
                + "comes out of 'left' or 'right' but the thump of a constant settling, if that. " + PatchSenses.SilenceCauses
                + (picture ? "" : " Nothing reaches 'color' either, so this patch draws nothing as well.")
                + " If it is meant to start silent and come in later, propose again with 'starts_silent' true.");
        }

        bench.Proposal = summary;
        return ToolOutcome.Fine($"proposed. {summary}");
    }
}
