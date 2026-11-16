using System.Runtime.CompilerServices;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Measure;
using Flyback.Engine.Render;

namespace Flyback.Engine.Mix;

/// <summary>
/// Listens to a patch offline for a few seconds and flags what usually goes wrong in a
/// mix: no master chain, clipping, DC, a missing sub, muddy low mids, a wide low end,
/// a loudness far off, and sound sources that stay silent.
/// </summary>
/// <remarks>
/// The sound is rendered as it is exported, so clipping and loudness are what a
/// listener gets. DC and silent sources are read off the wires before the Output,
/// since the renderer's DC blocker takes DC out of what it writes.
/// </remarks>
public static class MixCheck
{
    /// <summary>How long the sound is warmed before the window, so delays and reverbs arrive full.</summary>
    private const double Lead = 1d;

    /// <summary>How far into a render from zero the start is left to settle before it is judged silent.</summary>
    private const double Settle = 0.01d;

    /// <summary>Below this a clip is silence: -66 dBFS.</summary>
    private const float SilenceFloor = 0.0005f;

    /// <summary>A sample this close to 1 sits at full scale, where the renderer clamps it.</summary>
    private const float FullScale = 0.9999f;

    /// <summary>A mean this far off zero, -34 dBFS, eats headroom and thumps at the start and the stop.</summary>
    private const double DcLimit = 0.02d;

    /// <summary>A source whose sound moves less than this is silent.</summary>
    private const double Still = 1e-4d;

    /// <summary>Under this, in LUFS, a song is too quiet: well below the -14 streaming services play at.</summary>
    public const double QuietLufs = -23d;

    /// <summary>Over this, in LUFS, a song is squashed louder than anything is played at.</summary>
    public const double LoudLufs = -7d;

    /// <summary>Under this share of the power below 60 Hz a song has next to no sub; a mixed song has -3 to -10 dB there.</summary>
    public const double NoSub = -20d;

    /// <summary>Over this share of the power from 200 to 500 Hz, standing over the bass under it, the low mids are muddy.</summary>
    public const double Muddy = -6d;

    /// <summary>Over this, the side below 120 Hz against the middle, the bass is wide enough to cancel in mono.</summary>
    public const double Wide = -12d;

    /// <summary>The low end is judged for width only where it carries at least this share of the power, in dB.</summary>
    private const double LowEndPresent = -20d;

    /// <summary>The modules that make a master chain: one of them on the way to the Output holds the peaks.</summary>
    private static readonly HashSet<string> Limiters = ["flyback.mastering.limiter", "flyback.mastering.maximizer"];

