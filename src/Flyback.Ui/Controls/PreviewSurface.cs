using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;

namespace Flyback.Ui.Controls;

/// <summary>
/// The screen of the synth: renders the compiled patch on a background thread and
/// blits the result, letterboxed, into whatever space the layout gives it.
/// </summary>
/// <remarks>
/// Frames are never rendered on the UI thread: the renderer uses
/// <c>Parallel.For</c>, and blocking the Avalonia dispatcher on it deadlocks — the
/// dispatcher pumps messages while waiting, a paint re-enters, and the compositor
/// batch that paint waits on can never be committed.
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The bitmap goes when the control leaves the visual tree.")]
public class PreviewSurface : Control, IPreviewSurface
{
    private readonly IFrameRenderer renderer;
    private readonly Action reset;
    private readonly string step;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch frameClock = Stopwatch.StartNew();

    /// <summary>The tick rate with nothing asking for slower — as fast as the dispatcher allows.</summary>
    private static readonly TimeSpan UncappedInterval = TimeSpan.FromMilliseconds(16);

    private WriteableBitmap? bitmap;
    private byte[] backBuffer = [];
    private PixelSize bufferSize;
    private PixelSize resolution = new(640, 360);
    private CompiledPatch activeProgram = CompiledPatch.Black;
    private LiveValues live = LiveValues.None;
    private TimeSpan lastTick;
    private TimeSpan restUntil;
    private bool rendering;
    /// <summary>
    /// Volatile because <see cref="Refresh"/> is called from outside this
    /// thread as well: a note from a MIDI device arrives on the driver's thread
    /// and asks for a frame from there. Everything else that sets it is the UI
    /// thread, and the GPU surface guards its own copy with the lock it already
    /// has.
    /// </summary>
    private volatile bool dirty = true;

    /// <summary>
    /// Fraction of a frame's own cost to idle for afterwards. A frame at 960x540
    /// costs longer than the timer interval, so without this the preview renders
    /// back to back and the machine never has a quiet moment — which is when the
    /// audio callback misses its deadline.
    /// </summary>
    /// <remarks>
    /// Proportional rather than a fixed cap, so it scales itself: a cheap patch
    /// rests a millisecond and still reaches the full 60 Hz, while an expensive
    /// one settles at about two thirds of the rate it would otherwise manage and
    /// leaves the rest of the machine alone.
    /// </remarks>
    private const double RestFraction = 0.5;

    /// <summary>
    /// The fraction of a frame's cost to rest for afterwards: <see cref="RestFraction"/>
    /// for a frame the processor draws, and none for one the graphics card draws, which
    /// leaves the processor free while it waits.
    /// </summary>
    private protected virtual double Rest => RestFraction;

    public PreviewSurface() : this(new SynthRenderer(), "CPU frame")
    {
    }

    private PreviewSurface(SynthRenderer renderer, string step) : this(renderer, renderer.Reset, step)
    {
    }

