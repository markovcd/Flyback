using System.Runtime.InteropServices.JavaScript;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Ui.Audio;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Render;

namespace Flyback.Editor.Web;

/// <summary>
/// The sound in a page (ADR-0162): each edit handed as text to the web viewer's worker,
/// which plays it, and the clock read from how far its speaker has got.
/// </summary>
/// <remarks>
/// The program is compiled here as well, only for its live inputs and its size: the block
/// the knobs and the keys write into is this side's, and what changes in it is handed
/// over once a frame. Nothing is recorded and no preset is auditioned in a page.
/// </remarks>
internal sealed partial class PageSound : UnplayedSound
{
    private float aspect = 1f;
    private float gain = 1f;

    /// <summary>What the worker was last told each value in <see cref="Live"/> is; NaN for never.</summary>
    private float[] told = [];

    /// <summary>The files last handed over, so an edit hands them again only when the patch names others.</summary>
    private ISampleLibrary? handed;

    /// <summary>The Meters the picture reads and the charts it draws, as the worker was last told, and the number their readings come back under.</summary>
    private string[] watched = [];
    private (Guid Node, float Window, ChartKind Chart)[] charted = [];
    private int watching;

    public override double Time => JsTime();

    public override float Aspect
    {
        get => aspect;
        set
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (value == aspect) return;

            aspect = value;
            JsAspect(value);
        }
    }

    public override float Gain
    {
        get => gain;
        set
        {
            gain = Math.Clamp(value, 0f, 1f);
            JsGain(gain);
        }
    }

    /// <summary>What the worker plays at, which it also lowers itself when the sound keeps falling behind.</summary>
    public override int Oversample
    {
        get => JsOversampleNow() is > 0 and var factor ? factor : AudioRenderer.DefaultOversample;
        set
        {
            if (value != Oversample) JsOversample(value);
        }
    }

    public override double Speed => IsRunning ? JsSpeed() : 0;

    public override void Start()
    {
        base.Start();
        Tell();
        JsStart(SampleRate);
    }

    public override void Stop()
    {
        base.Stop();
        JsStop();
    }

    public override void SeekTo(double seconds) => JsSeek(double.IsFinite(seconds) ? Math.Max(0, seconds) : 0);

    protected override void Updated(Patch patch, ISampleLibrary? samples)
    {
        told = new float[Live.Count];
        Array.Fill(told, float.NaN);

        if (!ReferenceEquals(samples, handed))
        {
            handed = samples;
            JsForget();

            if (samples is BundleFiles files)
                foreach (var (path, bytes) in files.Bytes) JsKeep(path, bytes);
        }

        JsEdit(PatchIO.ToJson(patch), aspect);
    }

    public override void Listen(CompiledPatch drawn, LiveValues watching)
    {
        Tell();
        Watch(watching, drawn.Taps);

        var readings = JsRead(this.watching);

        for (var i = 0; i < readings.Length && i < watched.Length; i++) watching.Set(watched[i], (float)readings[i]);

        var at = watched.Length;

        foreach (var tap in drawn.Taps)
        {
            var buffer = tap.Trace.Samples;
            if (at + buffer.Length > readings.Length) break;

            for (var i = 0; i < buffer.Length; i++) buffer[i] = (float)readings[at + i];
            at += buffer.Length;
        }
    }

    /// <summary>Hands the worker every value in <see cref="Live"/> that has changed since it was last told.</summary>
    private void Tell()
    {
        var keys = Live.Keys;
        var values = Live.Storage;
        List<string>? changed = null;
        List<double>? to = null;

        for (var i = 0; i < values.Length; i++)
        {
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (values[i] == told[i] || MeterSignals.Is(keys[i])) continue;

            told[i] = values[i];
            (changed ??= []).Add(keys[i]);
            (to ??= []).Add(values[i]);
        }

        if (changed is not null) JsPlay([.. changed], [.. to!]);
    }

    /// <summary>
    /// Names the Meters <paramref name="watching"/> reads and the charts <paramref name="taps"/>
    /// draws to the worker, when they are not the ones it was last told.
    /// </summary>
    private void Watch(LiveValues watching, IReadOnlyList<TapSpec> taps)
    {
        var keys = watching.Keys.Where(MeterSignals.Is).ToArray();
        (Guid Node, float Window, ChartKind Chart)[] charts = [.. taps.Select(tap => (tap.Node, tap.Window, tap.Chart))];

        if (keys.AsSpan().SequenceEqual(watched) && charts.AsSpan().SequenceEqual(charted)) return;

        watched = keys;
        charted = charts;
        this.watching = JsWatch(
            keys,
            [.. charts.Select(chart => chart.Node.ToString())],
            [.. charts.Select(chart => (double)chart.Window)],
            [.. charts.Select(chart => (int)chart.Chart)]);
    }

    [JSImport("time", Module)] private static partial double JsTime();
    [JSImport("start", Module)] private static partial void JsStart(int sampleRate);
    [JSImport("stop", Module)] private static partial void JsStop();
    [JSImport("seekTo", Module)] private static partial void JsSeek(double seconds);
    [JSImport("gain", Module)] private static partial void JsGain(double level);
    [JSImport("aspect", Module)] private static partial void JsAspect(double aspect);
    [JSImport("oversample", Module)] private static partial void JsOversample(int factor);
    [JSImport("oversampleNow", Module)] private static partial int JsOversampleNow();
    [JSImport("speed", Module)] private static partial double JsSpeed();
    [JSImport("edit", Module)] private static partial void JsEdit(string text, double aspect);
    [JSImport("keep", Module)] private static partial void JsKeep(string path, byte[] bytes);
    [JSImport("forget", Module)] private static partial void JsForget();

    [JSImport("play", Module)]
    private static partial void JsPlay(
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] keys,
        [JSMarshalAs<JSType.Array<JSType.Number>>] double[] values);

    [JSImport("watch", Module)]
    private static partial int JsWatch(
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] keys,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] charts,
        [JSMarshalAs<JSType.Array<JSType.Number>>] double[] windows,
        [JSMarshalAs<JSType.Array<JSType.Number>>] int[] kinds);

    [JSImport("read", Module)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial double[] JsRead(int number);

    private const string Module = "speakers";
}
