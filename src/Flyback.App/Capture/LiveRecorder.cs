using System.Diagnostics;
using Avalonia;
using Flyback.Core.Render;

namespace Flyback.App.Capture;

/// <summary>What a recording is, before it exists.</summary>
/// <param name="Path">Where it goes. The extension has already decided the container.</param>
/// <param name="Size">Frame size, or an empty size for a sound-only take.</param>
/// <param name="Channels">Zero writes a silent video, the way a video-only export does.</param>
internal readonly record struct RecordingSettings(
    string Path,
    PixelSize Size,
    double FramesPerSecond,
    int Quality,
    int SampleRate,
    int Channels)
{
    public bool HasPicture => Size.Width > 0 && Size.Height > 0;

    public bool HasSound => Channels > 0 && SampleRate > 0;
}

/// <summary>How a recording is going, for the status bar to read.</summary>
internal readonly record struct RecordingStatus(
    double Seconds,
    long Frames,
    long Duplicated,
    long AudioDropped,
    string? Stopped);

/// <summary>
/// A take: the frames the GPU drew and the samples the speakers got, going into
/// a file as they happen.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here runs on a thread that can afford it. <see cref="Accept"/> is the
/// render thread and does one copy; <see cref="WriteAudio"/> is the sound
/// callback and does one copy into a ring. Everything expensive — the color
/// conversion, the JPEG, the file — is on this class's own thread, which is
/// allowed to fall behind because <see cref="CapturePacer"/> makes falling
/// behind mean a repeated frame rather than a broken file.
/// </para>
/// <para>
/// The sound is the clock whenever there is any, because it is the one stream
/// that cannot be dropped or repeated: a sample count is an exact measure of how
/// long the take has run, and pacing the picture against it is what keeps the two
/// together over an hour. Only a silent video falls back to a stopwatch.
/// </para>
/// <para>
/// Nothing is written until the first frame is in hand. A file that opens with
/// the sound already running and the picture arriving a moment later is out of
/// step for its whole length, and the fix is simply to agree on where the take
/// starts.
/// </para>
/// </remarks>
internal sealed class LiveRecorder : IFrameSink, IAudioSink, IDisposable
{
    /// <summary>
    /// Four seconds of it. The consumer only has to keep up with a file write,
    /// so this is not a buffer against slowness — it is the margin that decides
    /// whether a scheduling hiccup is inaudible or a hole in the recording.
    /// </summary>
    private const double RingSeconds = 4d;

    /// <summary>Long enough not to spin, short enough to be inside one frame at any rate worth using.</summary>
    private static readonly TimeSpan Idle = TimeSpan.FromMilliseconds(2);

    private readonly RecordingSettings settings;
    private readonly Stream file;
    private readonly AviWriter? avi;
    private readonly WavStreamWriter? wav;
    private readonly JpegWriter? jpeg;

    private readonly AudioRing? ring;
    private readonly FrameMailbox? mailbox;
    private readonly CapturePacer? pacer;

    private readonly Stopwatch clock = new();
    private readonly Thread worker;

    /// <summary>The encoder thread's own scratch — never touched from anywhere else.</summary>
    private readonly float[] drained;
    private readonly byte[] bgra;
    private readonly MemoryStream encoded;

    private long samplesWritten;
    private long frames;
    private long duplicated;
    private volatile string? stopped;
    private volatile bool stopping;
    private bool disposed;

    public LiveRecorder(RecordingSettings settings)
    {
        if (!settings.HasPicture && !settings.HasSound)
            throw new ArgumentException("A recording has to be of something.", nameof(settings));

        this.settings = settings;

        file = File.Create(settings.Path);

        if (settings.HasPicture)
        {
            avi = new AviWriter(
                file,
                settings.Size.Width,
                settings.Size.Height,
                settings.FramesPerSecond,
                settings.HasSound ? settings.SampleRate : 0,
                settings.HasSound ? settings.Channels : 0);

            jpeg = new JpegWriter(settings.Quality);
            mailbox = new FrameMailbox(settings.Size.Width * settings.Size.Height * 4);
            pacer = new CapturePacer(settings.FramesPerSecond);

            bgra = new byte[settings.Size.Width * settings.Size.Height * 4];
            encoded = new MemoryStream(settings.Size.Width * settings.Size.Height / 4);
        }
        else
        {
            wav = new WavStreamWriter(file, settings.SampleRate, settings.Channels);
            bgra = [];
            encoded = new MemoryStream(0);
        }

        if (settings.HasSound)
        {
            var capacity = (int)(RingSeconds * settings.SampleRate * settings.Channels);
            ring = new AudioRing(capacity);
            drained = new float[Math.Min(capacity, settings.SampleRate * settings.Channels / 4)];
        }
        else
        {
            drained = [];
        }

        worker = new Thread(Run) { IsBackground = true, Name = "Flyback capture" };
        worker.Start();
    }

    /// <summary>Where the take is being written.</summary>
    public string Path => settings.Path;

    /// <summary>True while the file is still open and taking frames.</summary>
    public bool IsRunning => !stopping && stopped is null;

    public RecordingStatus Status => new(
        Elapsed,
        Volatile.Read(ref frames),
        Volatile.Read(ref duplicated),
        ring?.Dropped ?? 0,
        stopped);

