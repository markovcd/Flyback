using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Slopes;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// The grab-the-output spike on a real patch: goals on the picture and the sound at a few
/// moments, met by turning the panel knobs along their slopes.
/// </summary>
internal static class GrabSpikeCommand
{
    private const int Columns = 96, Rows = 54, Lead = 30, Fps = 30, Rate = 48_000, Window = 32_768;

    public static Command Build(PluginRegistry plugins)
    {
        var patch = new Argument<FileInfo>("patch");
        var at = new Option<double[]>("--at") { AllowMultipleArgumentsPerToken = true, DefaultValueFactory = _ => [30d, 60d, 90d] };
        var survey = new Option<bool>("--survey");
        var warmer = new Option<double>("--warmer") { Description = "Move the region's red less blue by this much." };
        var brighter = new Option<double>("--brighter") { Description = "Move the region's light by this much." };
        var band = new Option<double?>("--band") { Description = "The octave to move, by a frequency in it." };
        var db = new Option<double>("--db") { Description = "How far to move that octave." };
        var keepBands = new Option<double[]>("--keep-bands") { AllowMultipleArgumentsPerToken = true, DefaultValueFactory = _ => [] };
        var keepPicture = new Option<bool>("--keep-picture") { Description = "Hold the region's light and warmth where they are." };
        var sparse = new Option<bool>("--sparse") { Description = "Solve again with only the knobs that carried the first answer." };
        var exclude = new Option<string[]>("--exclude") { AllowMultipleArgumentsPerToken = true, DefaultValueFactory = _ => ["Tempo"] };

        var command = new Command("spike-grab", "Spike: meet goals on the picture and the sound by turning the panel knobs.") { Hidden = true };

        foreach (var symbol in new Symbol[] { patch, at, survey, warmer, brighter, band, db, keepBands, keepPicture, exclude, sparse })
        {
            if (symbol is Argument argument) command.Add(argument);
            else command.Add((Option)symbol);
        }

        command.SetAction((result, _) =>
        {
            plugins.Ready();

            var output = result.InvocationConfiguration.Output;
            var error = result.InvocationConfiguration.Error;

            if (Patches.Open(result.GetRequiredValue(patch), error) is not { } opened) return Task.FromResult(Exit.Failed);

            var grab = new Grab(opened.Patch, opened.Samples, opened.Pictures, result.GetValue(at)!, result.GetValue(exclude)!, output);

            if (result.GetValue(survey))
            {
                grab.Survey();
                return Task.FromResult(Exit.Ok);
            }

            grab.Solve(
                result.GetValue(warmer),
                result.GetValue(brighter),
                result.GetValue(keepPicture),
                result.GetValue(band),
                result.GetValue(db),
                result.GetValue(keepBands)!,
                result.GetValue(sparse));

            return Task.FromResult(Exit.Ok);
        });

        return command;
    }

    private sealed class Grab
    {
        private readonly SlopeProgram seen;
        private readonly SlopeProgram heard;
        private readonly string[] keys;
        private readonly string[] labels;
        private readonly int[] seenKnob;
        private readonly int[] heardKnob;
        private readonly double[] rest;
        private readonly double[] moments;
        private readonly Settled[] settled;
        private readonly TextWriter output;
        private readonly bool[] inRegion = new bool[Columns * Rows];
        private readonly int regionPixels;