    /// <exception cref="ArgumentOutOfRangeException">The window is not above 0 and up to <see cref="MixOptions.MaxSeconds"/>, or starts before 0.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancel"/> fired.</exception>
    /// <param name="patch"></param>
    /// <param name="options"></param>
    /// <param name="modules"></param>
    /// <param name="samples"></param>
    /// <param name="pictures"></param>
    /// <param name="names">What a module is called in the findings; its title where left out.</param>
    /// <param name="progress"></param>
    /// <param name="cancel"></param>
    public static MixReport Take(
        Patch patch,
        MixOptions options,
        ModuleCatalog? modules = null,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        Func<Guid, string>? names = null,
        IProgress<double>? progress = null,
        CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(options);

        if (!(options.Seconds > 0d && options.Seconds <= MixOptions.MaxSeconds))
            throw new ArgumentOutOfRangeException(nameof(options), options.Seconds, $"A window runs above 0 and up to {MixOptions.MaxSeconds} seconds.");

        if (!double.IsFinite(options.From) || options.From < 0d)
            throw new ArgumentOutOfRangeException(nameof(options), options.From, "A window starts at 0 seconds or later.");

        var catalog = modules ?? NodeCatalog.Current;

        string Named(Guid id) =>
            names?.Invoke(id)
            ?? (patch.Find(id) is { } node && catalog.Get(node.TypeId) is { } def ? node.Title(def) : id.ToString());

        MixReport Only(MixFinding finding, IReadOnlyList<string> issues) =>
            new(options.From, options.Seconds, MixFigures.Silent, [finding], issues);

        var sink = patch.FirstOf(OutputModule.TypeId);
        var left = sink is null ? null : patch.IncomingTo(sink.Id, OutputModule.LeftPort);
        var right = sink is null ? null : patch.IncomingTo(sink.Id, OutputModule.RightPort);

        if (left is null && right is null)
        {
            return Only(
                new MixFinding(MixProblem.NoSound, MixSeverity.Notice,
                    "Nothing is wired into the Output's left or right, so there is no sound to check.", []),
                []);
        }

        var compiled = patch.CompileForAudio(catalog, samples, pictures);
        var issues = compiled.Issues.Select(i => i.Message).ToArray();

        if (compiled.HasErrors)
        {
            return Only(
                new MixFinding(MixProblem.NoSound, MixSeverity.Warning,
                    "The patch does not compile, so there is nothing to hear: " + string.Join(" | ", issues), []),
                issues);
        }

        var feeding = Upstream(patch, [.. new[] { left, right }.OfType<Connection>().Select(c => c.SourceNode)]);
        var findings = new List<MixFinding>();

        // A song has a length; a patch without one is a sketch or a teaching patch,
        // where a missing sub or a melody in the low mids is no fault.
        var song = patch.Length is not null;

        if (!feeding.Any(id => patch.Find(id) is { Off: false } node && Limiters.Contains(node.TypeId)))
        {
            findings.Add(new MixFinding(MixProblem.NoMasterChain, song ? MixSeverity.Warning : MixSeverity.Notice,
                "No master chain: no Limiter or Maximizer stands between the mix and the Output. "
                + "Every song wants one, after a Compressor and an EQ, to hold its peaks and set its loudness.",
                []));
        }

        var work = new Steps(progress);
        var clip = Render(compiled.Program, options, work, cancel);

        var settle = options.From > 0d ? 0 : (int)(Settle * options.SampleRate) * 2;

        if (Peak(clip.AsSpan(Math.Min(settle, clip.Length))) < SilenceFloor)
        {
            findings.Insert(0, new MixFinding(MixProblem.NoSound, MixSeverity.Warning,
                $"Silence: nothing above -66 dBFS reached the speakers in the {MeasurementWords.Number(options.Seconds)} s "
                + $"from {MeasurementWords.Number(options.From)} s. Check the Output's volume, or start the window later.",
                []));

            return new MixReport(options.From, options.Seconds, MixFigures.Silent, Ordered(findings), issues);
        }

        var meter = new LoudnessMeter(options.SampleRate, OutputModule.AudioChannels);
        meter.Add(clip);

        var spectrum = MixSpectrum.Of(clip, options.SampleRate);
        var clips = Clips(clip);
        var truePeak = meter.TruePeak <= 0d ? double.NegativeInfinity : 20d * Math.Log10(meter.TruePeak);

        var (dcLeft, dcRight, silent) = Wires(patch, options, catalog, samples, pictures, left, right, feeding, work, cancel);

        var figures = new MixFigures(
            meter.Integrated,
            truePeak,
            clips,
            spectrum?.Sub ?? double.NegativeInfinity,
            spectrum?.LowMids ?? double.NegativeInfinity,
            spectrum?.LowEndWidth ?? double.NegativeInfinity,
            dcLeft,
            dcRight);

        if (clips > 0)
        {
            findings.Add(new MixFinding(MixProblem.Clipping, MixSeverity.Warning,
                $"Clipping: the sound sat at full scale {clips} time{(clips == 1 ? string.Empty : "s")}. "
                + "Turn the mix down before the Output, or put a Limiter last.",
                []));
        }
        else if (truePeak > 0d)
        {
            findings.Add(new MixFinding(MixProblem.Clipping, MixSeverity.Notice,
                $"True peak {MixWords.Decibels(truePeak)} dBTP: between the samples the sound goes over full scale, "
                + "and an MP3 or AAC of it may clip. A Limiter's ceiling at -1 dB keeps it under.",
                []));
        }

        foreach (var (side, mean, wire) in new[] { ("left", dcLeft, left), ("right", dcRight, right) })
        {
            if (mean is not { } offset || Math.Abs(offset) < DcLimit || wire is null) continue;
            if (side == "right" && left is not null && right is not null && left.SourceNode == right.SourceNode && left.SourcePort == right.SourcePort) continue;

            findings.Add(new MixFinding(MixProblem.DcOffset, MixSeverity.Warning,
                $"DC offset: what {Named(wire.SourceNode)} sends the Output's {side} averages {MeasurementWords.Number(Math.Round(offset, 3))}, "
                + "not 0. It eats headroom and thumps when the sound starts and stops; take out whatever constant is added in.",
                [wire.SourceNode]));
        }

        if (spectrum is { } heard)
        {
            if (song && heard.Sub < NoSub)
            {
                findings.Add(new MixFinding(MixProblem.NoSub, MixSeverity.Notice,
                    $"No sub: only {MixWords.Decibels(heard.Sub)} dB of the power is below 60 Hz, so a subwoofer has next to nothing to play. "
                    + "A sine an octave under the bass, kept in mono, gives it weight.",
                    []));
            }

            if (song && heard.LowMids > Muddy && heard.LowMids > heard.Bass && heard.LowMids > heard.Upper)
            {
                findings.Add(new MixFinding(MixProblem.Muddy, MixSeverity.Notice,
                    $"Muddy: {MixWords.Decibels(heard.LowMids)} dB of the power sits from 200 to 500 Hz, more than in the bass or "
                    + "everything above. Cut a few dB there with an EQ, or high-pass the parts that are not the bass.",
                    []));
            }

            if (heard.LowEnd > LowEndPresent && heard.LowEndWidth > Wide)
            {
                findings.Add(new MixFinding(MixProblem.WideLowEnd, MixSeverity.Warning,
                    $"Wide low end: below 120 Hz the difference between left and right is {MixWords.Decibels(heard.LowEndWidth)} dB "
                    + "against what they share, so the bass thins out or cancels in mono and on one subwoofer. "
                    + "Keep the bass in the middle: a Width module with 'mono below' at about 120 Hz.",
                    []));
            }
        }

        if (double.IsNegativeInfinity(figures.Lufs) || figures.Lufs < QuietLufs)
        {
            findings.Add(new MixFinding(MixProblem.TooQuiet, MixSeverity.Notice,
                $"Quiet: {MixWords.Lufs(figures.Lufs)}, where music is played at about -14 LUFS. Bring it up before the Limiter.",
                []));
        }
        else if (figures.Lufs > LoudLufs)
        {
            findings.Add(new MixFinding(MixProblem.TooLoud, MixSeverity.Warning,
                $"Loud: {MixWords.Lufs(figures.Lufs)}, squashed past the -14 LUFS music is played at, so streaming services "
                + "will turn it down and only the squashing is left. Ease the Limiter or Maximizer off.",
                []));
        }

        if (silent.Count > 0)
        {
            findings.Add(new MixFinding(MixProblem.SilentModule, MixSeverity.Notice,
                $"No sound from {MixWords.List(silent.Select(Named))}: on the way to the Output, but held still the whole window "
                + "with nothing played in. Check what triggers or plays it, or start the window where it comes in.",
                silent));
        }

        progress?.Report(1d);

        return new MixReport(options.From, options.Seconds, figures, Ordered(findings), issues);
    }

