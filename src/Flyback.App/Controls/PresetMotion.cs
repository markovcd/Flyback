using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.App.Controls;

/// <summary>
/// A preset's picture playing on its tile in place of the still, for as long as
/// the preset is being tried.
/// </summary>
/// <remarks>
/// Drawn by the interpreter at the thumbnail's size on a thread of its own, the
/// way the still was, and handed to the tile a frame at a time. Paced by the
/// clock it is given — the auditioned sound's, where there is one, so the
/// picture moves with what is heard — and by a stopwatch otherwise.
/// </remarks>
internal sealed class PresetMotion : IDisposable
{
    private static readonly TimeSpan FrameTime = TimeSpan.FromSeconds(1d / 30d);

    private readonly CancellationTokenSource stop = new();
    private readonly Image picture;

    /// <summary>What the tile showed before, put back when this stops. Null until the first frame.</summary>
    private IImage? still;

    /// <summary>Whether a frame has been put on the tile, and the still has to be put back.</summary>
    private bool showing;

    private readonly int width;
    private readonly int height;

    private PresetMotion(Image picture, int width, int height)
    {
        this.picture = picture;
        this.width = width;
        this.height = height;
    }

    /// <summary>
    /// Plays <paramref name="opened"/> on <paramref name="picture"/> until disposed.
    /// A patch with no picture, or one that will not compile, leaves the tile as it is.
    /// </summary>
    /// <param name="width">How wide the frames are drawn, a tile's width by default.</param>
    /// <param name="height">How tall they are drawn, which is what sets the aspect.</param>
    public static PresetMotion Play(
        Opened opened,
        Image picture,
        Func<double>? clock,
        int width = PresetThumbnails.Width,
        int height = PresetThumbnails.Height)
    {
        var motion = new PresetMotion(picture, width, height);

        _ = Task.Run(() => motion.RunAsync(opened, clock ?? WallClock(), motion.stop.Token));

        return motion;
    }

    private static Func<double> WallClock()
    {
        var watch = Stopwatch.StartNew();
        return () => watch.Elapsed.TotalSeconds;
    }

    private async Task RunAsync(Opened opened, Func<double> clock, CancellationToken token)
    {
        CompiledPatch program;

        try
        {
            if (!opened.Patch.Reaches().Picture) return;

            var video = opened.Patch.CompileForVideo(samples: opened.Samples, pictures: opened.Pictures);
            if (video.HasErrors) return;

            program = video.Program;
        }
        catch (Exception)
        {
            return;
        }

        var stride = width * 4;

        var renderer = new SynthRenderer();
        var pixels = new byte[stride * height];
        var bitmap = await Dispatcher.UIThread.InvokeAsync(() => new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque));

        while (!token.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();

            try
            {
                renderer.Render(program, clock(), width, height, pixels, stride);
            }
            catch (Exception)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => Show(bitmap, pixels, stride));

            var left = FrameTime - Stopwatch.GetElapsedTime(started);

            try
            {
                if (left > TimeSpan.Zero) await Task.Delay(left, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Puts a frame on the tile. On the UI thread, which is also the only one that stops this.</summary>
    private void Show(WriteableBitmap bitmap, byte[] pixels, int stride)
    {
        if (stop.IsCancellationRequested) return;

        using (var locked = bitmap.Lock())
        {
            for (var y = 0; y < height; y++)
                Marshal.Copy(pixels, y * stride, locked.Address + y * locked.RowBytes, stride);
        }

        // Asked every frame rather than once: the still may arrive while this
        // plays, and it is the one to put back.
        if (picture.Source != bitmap)
        {
            still = picture.Source;
            picture.Source = bitmap;
        }

        showing = true;

        picture.InvalidateVisual();
    }

    /// <summary>Stops the picture and puts the still back. Called on the UI thread.</summary>
    public void Dispose()
    {
        stop.Cancel();

        if (showing) picture.Source = still;

        showing = false;
    }
}