        public Grab(Patch patch, Core.Compile.ISampleLibrary samples, Core.Compile.IImageLibrary pictures, double[] at, string[] exclude, TextWriter output)
        {
            this.output = output;
            moments = at;
            seen = SlopeProgram.Compile(patch, sound: false, NodeCatalog.Current, samples, pictures);
            heard = SlopeProgram.Compile(patch, sound: true, NodeCatalog.Current, samples, pictures);

            var panel = seen.Knobs.Concat(heard.Knobs)
                .Where(knob => knob.Port == -1 && !exclude.Contains(knob.Label["panel.".Length..]))
                .DistinctBy(knob => knob.Key)
                .ToArray();

            keys = [.. panel.Select(knob => knob.Key)];
            labels = [.. panel.Select(knob => knob.Label["panel.".Length..])];
            rest = [.. panel.Select(knob => (double)knob.Value)];
            seenKnob = [.. keys.Select(key => IndexOf(seen, key))];
            heardKnob = [.. keys.Select(key => IndexOf(heard, key))];

            for (var p = 0; p < inRegion.Length; p++)
            {
                var (x, y) = SlopeReadings.At(p, Columns, Rows);
                inRegion[p] = Math.Abs(x) <= 0.9d && Math.Abs(y) <= 0.9d;
                if (inRegion[p]) regionPixels++;
            }

            var watch = Stopwatch.StartNew();
            settled = [.. moments.Select(Settle)];
            output.WriteLine($"{keys.Length} panel knobs, {seenKnob.Count(k => k >= 0)} in the picture, {heardKnob.Count(k => k >= 0)} in the sound; settled {moments.Length} moments in {watch.Elapsed.TotalSeconds:0} s");
        }

        private static int IndexOf(SlopeProgram sp, string key)
        {
            for (var k = 0; k < sp.Knobs.Count; k++)
                if (sp.Knobs[k].Key == key) return k;

            return -1;
        }

        private sealed record Settled(double Seconds, int FirstFrame, float[] Before, float[] Planes);

        /// <summary>Runs the picture with the knobs at rest up to <see cref="Lead"/> frames before the moment.</summary>
        private Settled Settle(double seconds)
        {
            var program = seen.Program;
            var pixels = Columns * Rows;
            var aspect = (double)Columns / Rows;
            var planes = new float[pixels * program.PlaneCount];
            var previous = new float[pixels * 3];
            var current = new float[pixels * 3];
            var registers = program.AllocateRegisters();
            var first = Math.Max(0, (int)Math.Round(seconds * Fps) - Lead);

            for (var frame = 0; frame < first; frame++)
            {
                var before = new FeedbackFrame(frame == 0 ? null : previous, Columns, Rows);

                for (var p = 0; p < pixels; p++)
                {
                    var (x, y) = SlopeReadings.At(p, Columns, Rows);
                    program.Evaluate(x, y, frame / (double)Fps, registers, before, null, aspect, seen.Live, planes.AsSpan(p * program.PlaneCount, program.PlaneCount));
                    for (var c = 0; c < 3; c++) current[p * 3 + c] = (float)Clamp01(registers[program.OutputBase + c]);
                }

                (previous, current) = (current, previous);
            }

            return new Settled(seconds, first, first == 0 ? [] : previous, planes);
        }

        private void Turn(double[] z)
        {
            for (var i = 0; i < keys.Length; i++)
            {
                if (seenKnob[i] >= 0) seen.Turn(seenKnob[i], (float)z[i]);
                if (heardKnob[i] >= 0) heard.Turn(heardKnob[i], (float)z[i]);
            }
        }

