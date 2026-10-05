using System.CommandLine;
using System.Diagnostics;
using Flyback.Cli.Common;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Slopes;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>The grab-the-output spike: slopes through every preset, checked against finite differences.</summary>
internal static class SlopeSpikeCommand
{
    private const int Columns = 64, Rows = 36, BlockColumns = 8, BlockRows = 6, Fps = 30;

    public static Command Build(PluginRegistry plugins)
    {
        var only = new Option<string[]>("--preset") { AllowMultipleArgumentsPerToken = true };
        var sound = new Option<bool>("--sound") { Description = "The sound instead of the picture." };
        var at = new Option<double>("--at") { DefaultValueFactory = _ => 2d };
        var verbose = new Option<bool>("--verbose");
        var trace = new Option<string>("--trace") { Description = "knob label@x,y: every op's value and slope." };

        var command = new Command("spike-slopes", "Spike: slopes through every preset against finite differences.")
        {
            Hidden = true,
        };

        command.Add(only);
        command.Add(sound);
        command.Add(at);
        command.Add(verbose);
        command.Add(trace);

        command.SetAction((result, _) =>
        {
            plugins.Ready();

            var output = result.InvocationConfiguration.Output;
            var error = result.InvocationConfiguration.Error;
            var names = result.GetValue(only) is { Length: > 0 } wanted
                ? wanted
                : [.. plugins.Catalog.Presets.Select(p => p.Name)];

            var totals = new Totals();

            if (result.GetValue(trace) is { } heardTrace && heardTrace.StartsWith("sound:", StringComparison.Ordinal))
            {
                var opened2 = File.Exists(names[0]) ? Patches.Open(new FileInfo(names[0]), error)!.Value : ShippedPresets.Open(plugins.Catalog, names[0], error)!.Value.Opened;
                SoundTrace(opened2, heardTrace["sound:".Length..], result.GetValue(at), output);
                return Task.FromResult(Exit.Ok);
            }

            if (result.GetValue(trace) is { } traced)
            {
                var opened1 = File.Exists(names[0]) ? Patches.Open(new FileInfo(names[0]), error)!.Value : ShippedPresets.Open(plugins.Catalog, names[0], error)!.Value.Opened;
                Trace(opened1, traced, result.GetValue(at), output);
                return Task.FromResult(Exit.Ok);
            }

            foreach (var name in names)
            {
                if (ShippedPresets.Open(plugins.Catalog, name, error) is not { } opened) continue;

                try
                {
                    if (result.GetValue(sound)) Sound(opened.Opened, opened.Name, result.GetValue(at), output, totals, result.GetValue(verbose));
                    else Picture(opened.Opened, opened.Name, result.GetValue(at), output, totals, result.GetValue(verbose));
                }
                catch (Exception ex)
                {
                    output.WriteLine($"{opened.Name}: FAILED {ex.GetType().Name}: {ex.Message}");
                }
            }

            totals.Write(output, result.GetValue(sound));

            return Task.FromResult(Exit.Ok);
        });

        return command;
    }

    // --- the picture -------------------------------------------------------------------

