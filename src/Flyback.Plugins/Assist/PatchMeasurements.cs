using System.Text;
using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
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
        var chosen = new List<(Guid Node, int? Port)>();

        if (ToolFields.Handles.List(arguments, out var handles))
        {
            foreach (var handle in handles.EnumerateArray())
            {
                if (Chosen(handle.GetString() ?? string.Empty, out var refusal) is not { } one)
                    return Task.FromResult(ToolOutcome.Refused(refusal));

                if (!chosen.Contains(one)) chosen.Add(one);
            }
        }

        var asked = ToolFields.Seconds.Number(arguments, out var length) ? Math.Clamp(length, 0.25d, LongestMeasure) : 2d;
        var outputs = Outputs(chosen);
        var seconds = Math.Max(0.25d, Math.Min(asked, limits.MeasuredOutputSeconds / Math.Max(1, outputs)));

        var from = ToolFields.From.Number(arguments, out var start) ? Math.Clamp(start, 0d, limits.LatestStart) : 0d;

        var patch = bench.Patch;

        return Task.Run(
            () =>
            {
                var report = Measurements.Take(
                    patch,
                    new MeasureOptions(seconds, from, Modules: chosen.Count == 0 ? null : [.. chosen.Select(c => c.Node).Distinct()]),
                    bench.Modules,
                    bench.Samples,
                    bench.Pictures,
                    cancel: cancel);

                var text = new StringBuilder(
                    $"Measured {MeasurementWords.Number(seconds)}s from {MeasurementWords.Number(from)}s, with nothing played in. "
                    + "The sound runs over time with memory; the picture runs over x, y and t without, "
                    + "so the two can differ.");

                if (seconds < asked)
                {
                    text.Append(
                        $" Shortened from the {MeasurementWords.Number(asked)}s asked: watching {outputs} outputs at once "
                        + $"is capped at {MeasurementWords.Number(limits.MeasuredOutputSeconds)} output-seconds, "
                        + "so name the modules or outputs you need for a longer window.");
                }

                var kept = report.Measurements
                    .Where(m => chosen.Count == 0 || chosen.Any(c => c.Node == m.Node && (c.Port is null || c.Port == m.Port)))
                    .ToList();

                if (kept.Count == 0) text.Append("\nNothing measured has an output.");

                foreach (var m in kept)
                {
                    text.Append('\n').Append(bench.Handle(patch.Find(m.Node))).Append('.').Append(m.Socket.Replace(' ', '_'));
                    if (m.Differs) text.Append("  (sound and picture differ)");

                    foreach (var line in MeasurementWords.Half("sound", m.Sound, seconds, display: m.Display)) text.Append("\n  ").Append(line);
                    foreach (var line in MeasurementWords.Half("picture", m.Picture, seconds, display: m.Display)) text.Append("\n  ").Append(line);
                }

                return ToolOutcome.Fine(text.ToString());
            },
            cancel);
    }

    /// <summary>How many outputs a measurement of <paramref name="chosen"/> watches, or of every module where none are.</summary>
    private int Outputs(List<(Guid Node, int? Port)> chosen)
    {
        int Of(NodeInstance? node) => node is not null && bench.Modules.Get(node.TypeId) is { } def ? def.Outputs.Count : 0;

        return chosen.Count == 0
            ? bench.Patch.Nodes.Sum(Of)
            : chosen.Sum(one => one.Port is null ? Of(bench.Patch.Find(one.Node)) : 1);
    }

    /// <summary>
    /// What one name in <c>handles</c> asks for: a module's every output, one output as
    /// <c>module.output</c>, or the output a <c>let</c> name stands for.
    /// </summary>
    private (Guid Node, int? Port)? Chosen(string name, out string refusal)
    {
        if (bench.Node(name, out var whole, out _, out refusal)) return (whole.Id, null);

        if (bench.NamedOutput(name, out var aliased, out var bound))
        {
            refusal = string.Empty;
            return (aliased.Id, bound);
        }

        var dot = name.LastIndexOf('.');

        if (dot <= 0) return null;

        if (!bench.Node(name[..dot], out var node, out var def, out var missing))
        {
            refusal = missing;
            return null;
        }

        var socket = name[(dot + 1)..];

        if (OutputNames.Own(node, def, socket) is >= 0 and var part)
        {
            refusal = string.Empty;
            return (node.Id, part);
        }

        for (var port = 0; port < def.Outputs.Count; port++)
        {
            if (SocketNames.Same(def.Outputs[port].Name, socket))
            {
                refusal = string.Empty;
                return (node.Id, port);
            }
        }

        refusal = def.Outputs.Count == 0
            ? $"'{name[..dot]}' has no outputs."
            : $"'{name[..dot]}' has no output called '{socket}'. It has {string.Join(", ", def.Outputs.Select(SocketNames.Written))}.";

        return null;
    }
}
