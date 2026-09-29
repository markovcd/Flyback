using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Gpu;

namespace Flyback.WebEditor;

/// <summary>
/// The preview in a page: an HTML canvas placed where the preview is in the layout,
/// drawn with the web viewer's WebGL calls and the desktop's GPU renderer (ADR-0162).
/// </summary>
/// <remarks>
/// A page has one thread, so a frame is drawn in the tick that moves the clock
/// rather than handed to a render thread. The canvas is the browser's, drawn above
/// Avalonia's own, so nothing of Avalonia's can overlay it.
/// </remarks>
internal sealed partial class CanvasPreview : NativeControlHost, IGpuPreview
{
    /// <summary>The tick rate with nothing asking for slower.</summary>
    private static readonly TimeSpan UncappedInterval = TimeSpan.FromMilliseconds(16);

    private readonly DispatcherTimer timer;
    private readonly Stopwatch frameClock = Stopwatch.StartNew();
    private readonly FrameRateMeter meter = new();
    private readonly WebGl gl = new();

    private JSObject? canvas;
    private GpuFrameRenderer? renderer;

    private CompiledPatch program = CompiledPatch.Black;
    private PixelSize drawn;
    private TimeSpan lastTick;
    private bool rewindPending;
    private bool dirty = true;
    private bool linking;
    private bool finished;

    /// <summary>The cue this surface holds a part of until the program's shader is built.</summary>
    private Cue? part;

    public CanvasPreview()
    {
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = UncappedInterval };
        timer.Tick += OnTick;
    }

    public event Action<string>? Failed;

    public double Time { get; set; }

    public Func<double>? Clock { get; set; }

    public double FramesPerSecond => meter.PerSecond;

    public double FrameMilliseconds { get; private set; }

    public double FrameRate
    {
        get;
        set
        {
            field = value;
            timer.Interval = value > 0 ? TimeSpan.FromSeconds(1d / value) : UncappedInterval;
        }
    }

    public PixelSize Resolution
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            dirty = true;
        }
    } = new(640, 360);

    public CompiledPatch Program
    {
        get => program;
        set
        {
            program = value;
            dirty = true;

            // An opened patch's picture is ready once its shader is: a part of the
            // cue, taken here and given back when the shader is built.
            var start = value.Cue is { Waiting: true } cue ? cue : null;
            if (ReferenceEquals(start, part)) return;

            start?.Take();
            part?.Give();
            part = start;
        }
    }

    public LiveValues Live
    {
        get;
        set
        {
            field = value;
            dirty = true;
        }
    } = LiveValues.None;

    LiveValues IPreviewSurface.Live { get => Live; set => Live = value; }

    public void Refresh() => dirty = true;

    public void Rewind()
    {
        Time = 0;
        rewindPending = true;
        dirty = true;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        canvas = CreateCanvas();
        drawn = default;

        return new JSObjectControlHandle(canvas);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // The context goes with the canvas, and its objects with it.
        renderer?.Dispose(null);
        renderer = null;
        canvas = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer.Stop();
        GivePart();

        base.OnDetachedFromVisualTree(e);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (canvas is null || finished) return;

        var now = frameClock.Elapsed;
        var delta = now - lastTick;
        lastTick = now;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        var pixels = new PixelSize(
            Math.Max(1, (int)Math.Round(Bounds.Width * scaling)),
            Math.Max(1, (int)Math.Round(Bounds.Height * scaling)));

        if (pixels != drawn)
        {
            SizeCanvas(canvas, pixels.Width, pixels.Height);
            drawn = pixels;
            dirty = true;
        }

        if (Clock is { } clock)
        {
            var driven = clock();

            // Change detection against the double stored last tick, as the desktop's surface does.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (driven == Time && !dirty && !linking) return;

            Time = driven;
        }
        else if (!program.Waiting)
        {
            // Clamped so a stall, a slow recompile say, does not jump the patch forward.
            Time += Math.Min(delta.TotalSeconds, 0.1);
        }

        dirty = false;
        Draw();
    }

    private void Draw()
    {
        if (renderer is null)
        {
            if (AttachGl(canvas!) is { Length: > 0 } refused)
            {
                Fail(refused);
                return;
            }

            var built = new GpuFrameRenderer(GlslDialect.GlslEs300, backgroundLinks: true);

            if (built.Initialise(gl) is { } error)
            {
                built.Dispose(gl);
                Fail(error);
                return;
            }

            renderer = built;
        }

        if (rewindPending)
        {
            rewindPending = false;
            renderer.Rewind();
        }

        if (renderer.SetPatch(gl, program) is { } compileError)
        {
            Fail(compileError);
            return;
        }

        linking = renderer.Linking;

        if (!linking) GivePart();

        var started = frameClock.Elapsed;

        if (renderer.Render(
                gl,
                0,
                new SurfaceSize(drawn.Width, drawn.Height),
                new SurfaceSize(Resolution.Width, Resolution.Height),
                Time,
                Live) is { } renderError)
        {
            Fail(renderError);
            return;
        }

        FrameMilliseconds = (frameClock.Elapsed - started).TotalMilliseconds;
        meter.Mark();
    }

    private void GivePart()
    {
        part?.Give();
        part = null;
    }

    /// <summary>Said once, and then this surface stops drawing.</summary>
    private void Fail(string message)
    {
        if (finished) return;

        finished = true;
        timer.Stop();
        GivePart();

        Dispatcher.UIThread.Post(() => Failed?.Invoke($"{message} Falling back to the processor."));
    }

    [JSImport("createCanvas", PageModule.Name)]
    private static partial JSObject CreateCanvas();

    [JSImport("sizeCanvas", PageModule.Name)]
    private static partial void SizeCanvas(JSObject canvas, int width, int height);

    /// <summary>Binds the web viewer's GL calls to <paramref name="target"/>; null or empty where it has a context.</summary>
    [JSImport("attachGl", PageModule.Name)]
    private static partial string? AttachGl(JSObject target);
}