    private static void Picture(Opened opened, string name, double at, TextWriter output, Totals totals, bool verbose)
    {
        var sp = SlopeProgram.Compile(opened.Patch, sound: false, NodeCatalog.Current, opened.Samples, opened.Pictures);
        var program = sp.Program;
        var knobs = sp.Knobs.Count;

        if (knobs == 0)
        {
            output.WriteLine($"{name}: picture reads no knobs ({program.Ops.Length} ops)");
            return;
        }

        var aspect = (double)Columns / Rows;
        var pixels = Columns * Rows;
        var (before, planes, t) = SlopeReadings.Settle(sp, at, Columns, Rows);

        var all = Enumerable.Range(0, knobs).ToArray();
        var slopes = new Slopes(sp, all);
        var registers = program.AllocateRegisters();
        var cells = new float[program.PlaneCount];

        // E1: every knob's slope at sample points, against a central difference.
        int agree = 0, kinks = 0, wrong = 0, flatBoth = 0, oneSided = 0, sensitive = 0;
        var mismatches = new List<string>();

        for (var py = 2; py < Rows; py += 6)
        {
            for (var px = 3; px < Columns; px += 8)
            {
                var p = py * Columns + px;
                var (x, y) = At(p, aspect);

                Cells(planes, p, cells);
                slopes.Pixel(x, y, t, aspect, before, cells);

                for (var k = 0; k < knobs; k++)
                {
                    var knob = sp.Knobs[k];
                    var h = Step(knob);
                    var fd = Difference(sp, k, h, x, y, t, aspect, before, planes, p, registers, cells);
                    var fine = Difference(sp, k, h / 16d, x, y, t, aspect, before, planes, p, registers, cells);
                    var (right, left) = Sides(sp, k, h / 16d, x, y, t, aspect, before, planes, p, registers, cells);

                    for (var c = 0; c < 3; c++)
                    {
                        var ad = slopes.Output(c)[k] * knob.Range;
                        var coarse = fd[c] * knob.Range;
                        var small = fine[c] * knob.Range;

                        if (ad == 0d && coarse == 0d) { flatBoth++; agree++; }
                        else if (Close(ad, coarse) || Close(ad, small)) agree++;
                        else if (!Close(coarse, small)) kinks++;
                        else if (Close(ad, right[c] * knob.Range) || Close(ad, left[c] * knob.Range)) oneSided++;
                        else if (Close(ad, Difference(sp, k, h / 65536d, x, y, t, aspect, before, planes, p, registers, cells)[c] * knob.Range)) sensitive++;
                        else
                        {
                            wrong++;
                            if (mismatches.Count < 6)
                                mismatches.Add($"    {knob.Label} at ({x:0.00},{y:0.00}) ch{c}: slope {ad:G4}, difference {coarse:G4} / {small:G4}");
                        }
                    }
                }
            }
        }

        // E2: each block's mean light against a 2% turn of each knob, exact and with a pixel's footprint.
        var blocks = BlockColumns * BlockRows;
        var smooth = new Slopes(sp, all, 2d / Rows);
        var exactBlock = BlockSlopes(sp, slopes, t, aspect, before, planes, cells, out var mean);
        var smoothBlock = BlockSlopes(sp, smooth, t, aspect, before, planes, cells, out _);
        var fdBlock = new double[blocks * knobs];
        var bent = new bool[blocks * knobs];

        for (var k = 0; k < knobs; k++)
        {
            var knob = sp.Knobs[k];
            var delta = 0.02f * knob.Range;
            var up = Frame(sp, k, knob.Value + delta, t, aspect, before, planes, registers, cells);
            var down = Frame(sp, k, knob.Value - delta, t, aspect, before, planes, registers, cells);
            var span = ((double)(float)(knob.Value + delta) - (float)(knob.Value - delta)) / knob.Range;

            var rightSpan = ((double)(float)(knob.Value + delta) - knob.Value) / knob.Range;
            var leftSpan = (knob.Value - (double)(float)(knob.Value - delta)) / knob.Range;

            for (var b = 0; b < blocks; b++)
            {
                fdBlock[b * knobs + k] = (up[b] - down[b]) / span;

                // A move whose two halves disagree crossed something: a regime, not a slope.
                var right = (up[b] - mean[b]) / rightSpan;
                var left = (mean[b] - down[b]) / leftSpan;
                var larger = Math.Max(Math.Abs(right), Math.Abs(left));

                bent[b * knobs + k] = larger >= 0.05d && (Math.Sign(right) != Math.Sign(left) || Math.Min(Math.Abs(right), Math.Abs(left)) < larger / 3d);
            }
        }

        var exact = Region(fdBlock, exactBlock, bent);
        var smoothed = Region(fdBlock, smoothBlock, bent);

        sp.Rest();

        // E3: what a pixel costs plain, with one lane, and with every knob.
        var plain = Time(() =>
        {
            for (var p = 0; p < pixels; p++)
            {
                var (x, y) = At(p, aspect);
                Cells(planes, p, cells);
                program.Evaluate(x, y, t, registers, before, null, aspect, sp.Live, cells);
            }
        }) / pixels;

        var one = new Slopes(sp, [0]);
        var single = Time(() =>
        {
            for (var p = 0; p < pixels; p++)
            {
                var (x, y) = At(p, aspect);
                Cells(planes, p, cells);
                one.Pixel(x, y, t, aspect, before, cells);
            }
        }) / pixels;

        var every = Time(() =>
        {
            for (var p = 0; p < pixels; p++)
            {
                var (x, y) = At(p, aspect);
                Cells(planes, p, cells);
                slopes.Pixel(x, y, t, aspect, before, cells);
            }
        }) / pixels;

        // E5: drag the block that moves most by 0.1 of light, alone and with a far corner kept.
        var liveliest = Enumerable.Range(0, blocks)
            .MaxBy(b => Enumerable.Range(0, knobs).Where(k => !bent[b * knobs + k]).Sum(k => fdBlock[b * knobs + k] * fdBlock[b * knobs + k]));
        var goal = mean[liveliest] > 0.8d ? -0.1d : 0.1d;
        var far = Far(liveliest);
        var movable = Enumerable.Range(0, knobs).Any(k => !bent[liveliest * knobs + k] && Math.Abs(fdBlock[liveliest * knobs + k]) >= 0.05d);
        string[] drags =
        [
            Drag(sp, slopes, Weights(exactBlock, knobs), liveliest, goal, [], t, aspect, before, planes, cells),
            Drag(sp, smooth, Weights(smoothBlock, knobs), liveliest, goal, [], t, aspect, before, planes, cells),
            Drag(sp, slopes, Weights(exactBlock, knobs), liveliest, goal, far, t, aspect, before, planes, cells),
            Drag(sp, smooth, Weights(smoothBlock, knobs), liveliest, goal, far, t, aspect, before, planes, cells),
        ];

        sp.Rest();

        var checks = agree + kinks + oneSided + sensitive + wrong;
        var feedback = program.Ops.Any(op => op.Code is OpCode.SampleFeedback) ? "F" : program.PlaneCount > 0 ? "P" : " ";

        output.WriteLine(
            $"{name,-28}{feedback} ops {program.Ops.Length,5}  knobs {knobs,4}  "
            + $"E1 {Percent(agree, checks)} agree, {Percent(kinks, checks)} kink, {oneSided} one-sided, {sensitive} sensitive, {wrong} wrong  "
            + $"E2 {exact.Significant,5} moved ({exact.Bent} bent): blind {Percent(exact.Blind, exact.Significant)} -> {Percent(smoothed.Blind, smoothed.Significant)}, "
            + $"2x {Percent(exact.Within, exact.Significant)} -> {Percent(smoothed.Within, smoothed.Significant)}, "
            + $"sign {Percent(exact.Sign, exact.Significant)} -> {Percent(smoothed.Sign, smoothed.Significant)}, phantom {exact.Phantom} -> {smoothed.Phantom}  "
            + $"E3 {plain * 1e6:0.00} / {single * 1e6:0.00} / {every * 1e6:0.0} us  "
            + $"E5{(movable ? "" : " (fixed)")} {string.Join(" | ", drags)}");

        if (verbose || wrong > 0)
            foreach (var line in mismatches) output.WriteLine(line);

        if (verbose)
        {
            foreach (var k in Enumerable.Range(0, knobs))
            {
                var moved = Enumerable.Range(0, blocks).Where(b => Math.Abs(fdBlock[b * knobs + k]) >= 0.05d).ToArray();
                if (moved.Length == 0) continue;
                var blindHere = moved.Count(b => Math.Abs(smoothBlock[b * knobs + k]) < 0.1d * Math.Abs(fdBlock[b * knobs + k]));
                var sample = moved.MaxBy(b => Math.Abs(fdBlock[b * knobs + k]));
                output.WriteLine($"    {sp.Knobs[k].Label,-32} moved {moved.Length,3} blind {blindHere,3}  e.g. block {sample}: fd {fdBlock[sample * knobs + k]:G3} exact {exactBlock[sample * knobs + k]:G3} smooth {smoothBlock[sample * knobs + k]:G3}");
            }
        }

        if (slopes.NonFinite > 0) output.WriteLine($"    {slopes.NonFinite} slopes were not finite");

        totals.Add(knobs, agree, kinks + oneSided + sensitive, wrong, exact, smoothed, plain, single, every, movable, drags);
    }

