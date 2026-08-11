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
    private readonly SynthRenderer _renderer = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private WriteableBitmap? _bitmap;
    private byte[] _backBuffer = [];
    private PixelSize _bufferSize;
    private PixelSize _resolution = new(640, 360);
    private CompiledPatch _program = CompiledPatch.Black;
    private TimeSpan _lastTick;
    private double _frameMilliseconds;
    private bool _rendering;
    private bool _dirty = true;

    public PreviewSurface()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
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
    public double FrameMilliseconds => _frameMilliseconds;

    public PixelSize Resolution
    {
        get => _resolution;
        set
        {
            if (_resolution == value) return;

            _resolution = value;
            _dirty = true;
        }
    }

    public CompiledPatch Program
    {
        get => _program;
        set
        {
            _program = value;
            _dirty = true;
        }
    }

    /// <summary>Rewinds to zero and clears the feedback history.</summary>
    public void Rewind()
    {
        Time = 0;
        _renderer.Reset();
        _dirty = true;
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
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        // A frame still in flight means we're not keeping up; skip rather than queue.
        if (_rendering) return;

        var now = _clock.Elapsed;
        var delta = now - _lastTick;
        _lastTick = now;

        if (Clock is { } clock)
        {
            var driven = clock();

            // A stopped audio clock holds its value; nothing to redraw.
            if (driven == Time && !_dirty) return;

            Time = driven;
        }
        else if (IsPlaying)
        {
            // Clamp so a stall (dragging the window, a slow recompile) doesn't jump time.
            Time += Math.Min(delta.TotalSeconds, 0.1);
        }
        else if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _rendering = true;

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
            _rendering = false;
        }
    }

    private async Task RenderFrameAsync()
    {
        // Snapshot everything the background pass needs while still on the UI thread.
        var size = new PixelSize(Math.Max(_resolution.Width, 1), Math.Max(_resolution.Height, 1));
        var program = _program;
        var time = (float)Time;

        if (_bufferSize != size)
        {
            _backBuffer = new byte[size.Width * 4 * size.Height];
            _bufferSize = size;
        }

        var buffer = _backBuffer;
        var stride = size.Width * 4;

        var started = _clock.Elapsed;
        await Task.Run(() => _renderer.Render(program, time, size.Width, size.Height, buffer, stride));
        _frameMilliseconds = (_clock.Elapsed - started).TotalMilliseconds;

        // The control may have been resized or detached while we were rendering.
        if (_bufferSize != size) return;

        if (_bitmap is null || _bitmap.PixelSize != size)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        }

        Blit(buffer, stride, size);
        InvalidateVisual();
    }

    /// <summary>Copies the finished frame into the bitmap. Cheap enough to keep on the UI thread.</summary>
    private unsafe void Blit(byte[] buffer, int stride, PixelSize size)
    {
        using var locked = _bitmap!.Lock();

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

        if (_bitmap is null) return;

        context.DrawImage(_bitmap, new Rect(_bitmap.Size), Letterbox(area, _bitmap.Size));
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
