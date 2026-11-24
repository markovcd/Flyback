using System.Diagnostics;
using Flyback.Core;
using Flyback.Gpu;
using Flyback.Plugins.Settings;
using Flyback.Plugins.Video;

namespace Flyback.Editor.Capture;

/// <summary>The preview's frames, going to a picture output as they are drawn.</summary>
/// <remarks>
/// <see cref="Accept"/> is the render thread and does one copy; turning the frame
/// round and handing it to the output is this class's own thread, which skips frames
/// rather than queue them. The output is opened at the first frame's size, and
/// opened again whenever the preview's size changes. Each frame's moment is how long
/// sending has run, the one clock live that never jumps back on a rewind.
/// </remarks>
internal sealed class PictureSender : IFrameSink, IDisposable
{
    /// <summary>How long the thread waits for a frame before looking whether it should stop.</summary>
    private static readonly TimeSpan Idle = TimeSpan.FromMilliseconds(100);

    private readonly IVideoOutput output;
    private readonly SettingValues values;
    private readonly double framesPerSecond;
    private readonly Action<string> failed;

    private readonly Stopwatch clock = Stopwatch.StartNew();
    /// <summary>Pulsed by each frame drawn, so the thread need not poll.</summary>
    private readonly object drawn = new();

    /// <summary>Whether a frame was drawn since the thread last looked; held under <see cref="drawn"/>.</summary>
    private bool fresh;
    private readonly Thread worker;

    /// <summary>The frames waiting, at the size they were drawn; replaced, not resized, when the size changes.</summary>
    private Waiting? waiting;

    private volatile bool stopping;

    /// <summary>The worker thread's own: the open feed, its size, and the frame turned round.</summary>
    private IVideoFeed? feed;
    private int feedWidth, feedHeight;
    private byte[] bgra = [];
    private TimeSpan? lastSent;

    /// <param name="framesPerSecond">The most frames a second sent, and the rate the output is told to expect.</param>
    /// <param name="failed">Told once, on the sending thread, why sending stopped, when the output refuses or throws.</param>
    public PictureSender(IVideoOutput output, SettingValues values, double framesPerSecond, Action<string> failed)
    {
        this.output = output;
        this.values = values;
        this.framesPerSecond = framesPerSecond;
        this.failed = failed;

        worker = new Thread(Run) { IsBackground = true, Name = $"{GlobalConstants.ApplicationName} send" };
        worker.Start();
    }

    /// <summary>Frames handed to the output so far; tests read it back.</summary>
    public long Sent => Volatile.Read(ref sent);

    private long sent;

    public void Accept(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (stopping || width <= 0 || height <= 0) return;

        var box = waiting;

        if (box is null || box.Width != width || box.Height != height)
        {
            box = new Waiting(new FrameMailbox(width * height * 4), width, height);
            Volatile.Write(ref waiting, box);
        }

        if (rgba.Length < box.Frames.Writing.Length) return;

        rgba[..box.Frames.Writing.Length].CopyTo(box.Frames.Writing);
        box.Frames.Publish();
        Wake();
    }

    private void Wake()
    {
        lock (drawn)
        {
            fresh = true;
            Monitor.Pulse(drawn);
        }
    }

    private void Run()
    {
        try
        {
            while (!stopping)
            {
                lock (drawn)
                {
                    if (!fresh) Monitor.Wait(drawn, Idle);
                    fresh = false;
                }

                // Waits out the rest of the frame's interval before taking one, so a
                // frame drawn too soon is replaced by a later one rather than lost.
                while (!stopping && lastSent + TimeSpan.FromSeconds(1 / framesPerSecond) - clock.Elapsed is { Ticks: > 0 } early)
                    lock (drawn) Monitor.Wait(drawn, early);

                if (stopping || Volatile.Read(ref waiting) is not { } box) continue;

                var frame = box.Frames.TakeLatest();

                if (frame.IsEmpty) continue;

                var now = clock.Elapsed;

                Send(frame, box.Width, box.Height, now);
                lastSent = now;
            }
        }
        catch (Exception ex)
        {
            stopping = true;
            failed($"Stopped sending the picture to {output.Name}: {ex.Message}");
        }
        finally
        {
            feed?.Dispose();
            feed = null;
        }
    }

    private void Send(ReadOnlySpan<byte> rgba, int width, int height, TimeSpan time)
    {
        if (feed is null || width != feedWidth || height != feedHeight)
        {
            feed?.Dispose();
            feed = null;
            feed = output.Create(new VideoFormat(width, height, framesPerSecond), values);
            (feedWidth, feedHeight) = (width, height);
            bgra = new byte[width * height * 4];
        }

        GlFrame.ToBgra(rgba, width, height, bgra);
        feed.Send(new VideoFrame(bgra, width * 4, time));
        Interlocked.Increment(ref sent);
    }

    /// <summary>Stops taking frames and closes the output, waiting for a frame being sent to go.</summary>
    /// <remarks>Detach it from the preview first, so the render thread is not still handing it frames.</remarks>
    public void Dispose()
    {
        stopping = true;
        Wake();

        if (Thread.CurrentThread != worker) worker.Join();
    }

    /// <summary>Frames of one size, and that size.</summary>
    private sealed record Waiting(FrameMailbox Frames, int Width, int Height);
}