    /// <summary>Each block's mean light, and how far it moves over each knob's travel.</summary>
    private static double[] BlockSlopes(
        SlopeProgram sp, Slopes slopes, double t, double aspect,
        in FeedbackFrame before, float[] planes, float[] cells, out double[] mean)
    {
        var knobs = sp.Knobs.Count;
        var pixels = Columns * Rows;
        var blocks = BlockColumns * BlockRows;
        var perBlock = pixels / blocks;
        var result = new double[blocks * knobs];

        mean = new double[blocks];

        for (var p = 0; p < pixels; p++)
        {
            var (x, y) = At(p, aspect);
            var b = Block(p);

            Cells(planes, p, cells);
            slopes.Pixel(x, y, t, aspect, before, cells);

            mean[b] += Light(slopes.Value(0), slopes.Value(1), slopes.Value(2)) / perBlock;

            for (var k = 0; k < knobs; k++)
                result[b * knobs + k] += Light(slopes.Output(0)[k], slopes.Output(1)[k], slopes.Output(2)[k]) * sp.Knobs[k].Range / perBlock;
        }

        return result;
    }

    private readonly record struct RegionCounts(int Significant, int Blind, int Within, int Sign, int Phantom, int Bent = 0);

    private static RegionCounts Region(double[] differences, double[] slopes, bool[] bent)
    {
        int significant = 0, blind = 0, within = 0, sign = 0, phantom = 0, bends = 0;

        for (var i = 0; i < differences.Length; i++)
        {
            double fd = differences[i], ad = slopes[i];

            if (bent[i])
            {
                bends++;
            }
            else if (Math.Abs(fd) >= 0.05d)
            {
                significant++;
                if (Math.Abs(ad) < 0.1d * Math.Abs(fd)) blind++;
                else if (Math.Sign(ad) != Math.Sign(fd)) sign++;
                else if (ad / fd is >= 0.5d and <= 2d) within++;
            }
            else if (Math.Abs(fd) < 0.005d && Math.Abs(ad) >= 0.05d)
            {
                phantom++;
            }
        }

        return new RegionCounts(significant, blind, within, sign, phantom, bends);
    }

