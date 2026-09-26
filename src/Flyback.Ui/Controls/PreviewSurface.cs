using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Flyback.Core.Compile;
using Flyback.Core.Render;

namespace Flyback.App.Controls;

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
public sealed class PreviewSurface : Control, IPreviewSurface
{
    private readonly SynthRenderer renderer = new();
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

    public PreviewSurface()
    {
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = UncappedInterval };
        timer.Tick += OnTick;
        timer.Start();
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

    /// <summary>Cost of the last frame, which sets how long the next tick rests.</summary>
    public double FrameMilliseconds { get; private set; }

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
        renderer.Reset();
        dirty = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer.Stop();
        bitmap?.Dispose();
        bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    private async void OnTick(object? sender, EventArgs e)
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
            // Trace rather than Debug: a preview that will not render is worth
            // knowing about in a build somebody is using, and Debug is compiled
            // out of exactly those. Program.Main puts this on the terminal.
            Trace.WriteLine($"Preview render failed: {ex}");
        }
        finally
        {
            rendering = false;
            restUntil = frameClock.Elapsed + TimeSpan.FromMilliseconds(FrameMilliseconds * RestFraction);
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

        if (bufferSize != size)
        {
            backBuffer = new byte[size.Width * 4 * size.Height];
            bufferSize = size;
        }

        var buffer = backBuffer;
        var stride = size.Width * 4;

        var started = frameClock.Elapsed;
        await Task.Run(() =>
            renderer.Render(program, time, size.Width, size.Height, buffer, stride, played));
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
        meter.Mark();
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
