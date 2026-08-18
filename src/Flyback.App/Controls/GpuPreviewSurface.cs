using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Flyback.Core.Compile;

namespace Flyback.App.Controls;

/// <summary>
/// The screen of the synth, drawn by the GPU. The patch is compiled to a
/// fragment shader and evaluated per pixel by the hardware built for it, which
/// is what stops the frame cost climbing with the size of the patch.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0018's rule — never render a frame on the UI thread — still holds, by a
/// different mechanism. <see cref="OnOpenGlRender"/> runs on the compositor's
/// render thread, and nothing here blocks the dispatcher on it, so the deadlock
/// that record documents cannot form: there is no <c>Parallel.For</c> for a
/// re-entrant paint to wait behind.
/// </para>
/// <para>
/// What that costs is a hand-off. The timer runs on the UI thread and does
/// nothing but move the clock; the render thread reads what it left. Everything
/// crossing between them goes through <see cref="gate"/>, apart from the frame
/// cost, which is one double and is exchanged atomically.
/// </para>
/// <para>
/// There is no equivalent of PreviewSurface's rest between frames and no core
/// held back for the audio callback. Neither is needed: a frame here is a few
/// dozen uniform uploads and two three-vertex draws, and the machine is
/// otherwise idle.
/// </para>
/// </remarks>
public sealed class GpuPreviewSurface : OpenGlControlBase, IPreviewSurface
{
    /// <summary>
    /// How long to wait for a graphics context before giving up on one. Avalonia
    /// initialises the control asynchronously and simply never calls back if the
    /// platform has no GL, so the absence of one can only be noticed by it not
    /// having happened.
    /// </summary>
    private static readonly TimeSpan ContextPatience = TimeSpan.FromSeconds(3);

    /// <summary>
    /// A context is occasionally lost for ordinary reasons — a driver update, a
    /// laptop switching GPUs — and rebuilding is the right answer to that. One
    /// that keeps going is not a renderer.
    /// </summary>
    private const int TolerableContextLosses = 2;

    private readonly DispatcherTimer timer;
    private readonly Stopwatch frameClock = Stopwatch.StartNew();
    private readonly Lock gate = new();

    private GpuFrameRenderer? renderer;

    private CompiledPatch program = CompiledPatch.Black;
    private PixelSize resolution = new(640, 360);
    private PixelSize controlPixels;
    private double time;
    private bool rewindPending;
    private bool dirty = true;

    private long frameCostBits;
    private int contextLosses;
    private TimeSpan waitingSince;
    private volatile bool running;
    private volatile bool finished;

    public GpuPreviewSurface()
    {
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += OnTick;
        timer.Start();
    }

    /// <summary>
    /// Raised once, on the UI thread, when this backend cannot go on. The message
    /// is written for the status bar, and the host is expected to put the CPU
    /// renderer in its place rather than to try again.
    /// </summary>
    public event Action<string>? Failed;

    /// <summary>Patch time in seconds. This is what the Time module reads.</summary>
    public double Time
    {
        get { lock (gate) return time; }
        set { lock (gate) time = value; }
    }

    /// <summary>
    /// When set, the timeline is read from here instead of accumulated from
    /// wall-clock deltas. Audio becomes the master clock while it is playing, for
    /// the reason PreviewSurface gives: sound cannot be stretched to catch up.
    /// </summary>
    public Func<double>? Clock { get; set; }

    /// <summary>Cost of the last frame, for the status readout.</summary>
    public double FrameMilliseconds => BitConverter.Int64BitsToDouble(Interlocked.Read(ref frameCostBits));

    /// <summary>Whether the frame history had to be kept at eight bits per channel.</summary>
    public bool EightBitFeedback => renderer?.EightBitFeedback ?? false;

    public PixelSize Resolution
    {
        get { lock (gate) return resolution; }
        set
        {
            lock (gate)
            {
                if (resolution == value) return;

                resolution = value;
                dirty = true;
            }
        }
    }

    public CompiledPatch Program
    {
        get { lock (gate) return program; }
        set
        {
            lock (gate)
            {
                program = value;
                dirty = true;
            }
        }
    }