    private static double[] Difference(
        SlopeProgram sp, int knob, double h, double x, double y, double t, double aspect,
        in FeedbackFrame before, float[] planes, int pixel, double[] registers, float[] cells)
    {
        var rest = sp.Knobs[knob].Value;
        var up = (float)(rest + h);
        var down = (float)(rest - h);
        var result = new double[3];

        sp.Turn(knob, up);
        Cells(planes, pixel, cells);
        sp.Program.Evaluate(x, y, t, registers, before, null, aspect, sp.Live, cells);
        for (var c = 0; c < 3; c++) result[c] = registers[sp.Program.OutputBase + c];

        sp.Turn(knob, down);
        Cells(planes, pixel, cells);
        sp.Program.Evaluate(x, y, t, registers, before, null, aspect, sp.Live, cells);
        for (var c = 0; c < 3; c++) result[c] = (result[c] - registers[sp.Program.OutputBase + c]) / ((double)up - down);

        sp.Turn(knob, rest);
        return result;
    }

    /// <summary>The slopes either side of where the knob rests, for a knob resting on a kink.</summary>
    private static (double[] Right, double[] Left) Sides(
        SlopeProgram sp, int knob, double h, double x, double y, double t, double aspect,
        in FeedbackFrame before, float[] planes, int pixel, double[] registers, float[] cells)
    {
        var rest = sp.Knobs[knob].Value;
        var at = new double[3];
        var right = new double[3];
        var left = new double[3];

        foreach (var (value, into) in new[] { (rest, at), ((float)(rest + h), right), ((float)(rest - h), left) })
        {
            sp.Turn(knob, value);
            Cells(planes, pixel, cells);
            sp.Program.Evaluate(x, y, t, registers, before, null, aspect, sp.Live, cells);
            for (var c = 0; c < 3; c++) into[c] = registers[sp.Program.OutputBase + c];
        }

        sp.Turn(knob, rest);

        for (var c = 0; c < 3; c++)
        {
            right[c] = (right[c] - at[c]) / ((double)(float)(rest + h) - rest);
            left[c] = (at[c] - left[c]) / (rest - (double)(float)(rest - h));
        }

        return (right, left);
    }

    /// <summary>Each block's mean light with one knob turned to <paramref name="value"/>.</summary>
    private static double[] Frame(
        SlopeProgram sp, int knob, float value, double t, double aspect,
        in FeedbackFrame before, float[] planes, double[] registers, float[] cells)
    {
        var program = sp.Program;
        var pixels = Columns * Rows;
        var blocks = new double[BlockColumns * BlockRows];
        var perBlock = pixels / blocks.Length;

        sp.Turn(knob, value);

        for (var p = 0; p < pixels; p++)
        {
            var (x, y) = At(p, aspect);
            Cells(planes, p, cells);
            program.Evaluate(x, y, t, registers, before, null, aspect, sp.Live, cells);
            blocks[Block(p)] += Light(registers[program.OutputBase], registers[program.OutputBase + 1], registers[program.OutputBase + 2]) / perBlock;
        }

        sp.Turn(knob, sp.Knobs[knob].Value);
        return blocks;
    }

    /// <summary>
    /// Gauss-Newton toward a block's light moved by <paramref name="goal"/>, holding
    /// <paramref name="keep"/> where they were: the smallest step in travel-scaled knob units.
    /// </summary>
    /// <summary>How freely the solver may turn each knob: less, the more it moves the whole frame.</summary>
    private static double[] Weights(double[] blockSlopes, int knobs)
    {
        var blocks = blockSlopes.Length / knobs;

        return [.. Enumerable.Range(0, knobs).Select(k =>
            1d / (1d + Math.Sqrt(Enumerable.Range(0, blocks).Average(b => blockSlopes[b * knobs + k] * blockSlopes[b * knobs + k]))))];
    }