    /// <summary>
    /// How long the take has run. Counted in samples wherever there are any:
    /// that is exact and a stopwatch is not, and the picture is paced against
    /// this.
    /// </summary>
    private double Elapsed => settings.HasSound
        ? Volatile.Read(ref samplesWritten) / (double)settings.Channels / settings.SampleRate
        : clock.Elapsed.TotalSeconds;

    /// <summary>
    /// The render thread's whole involvement: one copy, one atomic swap. A frame
    /// arriving at the wrong size is one from a resolution change and is ignored
    /// — the header has already committed to a size and cannot be talked out of
    /// it.
    /// </summary>
    public void Accept(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (mailbox is null || stopping || stopped is not null) return;
        if (width != settings.Size.Width || height != settings.Size.Height) return;
        if (rgba.Length < mailbox.Writing.Length) return;

        rgba[..mailbox.Writing.Length].CopyTo(mailbox.Writing);
        mailbox.Publish();
    }

    /// <summary>
    /// The sound callback's whole involvement. Called from the audio thread, so
    /// it does exactly what <see cref="AudioRing"/> does and no more.
    /// </summary>
    public void WriteAudio(ReadOnlySpan<float> interleaved)
    {
        if (stopping || stopped is not null) return;

        ring?.Write(interleaved);
    }

    /// <summary>
    /// Finishes the file. Blocks until the encoder has drained, which is what
    /// patches the header — a take abandoned without this is not a video.
    /// </summary>
    public void Stop()
    {
        if (stopping) return;

        stopping = true;
        worker.Join(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        Stop();

        avi?.Dispose();
        wav?.Dispose();
        file.Dispose();
        encoded.Dispose();
    }

    // --- the encoder thread ------------------------------------------------------

    private void Run()
    {
        // Sound-only has nothing to wait for; a video waits for its first frame
        // so that both streams start at the same instant.
        var started = !settings.HasPicture;

        if (started) clock.Restart();

        while (true)
        {
            var last = stopping;

            try
            {
                if (!started) started = TryStart();
                else Pump();
            }
            catch (Exception ex)
            {
                stopped = ex.Message;
                return;
            }

            if (last) return;

            Thread.Sleep(Idle);
        }
    }

    /// <summary>
    /// Waits for a first frame, throwing away the sound that arrived before it.
    /// Those samples belong to a moment the picture cannot show.
    /// </summary>
    private bool TryStart()
    {
        if (mailbox!.TakeLatest() is { IsEmpty: false } first)
        {
            Encode(first);
            Drain(discard: true);
            clock.Restart();

            return true;
        }

        Drain(discard: true);

        return false;
    }

    private void Pump()
    {
        Drain(discard: false);

        if (pacer is null) return;

        // Asked before anything is collected, so a moment with no frame due costs
        // no color conversion and no JPEG. The preview draws far faster than the
        // file wants, and this is where that surplus is discarded.
        var due = pacer.Due(Elapsed);
        if (due <= 0) return;

        var fresh = mailbox!.TakeLatest();
        if (!fresh.IsEmpty) Encode(fresh);

        // Cannot happen once started — the first frame is encoded before the take
        // begins — but nothing is committed until it is written, so if it ever did
        // the file would simply wait rather than skip a moment for good.
        if (encoded.Length == 0) return;

        var picture = encoded.GetBuffer().AsSpan(0, (int)encoded.Length);

        for (var i = 0; i < due; i++) avi!.WriteFrame(picture);

        pacer.Commit(due);

        Volatile.Write(ref frames, frames + due);
        Volatile.Write(ref duplicated, duplicated + due - (fresh.IsEmpty ? 0 : 1));
    }

    /// <summary>Moves everything waiting in the ring into the file, or bins it.</summary>
    private void Drain(bool discard)
    {
        if (ring is null) return;

        while (true)
        {
            var taken = ring.Read(drained);
            if (taken == 0) return;

            if (discard) continue;

            var samples = drained.AsSpan(0, taken);

            if (avi is not null) avi.WriteAudio(samples);
            else wav!.WriteAudio(samples);

            Volatile.Write(ref samplesWritten, samplesWritten + taken);
        }
    }

    /// <summary>
    /// OpenGL hands back RGBA with the first row at the bottom;
    /// <see cref="JpegWriter"/> wants BGRA with the first row at the top. Both
    /// halves of that are one pass, on this thread, where it costs nothing that
    /// anybody is waiting for.
    /// </summary>
    private void Encode(ReadOnlySpan<byte> rgba)
    {
        var width = settings.Size.Width;
        var height = settings.Size.Height;
        var stride = width * 4;

        for (var y = 0; y < height; y++)
        {
            var source = rgba.Slice((height - 1 - y) * stride, stride);
            var target = bgra.AsSpan(y * stride, stride);

            for (var x = 0; x < stride; x += 4)
            {
                target[x] = source[x + 2];
                target[x + 1] = source[x + 1];
                target[x + 2] = source[x];
                target[x + 3] = source[x + 3];
            }
        }

        encoded.SetLength(0);
        jpeg!.WriteBgra(encoded, bgra, width, height, stride);
    }
}
