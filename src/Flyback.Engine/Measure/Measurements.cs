using System.Runtime.CompilerServices;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;

namespace Flyback.Engine.Measure;

/// <summary>
/// Runs a patch offline over a window and says what every output carried, to the
/// speakers and to the screen.
/// </summary>
/// <remarks>
/// The sound runs at the speakers' oversampled rate with memory of its own, from
/// empty, so a long delay is measured filling. The picture runs on a coarse grid at
/// every frame, so a loop across frames runs as it does on screen; where the patch
/// reads the frame before, the Output's picture is drawn on the same grid to be it.
/// Nothing is played in: keys are up and knobs rest where they are.
/// </remarks>
public static class Measurements
{
    /// <summary>Evaluations between two looks at the cancellation token and the progress.</summary>
    private const int Stride = 4096;

    /// <summary>
    /// How rarely a signal may change to count as stepped: a sequencer holds for
    /// thousands of samples, and for a few frames.
    /// </summary>
    private const double SoundStepped = 0.01, PictureStepped = 0.25;

    /// <exception cref="ArgumentOutOfRangeException">The window is not a positive number of seconds no longer than <see cref="MeasureOptions.MaxSeconds"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancel"/> fired.</exception>
    public static MeasureReport Take(
        Patch patch,
        MeasureOptions options,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        IProgress<double>? progress = null,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(options);

        if (!(options.Seconds > 0d && options.Seconds <= MeasureOptions.MaxSeconds))
            throw new ArgumentOutOfRangeException(nameof(options), options.Seconds, $"A window runs above 0 and up to {MeasureOptions.MaxSeconds} seconds.");

        if (!double.IsFinite(options.From) || options.From < 0d)
            throw new ArgumentOutOfRangeException(nameof(options), options.From, "A window starts at 0 seconds or later.");

        if (options.SampleRate <= 0 || options.Columns <= 0 || options.Rows <= 0 || options.FramesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Rates and the grid are positive.");

        var catalog = modules ?? NodeCatalog.Current;
        var heard = patch.CompileForMeasure(options.Modules, sound: true, catalog, samples, pictures);
        var seen = patch.CompileForMeasure(options.Modules, sound: false, catalog, samples, pictures);

        var feedback = seen.Program.Ops.Any(op => op.Code is OpCode.SampleFeedback);
        var video = feedback ? patch.CompileForVideo(catalog, samples, pictures).Program : null;

        var soundSteps = (long)Math.Round(options.Seconds * options.SampleRate);
        var frames = Math.Max(1, (int)Math.Round(options.Seconds * options.FramesPerSecond));
        var work = new Work(progress, soundSteps + (long)frames * options.Columns * options.Rows * (video is null ? 1 : 2));

        var sound = Sound(heard.Program, options, soundSteps, work, cancel);
        var (picture, first, last) = Picture(seen.Program, video, options, frames, work, cancel);
        var pixels = options.Columns * options.Rows;

        progress?.Report(1d);

        var pictured = seen.Sockets.ToDictionary(socket => (socket.Node, socket.Port));
        var measurements = new List<Measurement>();

        foreach (var socket in heard.Sockets)
        {
            if (!pictured.TryGetValue((socket.Node, socket.Port), out var other)) continue;
            if (patch.Find(socket.Node) is not { } node || catalog.Get(node.TypeId) is not { } def) continue;

            measurements.Add(new Measurement(
                socket.Node,
                socket.Port,
                node.Title(def),
                def.Outputs[socket.Port].Name,
                sound.Slice(socket.Offset, socket.Width).ToArray(),
                picture.AsSpan(other.Offset, other.Width).ToArray(),
                Pictured(picture, first, last, other, pixels)));
        }

        var issues = heard.Issues.Concat(seen.Issues).Select(issue => issue.Message).Distinct().ToArray();

        return new MeasureReport(options.From, options.Seconds, measurements, issues, options.Columns, options.Rows);
    }

    /// <summary>A color's two frames, or a number's as gray where it varies across the picture; null for a number that does not.</summary>
    private static float[][]? Pictured(ComponentStats[] picture, float[] first, float[] last, MeasuredSocket socket, int pixels)
    {
        if (socket.Width == 3) return [Frame(first, socket.Offset, pixels), Frame(last, socket.Offset, pixels)];
        if (socket.Width != 1 || !picture[socket.Offset].Across) return null;

        return [Gray(first, socket.Offset, pixels), Gray(last, socket.Offset, pixels)];
    }

    private static float[] Gray(float[] frame, int offset, int pixels)
    {
        var rgb = new float[pixels * 3];

        for (var p = 0; p < pixels; p++)
            rgb[p * 3] = rgb[p * 3 + 1] = rgb[p * 3 + 2] = frame[offset * pixels + p];

        return rgb;
    }

    /// <summary>One color's pixels out of a kept frame, which holds every component a pixel apart.</summary>
    private static float[] Frame(float[] first, int offset, int pixels)
    {
        var rgb = new float[pixels * 3];

        for (var p = 0; p < pixels; p++)
            for (var c = 0; c < 3; c++)
                rgb[p * 3 + c] = first[(offset + c) * pixels + p];

        return rgb;
    }

    private static ReadOnlySpan<ComponentStats> Sound(
        CompiledPatch program,
        MeasureOptions options,
        long steps,
        Work work,
        CancellationToken cancel)
    {
        Speed(program, options);

        var trackers = Trackers(program.OutputWidth, SoundStepped, options.SampleRate / 4d);
        var registers = program.AllocateRegisters();
        var memory = new DelayState(program, options.SampleRate);
        var aspect = (double)options.Columns / options.Rows;
        var il = program.Il;

        for (long step = 0; step < steps; step++)
        {
            if (step % Stride == 0) work.Done(Stride, cancel);

            var t = options.From + step / (double)options.SampleRate;

            if (il is null) program.Evaluate(0d, 0d, t, registers, default, memory, aspect);
            else il.Evaluate(0d, 0d, t, registers, default, memory, aspect);

            for (var c = 0; c < trackers.Length; c++) trackers[c].Add(registers[program.OutputBase + c], t);
        }

        return trackers.Select(tracker => tracker.Finish(options.Seconds)).ToArray();
    }

    /// <returns>The stats, and the first and last frames: every component's pixels, one component after another.</returns>
    private static (ComponentStats[] Stats, float[] First, float[] Last) Picture(
        CompiledPatch program,
        CompiledPatch? video,
        MeasureOptions options,
        int frames,
        Work work,
        CancellationToken cancel)
    {
        Speed(program, options);
        if (video is not null) Speed(video, options);

        var (columns, rows) = (options.Columns, options.Rows);
        var pixels = columns * rows;
        var width = program.OutputWidth;
        var aspect = (double)columns / rows;

        var trackers = Trackers(width * pixels, PictureStepped, options.FramesPerSecond / 4d);
        var low = Enumerable.Repeat(double.PositiveInfinity, width).ToArray();
        var high = Enumerable.Repeat(double.NegativeInfinity, width).ToArray();
        var sums = new double[width];
        var across = new bool[width];

        var registers = program.AllocateRegisters();
        var planes = new float[pixels * program.PlaneCount];

        var videoRegisters = video?.AllocateRegisters();
        var videoPlanes = new float[pixels * (video?.PlaneCount ?? 0)];
        var previous = new float[pixels * 3];
        var current = new float[pixels * 3];

        var il = program.Il;
        var videoIl = video?.Il;
        var first = new float[width * pixels];
        var last = new float[width * pixels];
        var frameLow = new double[width];
        var frameHigh = new double[width];

        for (var frame = 0; frame < frames; frame++)
        {
            var t = options.From + frame / (double)options.FramesPerSecond;
            var before = new FeedbackFrame(previous, columns, rows);

            if (video is not null)
            {
                for (var p = 0; p < pixels; p++)
                {
                    var (x, y) = At(p, columns, rows, aspect);
                    var cells = videoPlanes.AsSpan(p * video.PlaneCount, video.PlaneCount);

                    if (videoIl is null) video.Evaluate(x, y, t, videoRegisters, before, null, aspect, null, cells);
                    else videoIl.Evaluate(x, y, t, videoRegisters, before, null, aspect, null, cells);

                    for (var c = 0; c < 3; c++)
                        current[p * 3 + c] = (float)Saturate(videoRegisters![video.OutputBase + c]);
                }

                work.Done(pixels, cancel);
            }

            Array.Fill(frameLow, double.PositiveInfinity);
            Array.Fill(frameHigh, double.NegativeInfinity);

            for (var p = 0; p < pixels; p++)
            {
                var (x, y) = At(p, columns, rows, aspect);
                var cells = planes.AsSpan(p * program.PlaneCount, program.PlaneCount);

                if (il is null) program.Evaluate(x, y, t, registers, before, null, aspect, null, cells);
                else il.Evaluate(x, y, t, registers, before, null, aspect, null, cells);

                for (var c = 0; c < width; c++)
                {
                    var value = registers[program.OutputBase + c];
                    if (!double.IsFinite(value)) value = 0d;

                    trackers[c * pixels + p].Add(value, t);
                    if (frame == 0) first[c * pixels + p] = (float)value;
                    if (frame == frames - 1) last[c * pixels + p] = (float)value;
                    frameLow[c] = Math.Min(frameLow[c], value);
                    frameHigh[c] = Math.Max(frameHigh[c], value);
                    sums[c] += value;
                }
            }

            work.Done(pixels, cancel);

            for (var c = 0; c < width; c++)
            {
                across[c] |= frameHigh[c] > frameLow[c];
                low[c] = Math.Min(low[c], frameLow[c]);
                high[c] = Math.Max(high[c], frameHigh[c]);
            }

            (previous, current) = (current, previous);
        }

        var stats = new ComponentStats[width];
        var samples = (double)frames * pixels;

        for (var c = 0; c < width; c++)
        {
            var overall = (low[c], high[c], sums[c] / samples);

            // The pixel that moved furthest says how fast: a pattern scrolling past
            // holds its frame's average still, and the middle may be where it rests.
            var moved = trackers
                .Skip(c * pixels)
                .Take(pixels)
                .Where(tracker => tracker.Changed)
                .MaxBy(tracker => tracker.Max - tracker.Min);

            stats[c] = moved?.Finish(options.Seconds, across[c], overall)
                ?? new ComponentStats(overall.Item1, overall.Item2, overall.Item3, OverTime: false, across[c]);
        }

        return (stats, first, last);
    }

    /// <summary>Where a grid pixel's middle falls in the patch's coordinates, as the screen has them.</summary>
    private static (double X, double Y) At(int pixel, int columns, int rows, double aspect)
    {
        var (row, column) = Math.DivRem(pixel, columns);

        return ((2d * (column + 0.5d) / columns - 1d) * aspect, 1d - 2d * (row + 0.5d) / rows);
    }

    private static ComponentTracker[] Trackers(int count, double steppedShare, double fastest)
    {
        var trackers = new ComponentTracker[count];
        for (var i = 0; i < count; i++) trackers[i] = new ComponentTracker(steppedShare, fastest);
        return trackers;
    }

    /// <summary>Puts IL under a program where the runtime can build it; the numbers are the interpreter's either way.</summary>
    private static void Speed(CompiledPatch program, MeasureOptions options)
    {
        if (!options.Interpreted && RuntimeFeature.IsDynamicCodeCompiled) _ = IlCompiler.CompileOnce(program, IlParts.Whole);
    }

    private static double Saturate(double v) => double.IsFinite(v) ? Math.Clamp(v, 0d, 1d) : 0d;

    /// <summary>How far through the pass is, told to whoever asked and checked against a cancel.</summary>
    private sealed class Work(IProgress<double>? progress, long total)
    {
        private long done;

        public void Done(long evaluations, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            done += evaluations;
            if (total > 0) progress?.Report(Math.Min(1d, (double)done / total));
        }
    }
}