        /// <summary>
        /// The region's light and warmth after <see cref="Lead"/> frames with the knobs at
        /// <paramref name="z"/>, and with <paramref name="slopes"/> how the last frame moves per knob.
        /// </summary>
        private (double Light, double Warmth) Picture(Settled moment, double[] z, double[]? lightSlope = null, double[]? warmthSlope = null)
        {
            Turn(z);

            var program = seen.Program;
            var pixels = Columns * Rows;
            var aspect = (double)Columns / Rows;
            var planes = (float[])moment.Planes.Clone();
            var previous = moment.Before.Length == 0 ? new float[pixels * 3] : (float[])moment.Before.Clone();
            var current = new float[pixels * 3];
            var registers = program.AllocateRegisters();
            var lanes = Enumerable.Range(0, keys.Length).Where(i => seenKnob[i] >= 0).ToArray();
            var slopes = lightSlope is null ? null : new Slopes(seen, [.. lanes.Select(i => seenKnob[i])], 2d / Rows, dither: true);
            double light = 0d, warmth = 0d;

            for (var f = 0; f < Lead; f++)
            {
                var frame = moment.FirstFrame + f;
                var t = frame / (double)Fps;
                var last = f == Lead - 1;
                var before = new FeedbackFrame(frame == 0 ? null : previous, Columns, Rows);

                for (var p = 0; p < pixels; p++)
                {
                    var (x, y) = SlopeReadings.At(p, Columns, Rows);
                    var cells = planes.AsSpan(p * program.PlaneCount, program.PlaneCount);
                    double r, g, b;

                    if (last && slopes is not null && inRegion[p])
                    {
                        slopes.Pixel(x, y, t, aspect, before, cells);
                        (r, g, b) = (slopes.Value(0), slopes.Value(1), slopes.Value(2));

                        for (var j = 0; j < lanes.Length; j++)
                        {
                            var dr = Inside(r) ? slopes.Output(0)[j] : 0d;
                            var dg = Inside(g) ? slopes.Output(1)[j] : 0d;
                            var db = Inside(b) ? slopes.Output(2)[j] : 0d;

                            lightSlope![lanes[j]] += Light(dr, dg, db) / regionPixels;
                            warmthSlope![lanes[j]] += (dr - db) / regionPixels;
                        }
                    }
                    else
                    {
                        program.Evaluate(x, y, t, registers, before, null, aspect, seen.Live, cells);
                        (r, g, b) = (registers[program.OutputBase], registers[program.OutputBase + 1], registers[program.OutputBase + 2]);
                    }

                    current[p * 3] = (float)Clamp01(r);
                    current[p * 3 + 1] = (float)Clamp01(g);
                    current[p * 3 + 2] = (float)Clamp01(b);

                    if (last && inRegion[p])
                    {
                        light += Light(Clamp01(r), Clamp01(g), Clamp01(b)) / regionPixels;
                        warmth += (Clamp01(r) - Clamp01(b)) / regionPixels;
                    }
                }

                (previous, current) = (current, previous);
            }

            return (light, warmth);
        }

        /// <summary>The left channel's octave levels in the window ending at the moment, from a second before it with memory empty.</summary>
        private double[] Sound(double seconds, double[] z, int[]? bands = null, double[,]? slope = null)
        {
            Turn(z);

            var steps = (long)Math.Round(seconds * Rate);
            var first = Math.Max(0, steps - 2 * Rate);

            if (slope is null) return SlopeReadings.Levels(heard, Rate, steps, Window, first);

            var lanes = Enumerable.Range(0, keys.Length).Where(i => heardKnob[i] >= 0).ToArray();
            var (levels, slopes) = SlopeReadings.Bands(heard, [.. lanes.Select(i => heardKnob[i])], Rate, steps, Window, first, dither: true);

            for (var row = 0; row < bands!.Length; row++)
                for (var j = 0; j < lanes.Length; j++)
                    slope[row, lanes[j]] = slopes[j, bands[row]];

            return levels;
        }

        public void Survey()
        {
            var z = (double[])rest.Clone();
            int[] watched = [SlopeReadings.Octave(63), SlopeReadings.Octave(125), SlopeReadings.Octave(500), SlopeReadings.Octave(2000), SlopeReadings.Octave(8000)];

            foreach (var moment in settled)
            {
                var lightSlope = new double[keys.Length];
                var warmthSlope = new double[keys.Length];
                var (light, warmth) = Picture(moment, z, lightSlope, warmthSlope);
                var bandSlope = new double[watched.Length, keys.Length];
                var levels = Sound(moment.Seconds, z, watched, bandSlope);

                output.WriteLine();
                output.WriteLine($"At {moment.Seconds} s: tunnel light {light:0.000}, warmth {warmth:+0.000;-0.000}; octaves {string.Join("  ", SlopeReadings.Octaves.Select((hz, i) => $"{hz}:{levels[i]:0.0}"))}");
                output.WriteLine($"  {"knob",-12} {"at",5}  {"light",8} {"(30 fr)",8} {"warmth",8} {"(30 fr)",8}   {string.Join(" ", watched.Select(b => $"{SlopeReadings.Octaves[b],7}"))}  (per full turn)");

                for (var i = 0; i < keys.Length; i++)
                {
                    var (lightUp, warmthUp) = seenKnob[i] >= 0 ? Picture(moment, With(z, i, +0.02)) : (light, warmth);
                    var (lightDown, warmthDown) = seenKnob[i] >= 0 ? Picture(moment, With(z, i, -0.02)) : (light, warmth);
                    var span = With(z, i, +0.02)[i] - With(z, i, -0.02)[i];
                    var fdLight = span == 0d ? 0d : (lightUp - lightDown) / span;
                    var fdWarmth = span == 0d ? 0d : (warmthUp - warmthDown) / span;

                    output.WriteLine(
                        $"  {labels[i],-12} {z[i],5:0.00}  {lightSlope[i],8:+0.000;-0.000} {fdLight,8:+0.000;-0.000} {warmthSlope[i],8:+0.000;-0.000} {fdWarmth,8:+0.000;-0.000}   "
                        + string.Join(" ", Enumerable.Range(0, watched.Length).Select(row => $"{bandSlope[row, i],7:+0.0;-0.0}")));
                }

                Turn(z);
            }
        }