    private static string Drag(
        SlopeProgram sp, Slopes slopes, double[] weight, int block, double goal, int[] keep, double t, double aspect,
        in FeedbackFrame before, float[] planes, float[] cells)
    {
        var knobs = sp.Knobs.Count;
        int[] rows = [block, .. keep];
        var start = BlockLight(sp, slopes, rows, t, aspect, before, planes, cells, out _);
        var target = rows.Select((_, i) => i == 0 ? start[0] + goal : start[i]).ToArray();
        var moved = new double[knobs];
        var light = start;
        var jacobian = Jacobian(sp, slopes, rows, t, aspect, before, planes, cells);

        for (var step = 1; step <= 40; step++)
        {
            var residual = target.Select((goalLight, i) => goalLight - light[i]).ToArray();

            if (Math.Abs(residual[0]) < 0.01d && residual.Skip(1).All(r => Math.Abs(r) < 0.02d))
            {
                sp.Rest();
                return $"{step - 1,2} steps {Norm(moved):0.000}";
            }

            // dz = J^T (J J^T + l I)^-1 r, with knobs measured in their travel.
            var m = rows.Length;
            var normal = new double[m, m];

            for (var i = 0; i < m; i++)
                for (var j = 0; j < m; j++)
                    normal[i, j] = Enumerable.Range(0, knobs).Sum(k => weight[k] * weight[k] * jacobian[i, k] * jacobian[j, k]) + (i == j ? 1e-9d : 0d);

            var weights = Solve(normal, residual);
            var dz = new double[knobs];

            for (var k = 0; k < knobs; k++)
                for (var i = 0; i < m; i++)
                    dz[k] += weight[k] * weight[k] * jacobian[i, k] * weights[i];

            var size = Norm(dz);
            if (size == 0d) break;
            if (size > 0.02d) for (var k = 0; k < knobs; k++) dz[k] *= 0.02d / size;

            // Halve the step until it leaves less to do, as a slope only holds for a small move.
            var was = Error(residual);
            var taken = false;

            for (var halving = 0; halving < 8 && !taken; halving++, Scale(dz, 0.5d))
            {
                for (var k = 0; k < knobs; k++) sp.Turn(k, (float)(sp.Knobs[k].Value + (moved[k] + dz[k]) * sp.Knobs[k].Range));

                var tried = BlockLight(sp, slopes, rows, t, aspect, before, planes, cells, out var next);

                // Equal is taken: a hard edge between pixel centers leaves the light flat until it crosses one.
                if (Error([.. target.Select((goalLight, i) => goalLight - tried[i])]) > was) continue;

                for (var k = 0; k < knobs; k++) moved[k] += dz[k];
                (light, jacobian, taken) = (tried, next, true);
            }

            if (!taken)
            {
                if (Environment.GetEnvironmentVariable("SPIKE_DEBUG") is not null)
                {
                    var top = Enumerable.Range(0, knobs).OrderByDescending(k => Math.Abs(dz[k])).Take(3)
                        .Select(k => $"{sp.Knobs[k].Label} J {jacobian[0, k]:G3} w {weight[k]:G3} dz {dz[k]:G3}");
                    Console.Error.WriteLine($"  stuck at step {step}: residual {string.Join(",", residual.Select(r => r.ToString("G3")))} |dz| {Norm(dz):G3}; {string.Join("; ", top)}");
                }

                break;
            }
        }

        sp.Rest();
        return $"miss {light[0] - start[0]:+0.000;-0.000} keep {rows.Skip(1).Select((_, i) => Math.Abs(light[i + 1] - start[i + 1])).DefaultIfEmpty(0d).Max():0.000}";
    }

    private static double Error(double[] residual) => Math.Max(Math.Abs(residual[0]) / 0.01d, residual.Skip(1).Select(r => Math.Abs(r) / 0.02d).DefaultIfEmpty(0d).Max());

    private static void Scale(double[] v, double by)
    {
        for (var i = 0; i < v.Length; i++) v[i] *= by;
    }

    private static double[,] Jacobian(
        SlopeProgram sp, Slopes slopes, int[] rows, double t, double aspect,
        in FeedbackFrame before, float[] planes, float[] cells)
    {
        BlockLight(sp, slopes, rows, t, aspect, before, planes, cells, out var jacobian);
        return jacobian;
    }

    private static double[] BlockLight(
        SlopeProgram sp, Slopes slopes, int[] rows, double t, double aspect,
        in FeedbackFrame before, float[] planes, float[] cells, out double[,] jacobian)
    {
        var knobs = sp.Knobs.Count;
        var light = new double[rows.Length];
        var perBlock = Columns * Rows / (BlockColumns * BlockRows);

        jacobian = new double[rows.Length, knobs];

        for (var p = 0; p < Columns * Rows; p++)
        {
            var row = Array.IndexOf(rows, Block(p));
            if (row < 0) continue;

            var (x, y) = At(p, aspect);
            Cells(planes, p, cells);
            slopes.Pixel(x, y, t, aspect, before, cells);

            light[row] += Light(slopes.Value(0), slopes.Value(1), slopes.Value(2)) / perBlock;

            for (var k = 0; k < knobs; k++)
                jacobian[row, k] += Light(slopes.Output(0)[k], slopes.Output(1)[k], slopes.Output(2)[k]) * sp.Knobs[k].Range / perBlock;
        }

        return light;
    }

