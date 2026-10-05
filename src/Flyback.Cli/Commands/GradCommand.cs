using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Slopes;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// How much each knob moves one point of the picture, or one octave of the sound:
/// the slopes the patch's ops give, exact rather than measured.
/// </summary>
internal static class GradCommand
{
    private const int Columns = 160, Rows = 90, Rate = 96_000, Window = 8192;

    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var patch = new Argument<FileInfo?>("patch")
        {
            Description = "The patch: a document, a bundle, or one written as text. Left out, give --preset instead.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var preset = new Option<string>("--preset") { Description = "A shipped preset, by name, in place of a file." };

        var point = new Option<string>("--point")
        {
            Description = "Where in the picture, as x,y in the patch's coordinates: y from -1 at the bottom to 1 at the top, x across by the frame's shape.",
        };

        var band = new Option<double?>("--band") { Description = "Which octave of the sound, by a frequency in it, in place of --point." };

        var at = new Option<double>("--at")
        {
            Description = "Which second of the patch.",
            DefaultValueFactory = _ => 2d,
        };

        var top = new Option<int>("--top")
        {
            Description = "How many knobs to list, the ones that move it most first.",
            DefaultValueFactory = _ => 12,
        };

        var command = new Command(
            "grad",
            "Say how much each knob moves one point of the picture or one octave of the sound, per unit and over its whole travel.")
        {
            patch, preset, point, band, at, top, json,
        };

        command.SetAction((result, _) =>
        {
            plugins.Ready();

            var output = result.InvocationConfiguration.Output;
            var error = result.InvocationConfiguration.Error;

            Opened? opened = result.GetValue(preset) is { } shipped
                ? ShippedPresets.Open(plugins.Catalog, shipped, error)?.Opened
                : result.GetValue(patch) is { } file ? Patches.Open(file, error) : null;

            if (opened is not { } found)
            {
                if (result.GetValue(preset) is null && result.GetValue(patch) is null)
                    error.WriteLine($"{GlobalConstants.ApplicationName}: say what to read: a patch, or --preset and its name.");

                return Task.FromResult(Exit.Failed);
            }

            var seconds = result.GetValue(at);

            if (!double.IsFinite(seconds) || seconds < 0d || seconds > 600d)
            {
                error.WriteLine("--at is a second from 0 to 600.");
                return Task.FromResult(Exit.Failed);
            }

            if (result.GetValue(band) is { } hz)
                return Task.FromResult(Sound(found, hz, seconds, result.GetValue(top), result.GetValue(json), output, error));

            if (Point(result.GetValue(point)) is not { } xy)
            {
                error.WriteLine("Say where: --point x,y for the picture, or --band <hz> for the sound.");
                return Task.FromResult(Exit.Failed);
            }

            return Task.FromResult(Picture(found, xy, seconds, result.GetValue(top), result.GetValue(json), output));
        });

        return command;
    }