        private static double[] With(double[] z, int i, double by)
        {
            var moved = (double[])z.Clone();
            moved[i] = Math.Clamp(moved[i] + by, 0d, 1d);
            return moved;
        }

        private sealed record Row(string Name, bool Picture, int Moment, int What, double Target, double Tolerance);

        public void Solve(double warmer, double brighter, bool keepPicture, double? band, double db, double[] keepBands, bool sparse)
        {
            var z = (double[])rest.Clone();
            var rows = new List<Row>();
            var watch = Stopwatch.StartNew();
            var start = Measure(z, out var startLevels);

            for (var m = 0; m < settled.Length; m++)
            {
                var (light, warmth) = start[m];

                if (warmer != 0d || keepPicture) rows.Add(new Row($"warmth@{moments[m]}", true, m, 1, warmth + warmer, warmer != 0d ? 0.005d : 0.01d));
                if (brighter != 0d || keepPicture || warmer != 0d) rows.Add(new Row($"light@{moments[m]}", true, m, 0, light + brighter, brighter != 0d ? 0.005d : 0.01d));

                if (band is { } hz)
                {
                    var b = SlopeReadings.Octave(hz);
                    rows.Add(new Row($"{SlopeReadings.Octaves[b]}Hz@{moments[m]}", false, m, b, startLevels[m][b] + db, 0.5d));
                }

                foreach (var kept in keepBands)
                {
                    var b = SlopeReadings.Octave(kept);
                    rows.Add(new Row($"{SlopeReadings.Octaves[b]}Hz@{moments[m]}", false, m, b, startLevels[m][b], 0.75d));
                }
            }

            output.WriteLine($"{rows.Count} rows: {string.Join(", ", rows.Select(r => $"{r.Name} -> {r.Target:0.000}"))}");

            var current = Values(rows, start, startLevels);
            var error = Error(rows, current);

            (z, current, error) = Run(rows, [.. keys.Select(_ => true)], z, current, error);

            if (sparse)
            {
                var largest = Enumerable.Range(0, keys.Length).Max(k => Math.Abs(z[k] - rest[k]));
                var carried = Enumerable.Range(0, keys.Length).Select(k => Math.Abs(z[k] - rest[k]) >= 0.25d * largest).ToArray();

                output.WriteLine($"Again with only {string.Join(", ", Enumerable.Range(0, keys.Length).Where(k => carried[k]).Select(k => labels[k]))}, which carried it.");

                z = (double[])rest.Clone();
                current = Values(rows, start, startLevels);
                error = Error(rows, current);

                (z, current, error) = Run(rows, carried, z, current, error);
            }

            Report(rows, z, current, error, start, startLevels, watch);
        }

