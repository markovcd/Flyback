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
/// The screen of the synth: renders the compiled patch on a background thread
/// and blits the result, letterboxed, into whatever space the layout gives it.
/// </summary>
/// <remarks>
/// Frames are deliberately never rendered on the UI thread. The renderer uses
/// <c>Parallel.For</c>, and blocking the Avalonia dispatcher on it deadlocks:
/// the dispatcher pumps messages while waiting, a paint re-enters, and the
/// compositor batch that paint waits on can never be committed.
/// </remarks>
public sealed class PreviewSurface : Control
{
    private readonly SynthRenderer renderer = new();
    private readonly DispatcherTimer timer;
    private readonly Stopwatch frameClock = Stopwatch.StartNew();

    private WriteableBitmap? bitmap;
    private byte[] backBuffer = [];
    private PixelSize bufferSize;
    private PixelSize resolution = new(640, 360);
    private CompiledPatch activeProgram = CompiledPatch.Black;
    private TimeSpan lastTick;
    private TimeSpan restUntil;
    private bool rendering;
    private bool dirty = true;

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
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += OnTick;
        timer.Start();
    }

    /// <summary>Patch time in seconds. This is what the Time module reads.</summary>
    public double Time { get; private set; }

    public bool IsPlaying { get; set; } = true;

    /// <summary>
    /// When set, the timeline is read from here instead of accumulated from
    /// wall-clock deltas. Audio becomes the master clock while it is playing —
    /// sound cannot be stretched to catch up, whereas video can drop a frame,
    /// and without this a patch pulsing in both eye and ear visibly drifts.
    /// </summary>
    public Func<double>? Clock { get; set; }

    /// <summary>Cost of the last frame, for the status readout.</summary>
    public double FrameMilliseconds { get; private set; }

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

    /// <summary>Rewinds to zero and clears the feedback history.</summary>
    public void Rewind()
    {
        Time = 0;
        renderer.Reset();
        dirty = true;
    }

    /// <summary>Renders a one-off frame at an arbitrary size and writes it as a PNG.</summary>
    public static Task SaveFrameAsync(CompiledPatch program, double time, string path, PixelSize size) =>
        Task.Run(() =>
        {
            var stride = size.Width * 4;
            var buffer = new byte[stride * size.Height];

            // A fresh renderer so exporting never disturbs the live feedback buffer.
            new SynthRenderer().Render(program, (float)time, size.Width, size.Height, buffer, stride);

            PngWriter.WriteBgra(path, buffer, size.Width, size.Height, stride);
        });

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer.Stop();
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
        else if (IsPlaying)
        {
            // Clamp so a stall (dragging the window, a slow recompile) doesn't jump time.
            Time += Math.Min(delta.TotalSeconds, 0.1);
        }
        else if (!dirty)
        {
            return;
        }

        dirty = false;
        rendering = true;

        try
        {
            await RenderFrameAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Preview render failed: {ex}");
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
        var time = (float)Time;

        if (bufferSize != size)
        {
            backBuffer = new byte[size.Width * 4 * size.Height];
            bufferSize = size;
        }

        var buffer = backBuffer;
        var stride = size.Width * 4;

        var started = frameClock.Elapsed;
        await Task.Run(() => renderer.Render(program, time, size.Width, size.Height, buffer, stride));
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

    /// <summary>Largest rect of the image's aspect that fits in <paramref name="area"/>, centred.</summary>
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