    /// <summary>Rewinds to zero and clears the feedback history.</summary>
    public void Rewind()
    {
        lock (gate)
        {
            time = 0;
            rewindPending = true;
            dirty = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    // --- the clock, on the UI thread ---------------------------------------------

    private TimeSpan lastTick;

    private void OnTick(object? sender, EventArgs e)
    {
        // Measured from when this started waiting, not from when the control was
        // built. A context lost after an hour is waited for as patiently as one
        // at startup, which is what lets a driver reset be recovered from rather
        // than being read as a machine that never had a GPU.
        if (renderer is null && frameClock.Elapsed - waitingSince > ContextPatience)
        {
            Fail("No OpenGL context — the picture is being drawn on the processor.");
            return;
        }

        var now = frameClock.Elapsed;
        var delta = now - lastTick;
        lastTick = now;

        // Read here rather than on the render thread: Bounds belongs to the UI
        // thread, and the size the compositor is about to draw at is one of the
        // things the frame is a snapshot of.
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1d;
        var pixels = new PixelSize(
            Math.Max(1, (int)Math.Round(Bounds.Width * scaling)),
            Math.Max(1, (int)Math.Round(Bounds.Height * scaling)));

        lock (gate)
        {
            controlPixels = pixels;

            if (Clock is { } clock)
            {
                var driven = clock();

                // Change detection against the double we stored last tick, not a
                // numeric comparison — see PreviewSurface, where the same line is
                // and for the same reason.
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (driven == time && !dirty) return;

                time = driven;
            }
            else
            {
                // Clamped so a stall — dragging the window, a slow recompile —
                // does not jump the patch forward.
                time += Math.Min(delta.TotalSeconds, 0.1);
            }

            dirty = false;
        }

        RequestNextFrameRendering();
    }

    // --- the frame, on the render thread ------------------------------------------

    protected override void OnOpenGlInit(GlInterface gl)
    {
        if (!GpuFrameRenderer.CanRun(GlVersion))
        {
            Fail($"This machine's OpenGL ({GlVersion}) is older than the shader backend needs.");
            return;
        }

        var built = new GpuFrameRenderer(GpuFrameRenderer.DialectFor(GlVersion));

        if (built.Initialise(gl) is { } error)
        {
            built.Dispose(gl);
            Fail(error);
            return;
        }

        renderer = built;
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        renderer?.Dispose(gl);
        renderer = null;
    }

    protected override void OnOpenGlLost()
    {
        // The objects are already gone, so there is nothing to hand back — only
        // names that no longer mean anything. Handing them to the dead context to
        // be freed is the one thing that must not happen here.
        renderer?.Dispose(null);
        renderer = null;

        // The history goes with them. A feedback patch restarts from black, which
        // is what SynthRenderer.Reset does and the only honest answer: the frames
        // it was built from were in memory the driver has already reclaimed.
        waitingSince = frameClock.Elapsed;

        if (++contextLosses > TolerableContextLosses)
            Fail("The graphics context keeps being lost.");
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (renderer is not { } active || finished) return;

        CompiledPatch snapshot;
        PixelSize size, control;
        double at;
        bool rewind;

        lock (gate)
        {
            snapshot = program;
            size = resolution;
            control = controlPixels;
            at = time;
            rewind = rewindPending;
            rewindPending = false;
        }

        if (rewind) active.Rewind();

        if (active.SetPatch(gl, snapshot) is { } compileError)
        {
            Fail(compileError);
            return;
        }

        if (active.Render(gl, fb, control, size, at) is { } renderError)
        {
            Fail(renderError);
            return;
        }

        Interlocked.Exchange(ref frameCostBits, BitConverter.DoubleToInt64Bits(active.PatchMilliseconds));

        running = true;
    }

    /// <summary>
    /// Said once, on the UI thread, and then this surface stops drawing. Trying
    /// again would fail in exactly the same way every sixteen milliseconds.
    /// </summary>
    private void Fail(string message)
    {
        if (finished) return;

        finished = true;
        timer.Stop();

        Dispatcher.UIThread.Post(() => Failed?.Invoke(
            running ? message : $"{message} Falling back to the processor."));
    }
}