    /// <param name="renderer">Draws a frame into memory, on a thread of the pool.</param>
    /// <param name="reset">Clears the renderer's feedback history.</param>
    /// <param name="step">What a frame is called in a stall trace.</param>
    private protected PreviewSurface(IFrameRenderer renderer, Action reset, string step)
    {
        this.renderer = renderer;
        this.reset = reset;
        this.step = step;

        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = UncappedInterval };
        timer.Tick += OnTick;
    }

    /// <summary>Patch time in seconds. This is what the Time module reads.</summary>
    public double Time { get; set; }

    /// <summary>
    /// When set, the timeline is read from here instead of accumulated from
    /// wall-clock deltas. Audio becomes the master clock while it is playing —
    /// sound cannot be stretched to catch up, whereas video can drop a frame,
    /// and without this a patch pulsing in both eye and ear visibly drifts.
    /// </summary>
    public Func<double>? Clock { get; set; }

    private readonly FrameRateMeter meter = new();

    /// <summary>Frames reaching the screen each second, for the status readout.</summary>
    public double FramesPerSecond => meter.PerSecond;

    public long Frames => meter.Drawn;

    /// <summary>Cost of the last frame, which sets how long the next tick rests.</summary>
    public double FrameMilliseconds { get; private set; }

    public double SlowestFrameMilliseconds => meter.Slowest;

    /// <summary>How often the preview redraws itself, or 0 to run as fast as the dispatcher allows.</summary>
    public double FrameRate
    {
        get;
        set
        {
            field = value;
            timer.Interval = value > 0 ? TimeSpan.FromSeconds(1d / value) : UncappedInterval;
        }
    }

    public virtual Smoothing Smoothing { get; set; }

    public PixelSize Resolution
    {
        get => resolution;
        set
        {
            if (resolution == value) return;

            resolution = value;
            dirty = true;
        }
    }

    public CompiledPatch Program
    {
        get => activeProgram;
        set
        {
            activeProgram = value;
            dirty = true;
        }
    }

    /// <summary>What is being played into the patch as it is drawn.</summary>
    internal LiveValues Live
    {
        get => live;
        set
        {
            live = value;
            dirty = true;
        }
    }

    LiveValues IPreviewSurface.Live { get => Live; set => Live = value; }

    /// <summary>A key moved, so the next tick has something to draw after all.</summary>
    public void Refresh() => dirty = true;

    /// <summary>Rewinds to zero and clears the feedback history.</summary>
    public void Rewind()
    {
        Time = 0;
        reset();
        dirty = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer.Stop();
        bitmap?.Dispose();
        bitmap = null;
        detached = true;

        if (!rendering) Retire();

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Whether the control has left the tree, after which it never draws again.</summary>
    private bool detached;

    /// <summary>
    /// Hands back what the renderer holds, once the control has left the tree and no
    /// frame is in flight.
    /// </summary>
    private protected virtual void Retire()
    {
    }

    /// <summary>A frame that threw. The processor's renderer says so on the trace and draws the next.</summary>
    private protected virtual void OnFrameFailed(Exception ex) =>
        // Trace rather than Debug: a preview that will not render is worth
        // knowing about in a build somebody is using, and Debug is compiled
        // out of exactly those. Program.Main puts this on the terminal.
        Trace.WriteLine($"Preview render failed: {ex}");

    /// <summary>Stops drawing for good, as when the renderer has failed.</summary>
    private protected void Stop() => timer.Stop();

    private async void OnTick(object? sender, EventArgs e) => await Tick();

    /// <summary>
    /// What the timer does each time it fires: draws the next frame, unless one is in
    /// flight or the last asks for a rest. A test calls it rather than waiting on a
    /// timer the headless platform need not fire.
    /// </summary>
    internal async Task Tick()
    {
        // A frame still in flight means we're not keeping up; skip rather than queue.
        if (rendering) return;

        // And having just finished an expensive one, let the machine breathe.
        if (frameClock.Elapsed < restUntil) return;

        var now = frameClock.Elapsed;
        var delta = now - lastTick;
        lastTick = now;

        // Opened and still compiling: the last frame stays and the clock stands.
        if (activeProgram.Waiting) return;

        if (Clock is { } clock)
        {
            var driven = clock();

            // A stopped audio clock holds its value; nothing to redraw. This is
            // change detection against the double we ourselves stored last tick,
            // not a numeric comparison — a tolerance here would drop real frames
            // whenever the timeline advances slowly.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (driven == Time && !dirty) return;

            Time = driven;
        }
        else
        {
            // Clamp so a stall (dragging the window, a slow recompile) doesn't jump time.
            Time += Math.Min(delta.TotalSeconds, 0.1);
        }

        dirty = false;
        rendering = true;

        try
        {
            await RenderFrameAsync();
        }
        catch (Exception ex)
        {
            OnFrameFailed(ex);
        }
        finally
        {
            rendering = false;

            if (detached) Retire();

            restUntil = frameClock.Elapsed + TimeSpan.FromMilliseconds(FrameMilliseconds * Rest);

            // A frame that outlasted a tick starts the next one now, rather than at the
            // tick after it: a 21 ms frame would otherwise wait for the 32 ms tick.
            if (!detached && Rest == 0 && frameClock.Elapsed - lastTick >= timer.Interval)
                Dispatcher.UIThread.Post(() => _ = Tick(), DispatcherPriority.Background);
        }
    }

    private async Task RenderFrameAsync()
    {
        // Snapshot everything the background pass needs while still on the UI thread.
        var size = new PixelSize(Math.Max(resolution.Width, 1), Math.Max(resolution.Height, 1));
        var program = activeProgram;
        var time = Time;

        // Snapshotted with the rest, so every row of one frame is drawn with the
        // same reference — the block itself may still be written into while the
        // rows run, and a note landing mid-frame shows up in the next one rather
        // than halfway down this one.
        var played = live;
        var smoothing = Smoothing;

        if (bufferSize != size)
        {
            backBuffer = new byte[size.Width * 4 * size.Height];
            bufferSize = size;
        }

        var buffer = backBuffer;
        var stride = size.Width * 4;

        var started = frameClock.Elapsed;
        await Task.Run(() =>
        {
            using var traced = StallTrace.Step(step);

            renderer.Render(program, time, size.Width, size.Height, buffer, stride, played);
            Smooth(smoothing, buffer, size, stride);
        });
        FrameMilliseconds = (frameClock.Elapsed - started).TotalMilliseconds;

        // The control may have been resized or detached while we were rendering.
        if (bufferSize != size) return;

        if (bitmap is null || bitmap.PixelSize != size)
        {
            bitmap?.Dispose();
            bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }

        Blit(buffer, stride, size);
        InvalidateVisual();
        meter.Mark(FrameMilliseconds);
    }

    /// <summary>Smooths the frame just drawn, on the thread that drew it.</summary>
    private protected virtual void Smooth(Smoothing smoothing, byte[] buffer, PixelSize size, int stride)
    {
        if (smoothing != Smoothing.Off) Fxaa.Apply(buffer, size.Width, size.Height, stride);
    }

    /// <summary>Copies the finished frame into the bitmap. Cheap enough to keep on the UI thread.</summary>
    private unsafe void Blit(byte[] buffer, int stride, PixelSize size)
    {
        using var locked = bitmap!.Lock();

        fixed (byte* source = buffer)
        {
            var destination = (byte*)locked.Address;

            if (locked.RowBytes == stride)
            {
                Buffer.MemoryCopy(source, destination, (long)stride * size.Height, (long)stride * size.Height);
                return;
            }

            for (var y = 0; y < size.Height; y++)
                Buffer.MemoryCopy(source + (long)y * stride, destination + (long)y * locked.RowBytes, locked.RowBytes, stride);
        }
    }

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Black, area);

        if (bitmap is null) return;

        context.DrawImage(bitmap, new Rect(bitmap.Size), Letterbox(area, bitmap.Size));
    }

    /// <summary>Largest rect of the image's aspect that fits in <paramref name="area"/>, centered.</summary>
    private static Rect Letterbox(Rect area, Size image)
    {
        if (image.Width <= 0 || image.Height <= 0 || area.Width <= 0 || area.Height <= 0) return area;

        var scale = Math.Min(area.Width / image.Width, area.Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;

        return new Rect(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2,
            width,
            height);
    }
}