    private static int Picture(Opened opened, (double X, double Y) point, double seconds, int top, bool json, TextWriter output)
    {
        var sp = SlopeProgram.Compile(opened.Patch, sound: false, NodeCatalog.Current, opened.Samples, opened.Pictures);
        var (before, planes, t) = SlopeReadings.Settle(sp, seconds, Columns, Rows);
        var slopes = new Slopes(sp, [.. Enumerable.Range(0, sp.Knobs.Count)]);
        var cells = new float[sp.Program.PlaneCount];

        planes.AsSpan(SlopeReadings.Nearest(point.X, point.Y, Columns, Rows) * cells.Length, cells.Length).CopyTo(cells);
        slopes.Pixel(point.X, point.Y, t, (double)Columns / Rows, before, cells);

        var rows = Enumerable.Range(0, sp.Knobs.Count)
            .Select(k => (Knob: sp.Knobs[k], Slope: new[] { slopes.Output(0)[k], slopes.Output(1)[k], slopes.Output(2)[k] }))
            .Where(row => row.Slope.Any(s => s != 0d))
            .OrderByDescending(row => row.Slope.Max(Math.Abs) * row.Knob.Range)
            .Take(top)
            .ToArray();

        double[] color = [slopes.Value(0), slopes.Value(1), slopes.Value(2)];

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    point = new[] { point.X, point.Y },
                    seconds = t,
                    color,
                    knobs = rows.Select(row => new
                    {
                        knob = row.Knob.Label,
                        value = row.Knob.Value,
                        min = row.Knob.Min,
                        max = row.Knob.Max,
                        slope = row.Slope,
                        travel = row.Slope.Select(s => s * row.Knob.Range),
                    }),
                    still = sp.Knobs.Count - rows.Length,
                    issues = sp.Issues.Select(issue => issue.Message),
                },
                Writing.Json));

            return Exit.Ok;
        }

        output.WriteLine($"At ({Number(point.X)}, {Number(point.Y)}), {Number(t)} s, the color is {Number(color[0])} {Number(color[1])} {Number(color[2])}.");

        if (rows.Length == 0)
        {
            output.WriteLine($"None of its {sp.Knobs.Count} knobs moves it here.");
            return Exit.Ok;
        }

        output.WriteLine("Red, green and blue moved over each knob's whole travel, if it held its slope:");

        foreach (var (knob, slope) in rows)
            output.WriteLine($"  {knob.Label,-36} {string.Join("  ", slope.Select(s => Signed(s * knob.Range))),-30} at {Number(knob.Value)} of {Number(knob.Min)}..{Number(knob.Max)}");

        return Exit.Ok;
    }

    private static int Sound(Opened opened, double hz, double seconds, int top, bool json, TextWriter output, TextWriter error)
    {
        var sp = SlopeProgram.Compile(opened.Patch, sound: true, NodeCatalog.Current, opened.Samples, opened.Pictures);
        var steps = Math.Max(Window, (long)Math.Round(seconds * Rate));
        var watch = Stopwatch.StartNew();
        var (levels, slopes) = SlopeReadings.Bands(sp, [.. Enumerable.Range(0, sp.Knobs.Count)], Rate, steps, Window);
        var band = SlopeReadings.Octave(hz);
        var middle = SlopeReadings.Octaves[band];

        var rows = Enumerable.Range(0, sp.Knobs.Count)
            .Select(k => (Knob: sp.Knobs[k], Slope: slopes[k, band]))
            .Where(row => row.Slope != 0d && double.IsFinite(row.Slope))
            .OrderByDescending(row => Math.Abs(row.Slope * row.Knob.Range))
            .Take(top)
            .ToArray();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    band = middle,
                    seconds = steps / (double)Rate,
                    level = levels[band],
                    knobs = rows.Select(row => new
                    {
                        knob = row.Knob.Label,
                        value = row.Knob.Value,
                        min = row.Knob.Min,
                        max = row.Knob.Max,
                        slope = row.Slope,
                        travel = row.Slope * row.Knob.Range,
                    }),
                    still = sp.Knobs.Count - rows.Length,
                    issues = sp.Issues.Select(issue => issue.Message),
                },
                Writing.Json));

            return Exit.Ok;
        }

        output.WriteLine($"The {Number(middle)} Hz octave of the left channel, the {Window} samples to {Number(steps / (double)Rate)} s, is at {levels[band]:0.0} dB.");

        if (rows.Length == 0)
        {
            output.WriteLine($"None of its {sp.Knobs.Count} knobs moves it.");
            return Exit.Ok;
        }

        output.WriteLine("Decibels per unit of each knob, and over its whole travel if it held that slope:");

        foreach (var (knob, slope) in rows)
            output.WriteLine($"  {knob.Label,-36} {Signed(slope),10} dB  {Signed(slope * knob.Range),10} dB  at {Number(knob.Value)} of {Number(knob.Min)}..{Number(knob.Max)}");

        error.WriteLine($"Took {watch.Elapsed.TotalSeconds:0.0} s for {sp.Knobs.Count} knobs.");
        return Exit.Ok;
    }

    private static (double X, double Y)? Point(string? text)
    {
        var parts = text?.Split(',') ?? [];

        if (parts.Length != 2) return null;

        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && double.IsFinite(x) && double.IsFinite(y) && Math.Abs(x) <= 16d && Math.Abs(y) <= 16d
            ? (x, y)
            : null;
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Signed(double value) => value.ToString("+0.000;-0.000;0", CultureInfo.InvariantCulture);
}
