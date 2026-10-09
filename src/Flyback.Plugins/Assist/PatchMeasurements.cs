using System.Text;
using System.Text.Json;
using Flyback.Engine.Measure;

namespace Flyback.Plugins.Assist;

/// <summary>
/// <c>measure</c>: what each output carries, as numbers, without drawing or playing anything.
/// </summary>
internal sealed class PatchMeasurements(WorkingPatch bench, WorkbenchLimits limits)
{
    /// <summary>The longest window a measurement runs, long enough to count a slow LFO's turns.</summary>
    public const double LongestMeasure = 60d;

    /// <summary>
    /// Runs the patch offline and says what each output of the named modules carried,
    /// to the speakers and to the screen apart.
    /// </summary>
    /// <remarks>On a pool thread, for the reason <see cref="PatchSenses.RenderAsync"/> gives.</remarks>
    public Task<ToolOutcome> MeasureAsync(JsonElement arguments, CancellationToken cancel)
    {
        var chosen = new List<Guid>();

        if (ToolFields.Handles.List(arguments, out var handles))
        {
            foreach (var handle in handles.EnumerateArray())
            {
                if (!bench.Node(handle.GetString() ?? string.Empty, out var node, out _, out var refusal))
                    return Task.FromResult(ToolOutcome.Refused(refusal));

                chosen.Add(node.Id);
            }
        }

        var seconds = ToolFields.Seconds.Number(arguments, out var length) ? Math.Clamp(length, 0.25d, LongestMeasure) : 2d;

        var from = ToolFields.From.Number(arguments, out var start) ? Math.Clamp(start, 0d, limits.LatestStart) : 0d;

        var patch = bench.Patch;

        return Task.Run(
            () =>
            {
                var report = Measurements.Take(
                    patch,
                    new MeasureOptions(seconds, from, Modules: chosen.Count == 0 ? null : chosen),
                    bench.Modules,
                    bench.Samples,
                    bench.Pictures,
                    cancel: cancel);

                var text = new StringBuilder(
                    $"Measured {MeasurementWords.Number(seconds)}s from {MeasurementWords.Number(from)}s, with nothing played in. "
                    + "The sound runs over time with memory; the picture runs over x, y and t without, "
                    + "so the two can differ.");

                if (report.Measurements.Count == 0) text.Append("\nNothing measured has an output.");

                foreach (var m in report.Measurements)
                {
                    text.Append('\n').Append(bench.Handle(patch.Find(m.Node))).Append('.').Append(m.Socket);
                    if (m.Differs) text.Append("  (sound and picture differ)");

                    foreach (var line in MeasurementWords.Half("sound", m.Sound, seconds)) text.Append("\n  ").Append(line);
                    foreach (var line in MeasurementWords.Half("picture", m.Picture, seconds)) text.Append("\n  ").Append(line);
                }

                return ToolOutcome.Fine(text.ToString());
            },
            cancel);
    }
}