    /// <summary>Warnings first, each kind in the order <see cref="MixProblem"/> lists them.</summary>
    private static MixFinding[] Ordered(List<MixFinding> findings) =>
        [.. findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Problem)];

    /// <summary>Every module whose output reaches any of <paramref name="from"/>, them included.</summary>
    private static HashSet<Guid> Upstream(Patch patch, IReadOnlyList<Guid> from)
    {
        var into = patch.Connections.ToLookup(c => c.TargetNode, c => c.SourceNode);
        var seen = new HashSet<Guid>(from);
        var waiting = new Queue<Guid>(from);

        while (waiting.TryDequeue(out var node))
        {
            foreach (var source in into[node])
                if (seen.Add(source)) waiting.Enqueue(source);
        }

        return seen;
    }

    /// <summary>Renders the window as it is exported, warmed for <see cref="Lead"/> before it.</summary>
    private static float[] Render(CompiledPatch program, MixOptions options, Steps work, CancellationToken cancel)
    {
        var renderer = new AudioRenderer(options.SampleRate);

        if (RuntimeFeature.IsDynamicCodeCompiled) _ = IlCompiler.CompileOnce(program, IlParts.Whole);

        var begin = Math.Max(0d, options.From - Lead);
        renderer.SeekTo(begin);

        var warm = (long)Math.Round((options.From - begin) * options.SampleRate);
        var frames = (long)Math.Round(options.Seconds * options.SampleRate);
        var total = warm + frames;
        var block = new float[4096 * OutputModule.AudioChannels];
        var clip = new float[frames * OutputModule.AudioChannels];
        long done = 0;

        while (done < total)
        {
            cancel.ThrowIfCancellationRequested();

            var count = (int)Math.Min(block.Length / OutputModule.AudioChannels, total - done);
            var buffer = block.AsSpan(0, count * OutputModule.AudioChannels);

            renderer.Render(program, buffer);

            var into = done - warm;

            if (into >= 0)
            {
                buffer.CopyTo(clip.AsSpan((int)(into * OutputModule.AudioChannels)));
            }
            else if (into + count > 0)
            {
                var kept = (int)(into + count);
                buffer[^(kept * OutputModule.AudioChannels)..].CopyTo(clip);
            }

            done += count;
            work.Report(0.5d * done / total);
        }

        return clip;
    }

    /// <summary>
    /// The mean of each wire into the Output, and the sound sources on the way to it
    /// that held still, read off the patch over the same window.
    /// </summary>
    private static (double? Left, double? Right, IReadOnlyList<Guid> Silent) Wires(
        Patch patch,
        MixOptions options,
        ModuleCatalog catalog,
        ISampleLibrary? samples,
        IImageLibrary? pictures,
        Connection? left,
        Connection? right,
        HashSet<Guid> feeding,
        Steps work,
        CancellationToken cancel)
    {
        var sources = feeding
            .Where(id => patch.Find(id) is { Off: false } node
                && catalog.Get(node.TypeId) is { } def
                && def.Outputs.Count > 0
                && (def.Category == ModuleCategories.Oscillators || node.TypeId == SourceModules.SampleTypeId))
            .ToList();

        var chosen = new[] { left, right }.OfType<Connection>().Select(c => c.SourceNode).Concat(sources).Distinct().ToArray();

        var report = Measurements.Take(
            patch,
            new MeasureOptions(options.Seconds, options.From, chosen, Columns: 1, Rows: 1, FramesPerSecond: 1),
            catalog,
            samples,
            pictures,
            new Progress<double>(done => work.Report(0.5d + 0.5d * done)),
            cancel);

        double? Mean(Connection? wire) =>
            wire is null ? null : report.Measurements.FirstOrDefault(m => m.Node == wire.SourceNode && m.Port == wire.SourcePort) is { Sound: [var one] } ? one.Mean : null;

        var silent = sources
            .Where(id => report.Measurements.Where(m => m.Node == id).ToArray() is { Length: > 0 } outputs
                && outputs.All(m => m.Sound.All(c => c.Max - c.Min < Still)))
            .OrderBy(id => patch.Nodes.FindIndex(n => n.Id == id))
            .ToArray();

        return (Mean(left), Mean(right), silent);
    }

    /// <summary>How many runs of two or more frames sat at full scale, in either channel.</summary>
    private static int Clips(ReadOnlySpan<float> samples)
    {
        var runs = 0;

        for (var channel = 0; channel < OutputModule.AudioChannels; channel++)
        {
            var length = 0;

            for (var i = channel; i < samples.Length; i += OutputModule.AudioChannels)
            {
                if (Math.Abs(samples[i]) >= FullScale)
                {
                    if (++length == 2) runs++;
                }
                else
                {
                    length = 0;
                }
            }
        }

        return runs;
    }

    private static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples) peak = Math.Max(peak, Math.Abs(sample));
        return peak;
    }

    /// <summary>How far through the check is, rendering then measuring.</summary>
    private sealed class Steps(IProgress<double>? progress)
    {
        private int shown = -1;

        public void Report(double done)
        {
            var percent = (int)(done * 100);
            if (percent == shown) return;

            shown = percent;
            progress?.Report(done);
        }
    }
}