    private static double[] Solve(double[,] a, double[] b)
    {
        var n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();

        for (var col = 0; col < n; col++)
        {
            var pivot = Enumerable.Range(col, n - col).MaxBy(r => Math.Abs(m[r, col]));

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

    /// <summary>The four blocks in the corner furthest from <paramref name="block"/>.</summary>
    private static int[] Far(int block)
    {
        var (row, column) = Math.DivRem(block, BlockColumns);
        var farRow = row < BlockRows / 2 ? BlockRows - 2 : 0;
        var farColumn = column < BlockColumns / 2 ? BlockColumns - 2 : 0;

        return [farRow * BlockColumns + farColumn, farRow * BlockColumns + farColumn + 1, (farRow + 1) * BlockColumns + farColumn, (farRow + 1) * BlockColumns + farColumn + 1];
    }

    private static void Trace(Opened opened, string traced, double at, TextWriter output)
    {
        var (label, where) = (traced[..traced.IndexOf('@')], traced[(traced.IndexOf('@') + 1)..].Split(','));
        var sp = SlopeProgram.Compile(opened.Patch, sound: false, NodeCatalog.Current, opened.Samples, opened.Pictures);
        var knob = Enumerable.Range(0, sp.Knobs.Count).First(k => sp.Knobs[k].Label == label);
        var aspect = (double)Columns / Rows;
        var (before, planes, t) = SlopeReadings.Settle(sp, at, Columns, Rows);
        var slopes = new Slopes(sp, [knob], 2d / Rows);
        double x = double.Parse(where[0], System.Globalization.CultureInfo.InvariantCulture), y = double.Parse(where[1], System.Globalization.CultureInfo.InvariantCulture);
        var cells = new float[sp.Program.PlaneCount];

        planes.AsSpan(SlopeReadings.Nearest(x, y, Columns, Rows) * cells.Length, cells.Length).CopyTo(cells);
        slopes.Pixel(x, y, t, aspect, before, cells);

        foreach (var k in Enumerable.Range(0, sp.Knobs.Count).Where(k => sp.Knobs[k].Label == label))
        {
            var fd = Difference(sp, k, Step(sp.Knobs[k]), x, y, t, aspect, before, planes, SlopeReadings.Nearest(x, y, Columns, Rows), sp.Program.AllocateRegisters(), cells);
            output.WriteLine($"knob {k} {sp.Knobs[k]} live {Array.IndexOf(sp.KnobOf, k)} fd {string.Join(" ", fd)} h {Step(sp.Knobs[k])}");
            var regs = sp.Program.AllocateRegisters();
            foreach (var v in new[] { 0.4f, 0.402f, 0.5f, 3f })
            {
                sp.Turn(k, v);
                sp.Program.Evaluate(x, y, t, regs, before, null, aspect, sp.Live, cells);
                output.WriteLine($"  at {v}: live {sp.Live.At(Array.IndexOf(sp.KnobOf, k))} r105 {regs[105]} r109 {regs[109]} r122 {regs[122]} out {regs[sp.Program.OutputBase]} {regs[sp.Program.OutputBase + 1]} base {sp.Program.OutputBase}");
            }
            sp.Rest();
        }

        foreach (var op in sp.Program.Ops)
        {
            var width = OpShape.Outputs(op.Code);
            var parts = Enumerable.Range(0, width).Select(w => $"{slopes.Values[op.Out + w]:G6} d {slopes.Of(op.Out + w)[0]:G6}");
            output.WriteLine($"{op,-50} {string.Join(" | ", parts)}");
        }
    }

    /// <summary>The left channel's slope against a central difference, sample by sample, worst in each 50 ms.</summary>
    private static void SoundTrace(Opened opened, string label, double at, TextWriter output)
    {
        const int rate = 96_000;
        var sp = SlopeProgram.Compile(opened.Patch, sound: true, NodeCatalog.Current, opened.Samples, opened.Pictures);
        var knob = Enumerable.Range(0, sp.Knobs.Count).First(k => sp.Knobs[k].Label == label);
        var program = sp.Program;
        var h = Step(sp.Knobs[knob]) / 16d;

        if (Environment.GetEnvironmentVariable("SPIKE_OPS") is not null)
            for (var i = 0; i < program.LiveInputs.Count; i++)
                output.WriteLine($"live {i}: {program.LiveInputs[i]} -> knob {sp.KnobOf[i]} {(sp.KnobOf[i] >= 0 ? sp.Knobs[sp.KnobOf[i]].Label : "")}{(sp.KnobOf[i] == knob ? "  <- traced" : "")}");
        var rest = sp.Knobs[knob].Value;
        float up = (float)(rest + h), down = (float)(rest - h);

        var delays = new DelayState(program, rate);
        var slopes = new Slopes(sp, [knob]);
        var memory = new SlopeMemory(program, delays, 1);
        var programs = new[] { SlopeProgram.Compile(opened.Patch, true, NodeCatalog.Current, opened.Samples, opened.Pictures), SlopeProgram.Compile(opened.Patch, true, NodeCatalog.Current, opened.Samples, opened.Pictures) };
        programs[0].Turn(knob, up);
        programs[1].Turn(knob, down);
        var states = programs.Select(p => new DelayState(p.Program, rate)).ToArray();
        var banks = programs.Select(p => p.Program.AllocateRegisters()).ToArray();
        double worst = 0d, scale = 0d;

        for (long step = 0; step < (long)(at * rate); step++)
        {
            var t = step / (double)rate;
            slopes.Sample(t, 16d / 9d, delays, memory);

            for (var i = 0; i < 2; i++) programs[i].Program.Evaluate(0d, 0d, t, banks[i], default, states[i], 16d / 9d, programs[i].Live);

            var fd = (banks[0][program.OutputBase] - banks[1][program.OutputBase]) / ((double)up - down);
            var ad = slopes.Output(0)[0];

            worst = Math.Max(worst, Math.Abs(ad - fd));
            scale = Math.Max(scale, Math.Max(Math.Abs(ad), Math.Abs(fd)));

            if (step + 1 == (long)(at * rate) && Environment.GetEnvironmentVariable("SPIKE_OPS") is { } dump)
            {
                using var file = new StreamWriter(dump);
                foreach (var op in program.Ops)
                {
                    var width = Core.Compile.OpShape.Outputs(op.Code);
                    file.WriteLine($"{op,-50} " + string.Join(" | ", Enumerable.Range(0, width).Select(w => $"{slopes.Values[op.Out + w]:G6} d {slopes.Of(op.Out + w)[0]:G6}")));
                }
            }

            if ((step + 1) % (rate / 20) == 0)
            {
                output.WriteLine($"{t:0.00} s  worst |slope - difference| {worst:G3} of {scale:G3}  now {ad:G4} vs {fd:G4}");
                worst = scale = 0d;
            }
        }
    }

    // --- the sound ---------------------------------------------------------------------

    private static void Sound(Opened opened, string name, double at, TextWriter output, Totals totals, bool verbose)
    {
        const int rate = 96_000, window = 8192, lanes = 8;

        var sp = SlopeProgram.Compile(opened.Patch, sound: true, NodeCatalog.Current, opened.Samples, opened.Pictures);
        var knobs = sp.Knobs.Count;

        if (knobs == 0)
        {
            output.WriteLine($"{name}: sound reads no knobs ({sp.Program.Ops.Length} ops)");
            return;
        }

        var chosen = Enumerable.Range(0, Math.Min(lanes, knobs)).Select(i => i * knobs / Math.Min(lanes, knobs)).Distinct().ToArray();
        var steps = (long)Math.Round(at * rate);

        var watch = Stopwatch.StartNew();
        var (levels, slopes) = SlopeReadings.Bands(sp, chosen, rate, steps, window);
        var tangentTime = watch.Elapsed.TotalSeconds;

        watch.Restart();
        SlopeReadings.Levels(sp, rate, steps, window);
        var plainTime = watch.Elapsed.TotalSeconds;

        int agree = 0, kinks = 0, wrong = 0, chaotic = 0;
        var mismatches = new List<string>();

        for (var lane = 0; lane < chosen.Length; lane++)
        {
            var k = chosen[lane];
            var knob = sp.Knobs[k];
            var h = Step(knob);
            var coarse = SoundDifference(sp, k, h, rate, steps, window);
            var fine = SoundDifference(sp, k, h / 16d, rate, steps, window);

            for (var band = 0; band < SlopeReadings.Octaves.Count; band++)
            {
                if (levels[band] < -90d) continue;

                var ad = slopes[lane, band] * knob.Range;
                var c = coarse[band] * knob.Range;
                var f = fine[band] * knob.Range;

                if (CloseDb(ad, c) || CloseDb(ad, f)) agree++;
                else if (!CloseDb(c, f)) kinks++;
                else
                {
                    wrong++;
                    if (mismatches.Count < 6) mismatches.Add($"    {knob.Label} {SlopeReadings.Octaves[band]} Hz: slope {ad:G4} dB, difference {c:G4} / {f:G4}");
                }

                if (Math.Abs(ad) > 1e4d) chaotic++;
            }
        }

        var checks = agree + kinks + wrong;

        output.WriteLine(
            $"{name,-28} ops {sp.Program.Ops.Length,5}  knobs {knobs,4} ({chosen.Length} taken)  "
            + $"{Percent(agree, checks)} agree, {Percent(kinks, checks)} kink, {wrong} wrong, {chaotic} over 1e4 dB/travel  "
            + $"{at:0.#} s: plain {plainTime:0.00} s, {chosen.Length} lanes {tangentTime:0.00} s");

        if (verbose || wrong > 0)
            foreach (var line in mismatches) output.WriteLine(line);

        totals.AddSound(agree, kinks, wrong, plainTime, tangentTime, chosen.Length);
    }

    private static double[] SoundDifference(SlopeProgram sp, int knob, double h, int rate, long steps, int window)
    {
        var rest = sp.Knobs[knob].Value;
        var up = (float)(rest + h);
        var down = (float)(rest - h);

        sp.Turn(knob, up);
        var high = SlopeReadings.Levels(sp, rate, steps, window);

        sp.Turn(knob, down);
        var low = SlopeReadings.Levels(sp, rate, steps, window);

        sp.Turn(knob, rest);
        return [.. high.Select((level, band) => (level - low[band]) / ((double)up - down))];
    }

    // --- shared ------------------------------------------------------------------------

    /// <summary>A difference step small against the knob's value and its travel.</summary>
    private static double Step(Knob knob) =>
        1e-5d * Math.Max(Math.Max(Math.Abs(knob.Value), 0.01d * knob.Range), 1e-3d);

    private static bool Close(double a, double b) =>
        Math.Abs(a - b) <= 1e-4d + 1e-3d * Math.Max(Math.Abs(a), Math.Abs(b));

    private static bool CloseDb(double a, double b) =>
        Math.Abs(a - b) <= 0.01d + 0.01d * Math.Max(Math.Abs(a), Math.Abs(b));

    private static double Light(double r, double g, double b) => 0.2126d * r + 0.7152d * g + 0.0722d * b;

    private static double Finite(double v) => double.IsFinite(v) ? v : 0d;

    private static double Norm(double[] v) => Math.Sqrt(v.Sum(x => x * x));

    private static int Block(int pixel)
    {
        var (row, column) = Math.DivRem(pixel, Columns);

        return row / (Rows / BlockRows) * BlockColumns + column / (Columns / BlockColumns);
    }

    private static (double X, double Y) At(int pixel, double aspect) => SlopeReadings.At(pixel, Columns, Rows);

    private static void Cells(float[] planes, int pixel, float[] into)
    {
        if (into.Length > 0) planes.AsSpan(pixel * into.Length, into.Length).CopyTo(into);
    }

    private static double Time(Action run)
    {
        run();

        var watch = Stopwatch.StartNew();
        var rounds = 0;

        do
        {
            run();
            rounds++;
        }
        while (watch.Elapsed.TotalMilliseconds < 200d);

        return watch.Elapsed.TotalSeconds / rounds;
    }

    private static string Percent(int part, int whole) => whole == 0 ? "  -  " : $"{100d * part / whole,5:0.0}%";

    private sealed class Totals
    {
        private int presets, knobs, agree, kinks, wrong, movable, soundAgree, soundKinks, soundWrong, soundLanes;
        private RegionCounts exact, smoothed;
        private readonly int[] reached = new int[4];
        private double plain, single, every, plainSound, laneSound;

        public void Add(int knobCount, int a, int k, int w, RegionCounts e, RegionCounts s, double p, double one, double all, bool canMove, string[] drags)
        {
            presets++;
            knobs += knobCount;
            agree += a;
            kinks += k;
            wrong += w;
            exact = Sum(exact, e);
            smoothed = Sum(smoothed, s);
            plain += p;
            single += one;
            every += all;

            if (!canMove) return;

            movable++;
            for (var i = 0; i < drags.Length; i++)
                if (!drags[i].StartsWith("miss", StringComparison.Ordinal)) reached[i]++;
        }

        private static RegionCounts Sum(RegionCounts a, RegionCounts b) =>
            new(a.Significant + b.Significant, a.Blind + b.Blind, a.Within + b.Within, a.Sign + b.Sign, a.Phantom + b.Phantom, a.Bent + b.Bent);

        public void AddSound(int a, int k, int w, double p, double lanes, int laneCount)
        {
            presets++;
            soundAgree += a;
            soundKinks += k;
            soundWrong += w;
            plainSound += p;
            laneSound += lanes;
            soundLanes += laneCount;
        }

        public void Write(TextWriter output, bool sound)
        {
            output.WriteLine();

            if (sound)
            {
                var checks = soundAgree + soundKinks + soundWrong;
                output.WriteLine($"{presets} presets: {Percent(soundAgree, checks)} agree, {Percent(soundKinks, checks)} kink, {soundWrong} wrong; plain {plainSound:0.0} s, with slopes {laneSound:0.0} s ({soundLanes} lanes)");
                return;
            }

            var all = agree + kinks + wrong;
            output.WriteLine(
                $"{presets} presets, {knobs} knobs: E1 {Percent(agree, all)} agree, {Percent(kinks, all)} kink or too sensitive, {wrong} wrong; "
                + $"E2 {exact.Significant} moved ({exact.Bent} bent): blind {Percent(exact.Blind, exact.Significant)} -> {Percent(smoothed.Blind, smoothed.Significant)}, "
                + $"within 2x {Percent(exact.Within, exact.Significant)} -> {Percent(smoothed.Within, smoothed.Significant)}, "
                + $"wrong sign {Percent(exact.Sign, exact.Significant)} -> {Percent(smoothed.Sign, smoothed.Significant)}, phantom {exact.Phantom} -> {smoothed.Phantom}; "
                + $"E3 mean {plain / presets * 1e6:0.00} / {single / presets * 1e6:0.00} / {every / presets * 1e6:0.0} us a pixel; "
                + $"E5 of {movable} with a block to move: alone {reached[0]} -> {reached[1]}, keeping a corner {reached[2]} -> {reached[3]}");
        }
    }
}
