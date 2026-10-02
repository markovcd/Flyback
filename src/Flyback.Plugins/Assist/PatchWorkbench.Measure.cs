using System.Text;
using System.Text.Json;
using Flyback.Engine.Measure;

namespace Flyback.Plugins.Assist;

/// <summary>
/// <c>measure</c>: what each output carries, as numbers, without drawing or playing anything.
/// </summary>
public sealed partial class PatchWorkbench
{
    /// <summary>The longest window a measurement runs, long enough to count a slow LFO's turns.</summary>
    private const double LongestMeasure = 10d;

    /// <summary>
    /// Runs the patch offline and says what each output of the named modules carried,
    /// to the speakers and to the screen apart.
    /// </summary>
    /// <remarks>On a pool thread, for the reason <see cref="RenderAsync"/> gives.</remarks>
    private Task<ToolOutcome> MeasureAsync(JsonElement arguments, CancellationToken cancel)
    {
        var chosen = new List<Guid>();

        if (arguments.TryGetProperty("handles", out var handles) && handles.ValueKind == JsonValueKind.Array)
        {
            foreach (var handle in handles.EnumerateArray())
            {
                if (!Node(handle.GetString() ?? string.Empty, out var node, out _, out var refusal))
                    return Task.FromResult(ToolOutcome.Refused(refusal));

                chosen.Add(node.Id);
            }
        }

        var seconds = arguments.TryGetProperty("seconds", out var length) && length.ValueKind == JsonValueKind.Number
            ? Math.Clamp(length.GetDouble(), 0.25d, LongestMeasure)
            : 2d;

        var patch = working;

        return Task.Run(
            () =>
            {
                var report = Measurements.Take(
                    patch,
                    new MeasureOptions(seconds, Modules: chosen.Count == 0 ? null : chosen),
                    modules,
                    samples,
                    pictures,
                    cancel: cancel);

                var text = new StringBuilder(
                    $"Measured {MeasurementWords.Number(seconds)}s from 0, with nothing played in. "
                    + "The sound runs over time with memory; the picture runs over x, y and t without, "
                    + "so the two can differ.");

                if (report.Measurements.Count == 0) text.Append("\nNothing measured has an output.");

                foreach (var m in report.Measurements)
                {
                    text.Append('\n').Append(Handle(patch.Find(m.Node))).Append('.').Append(m.Socket);
                    if (m.Differs) text.Append("  (sound and picture differ)");

                    foreach (var line in MeasurementWords.Half("sound", m.Sound, seconds)) text.Append("\n  ").Append(line);
                    foreach (var line in MeasurementWords.Half("picture", m.Picture, seconds)) text.Append("\n  ").Append(line);
                }

                return ToolOutcome.Fine(text.ToString());
            },
            cancel);
    }
}