        private (double[] Z, double[] Current, double Error) Run(List<Row> rows, bool[] allowed, double[] z, double[] current, double error)
        {
            const double widest = 0.1d;

            // Each knob's own trust region: narrowed when it carried a step that failed, so the
            // next solve leans on knobs whose slope held.
            var caps = allowed.Select(free => free ? widest : 0d).ToArray();
            var sound = rows.Any(row => !row.Picture);

            for (var iteration = 1; iteration <= 12 && error > 1d; iteration++)
            {
                var jacobian = Jacobian(rows, z);
                var residual = rows.Select((row, i) => row.Target - current[i]).ToArray();
                var taken = false;

                for (var attempt = 0; attempt < 8 && !taken; attempt++)
                {
                    var weight = caps.Select(cap => cap / widest).ToArray();
                    double[] dz = [];

                    // Knobs at an end of their travel that the step would push past it are held there.
                    for (var pass = 0; pass < 4; pass++)
                    {
                        dz = MinNorm(jacobian, residual, rows, weight);

                        var stuck = Enumerable.Range(0, keys.Length).Where(k => weight[k] > 0d && (z[k] <= 0d && dz[k] < 0d || z[k] >= 1d && dz[k] > 0d)).ToArray();
                        if (stuck.Length == 0) break;
                        foreach (var k in stuck) weight[k] = 0d;
                    }

                    for (var k = 0; k < dz.Length; k++) dz[k] = Math.Clamp(dz[k], -caps[k], caps[k]);

                    if (dz.All(d => d == 0d)) break;

                    var tried = z.Select((value, k) => Math.Clamp(value + dz[k], 0d, 1d)).ToArray();
                    var measured = Measure(tried, out var levels, sound);
                    var values = Values(rows, measured, levels);
                    var triedError = Error(rows, values);

                    if (triedError <= error)
                    {
                        output.WriteLine($"  step {iteration}.{attempt}: error {error:0.00} -> {triedError:0.00}");
                        (z, current, error, taken) = (tried, values, triedError, true);

                        for (var k = 0; k < caps.Length; k++) caps[k] = Math.Min(widest, caps[k] * 1.5d);
                    }
                    else
                    {
                        var blamed = Enumerable.Range(0, keys.Length)
                            .MaxBy(k => Enumerable.Range(0, rows.Count).Sum(i => Math.Abs(jacobian[i, k] * dz[k]) / rows[i].Tolerance));

                        caps[blamed] *= 0.25d;
                        output.WriteLine($"  step {iteration}.{attempt}: error {error:0.00} -> {triedError:0.00}, {labels[blamed]} trusted to {caps[blamed]:0.000}");
                    }
                }

                if (!taken) break;
            }

            return (z, current, error);
        }

        private void Report(List<Row> rows, double[] z, double[] current, double error, (double Light, double Warmth)[] start, double[][] startLevels, Stopwatch watch)
        {
            var end = Measure(z, out var endLevels);

            output.WriteLine();
            output.WriteLine($"{(error <= 1d ? "Reached" : "Stopped short")} in {watch.Elapsed.TotalSeconds:0} s, error {error:0.00} (1 is every row within its tolerance).");

            for (var i = 0; i < rows.Count; i++)
                output.WriteLine($"  {rows[i].Name,-16} {Values(rows, start, startLevels)[i],8:0.000} -> {current[i],8:0.000}  (wanted {rows[i].Target:0.000} ± {rows[i].Tolerance})");

            output.WriteLine("Knobs moved:");

            for (var k = 0; k < keys.Length; k++)
                if (Math.Abs(z[k] - rest[k]) > 1e-4d)
                    output.WriteLine($"  {labels[k],-12} {rest[k]:0.0000} -> {z[k]:0.0000}  ({keys[k]})");

            for (var m = 0; m < settled.Length; m++)
            {
                output.WriteLine($"  at {moments[m]} s: light {start[m].Light:0.000} -> {end[m].Light:0.000}, warmth {start[m].Warmth:+0.000;-0.000} -> {end[m].Warmth:+0.000;-0.000}");
                output.WriteLine($"    octaves {string.Join("  ", SlopeReadings.Octaves.Select((hz, i) => $"{hz}:{endLevels[m][i] - startLevels[m][i]:+0.0;-0.0}"))} dB");
            }

            output.WriteLine("NEW " + string.Join(";", Enumerable.Range(0, keys.Length).Select(k => $"{keys[k]}={((float)z[k]).ToString("R", CultureInfo.InvariantCulture)}")));
        }

        private (double Light, double Warmth)[] Measure(double[] z, out double[][] levels, bool sound = true)
        {
            levels = sound
                ? [.. moments.Select(seconds => Sound(seconds, z))]
                : [.. moments.Select(_ => new double[SlopeReadings.Octaves.Count])];
            return [.. settled.Select(moment => Picture(moment, z))];
        }

        private static double[] Values(List<Row> rows, (double Light, double Warmth)[] picture, double[][] levels) =>
            [.. rows.Select(row => row.Picture ? (row.What == 0 ? picture[row.Moment].Light : picture[row.Moment].Warmth) : levels[row.Moment][row.What])];

        private static double Error(List<Row> rows, double[] values) =>
            rows.Select((row, i) => Math.Abs(row.Target - values[i]) / row.Tolerance).Max();

        private double[,] Jacobian(List<Row> rows, double[] z)
        {
            var jacobian = new double[rows.Count, keys.Length];

            for (var m = 0; m < settled.Length; m++)
            {
                var lightSlope = new double[keys.Length];
                var warmthSlope = new double[keys.Length];
                var pictureRows = rows.Select((row, i) => (row, i)).Where(p => p.row.Picture && p.row.Moment == m).ToArray();

                if (pictureRows.Length > 0)
                {
                    Picture(settled[m], z, lightSlope, warmthSlope);

                    foreach (var (row, i) in pictureRows)
                        for (var k = 0; k < keys.Length; k++)
                            jacobian[i, k] = row.What == 0 ? lightSlope[k] : warmthSlope[k];
                }

                var soundRows = rows.Select((row, i) => (row, i)).Where(p => !p.row.Picture && p.row.Moment == m).ToArray();

                if (soundRows.Length > 0)
                {
                    var slope = new double[soundRows.Length, keys.Length];
                    Sound(moments[m], z, [.. soundRows.Select(p => p.row.What)], slope);

                    for (var r = 0; r < soundRows.Length; r++)
                        for (var k = 0; k < keys.Length; k++)
                            jacobian[soundRows[r].i, k] = slope[r, k];
                }
            }

            return jacobian;
        }

        /// <summary>The smallest knob move the slopes say meets every row, each row weighed by its tolerance.</summary>
        private static double[] MinNorm(double[,] jacobian, double[] residual, List<Row> rows, double[] weight)
        {
            var m = rows.Count;
            var n = jacobian.GetLength(1);
            var a = new double[m, n];
            var r = new double[m];

            for (var i = 0; i < m; i++)
            {
                r[i] = residual[i] / rows[i].Tolerance;
                for (var k = 0; k < n; k++) a[i, k] = weight[k] * jacobian[i, k] / rows[i].Tolerance;
            }

            var normal = new double[m, m];

            for (var i = 0; i < m; i++)
                for (var j = 0; j < m; j++)
                {
                    var sum = 0d;
                    for (var k = 0; k < n; k++) sum += a[i, k] * a[j, k];
                    normal[i, j] = sum + (i == j ? 1e-3d : 0d);
                }

            var w = Solve(normal, r);
            var dz = new double[n];

            for (var k = 0; k < n; k++)
                for (var i = 0; i < m; i++)
                    dz[k] += weight[k] * a[i, k] * w[i];

            return dz;
        }

        private static double[] Solve(double[,] a, double[] b)
        {
            var n = b.Length;
            var m = (double[,])a.Clone();
            var x = (double[])b.Clone();

            for (var col = 0; col < n; col++)
            {
                var pivot = col;
                for (var r = col + 1; r < n; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;

                for (var j = 0; j < n; j++) (m[col, j], m[pivot, j]) = (m[pivot, j], m[col, j]);
                (x[col], x[pivot]) = (x[pivot], x[col]);

                if (Math.Abs(m[col, col]) < 1e-300) continue;

                for (var r = 0; r < n; r++)
                {
                    if (r == col) continue;

                    var f = m[r, col] / m[col, col];
                    for (var j = 0; j < n; j++) m[r, j] -= f * m[col, j];
                    x[r] -= f * x[col];
                }
            }

            for (var i = 0; i < n; i++) x[i] = Math.Abs(m[i, i]) < 1e-300 ? 0d : x[i] / m[i, i];
            return x;
        }

        private static bool Inside(double v) => v > 0d && v < 1d;

        private static double Clamp01(double v) => double.IsFinite(v) ? Math.Clamp(v, 0d, 1d) : 0d;

        private static double Light(double r, double g, double b) => 0.2126d * r + 0.7152d * g + 0.0722d * b;
    }
}
