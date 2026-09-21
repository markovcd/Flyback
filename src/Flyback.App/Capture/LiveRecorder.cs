using System.Diagnostics;
using Avalonia;
using Flyback.Core;
using Flyback.Core.Render;

namespace Flyback.App.Capture;

/// <summary>What a recording is, before it exists.</summary>
/// <param name="Path">Where it goes. The extension already agrees with <paramref name="Format"/>.</param>
/// <param name="Format">Which of <see cref="ClipFormats"/> it is written as.</param>
/// <param name="Size">Frame size, or an empty size for a sound-only take.</param>
/// <param name="Channels">Zero writes a silent video, the way a video-only export does.</param>
/// <param name="Ffmpeg">Where ffmpeg is, for a format that needs it.</param>
internal readonly record struct RecordingSettings(
    string Path,
    ClipFormat Format,
    PixelSize Size,
    double FramesPerSecond,
    int Quality,
    int SampleRate,
    int Channels,
    string? Ffmpeg = null)
{
    public bool HasPicture => Format.HasPicture && Size.Width > 0 && Size.Height > 0;

    public bool HasSound => Channels > 0 && SampleRate > 0;

    /// <summary>What the writer is opened against — the same shape a render uses.</summary>
    public ClipTarget Target => new(
        Path,
        Format,
        Size.Width,
        Size.Height,
        FramesPerSecond,
        Quality,
        HasSound ? SampleRate : 0,
        HasSound ? Channels : 0,
        Ffmpeg);
}

/// <summary>How a recording is going, for the status bar to read.</summary>
internal readonly record struct RecordingStatus(
    double Seconds,
    long Frames,
    long Duplicated,
    long AudioDropped,
    string? Stopped);

/// <summary>
/// A take: the frames the GPU drew and the samples the speakers got, going into a
/// file as they happen.
/// </summary>
/// <remarks>
/// Nothing here runs on a thread that can afford it. <see cref="Accept"/> is the
/// render thread and does one copy; <see cref="WriteAudio"/> is the sound callback
/// and does one copy into a ring. Everything expensive is on this class's own
/// thread, which may fall behind because <see cref="CapturePacer"/> makes that
/// mean a repeated frame rather than a broken file.
/// <para>
/// The sound is the clock whenever there is any, being the one stream that cannot
/// be dropped or repeated; only a silent video falls back to a stopwatch. Nothing
/// is written until the first frame is in hand, since a file that opens with the
/// sound already running is out of step for its whole length.
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

    /// <summary>
    /// How long the worker is given to drain and close the file. Minutes rather
    /// than seconds because an ffmpeg format ends by muxing the sound into the
    /// picture, which reads and rewrites everything recorded.
    /// </summary>
    private static readonly TimeSpan Finishing = TimeSpan.FromMinutes(10);

    private readonly RecordingSettings settings;
    private readonly IClipWriter clip;

    private readonly AudioRing? ring;
    private readonly FrameMailbox? mailbox;
    private readonly CapturePacer? pacer;

    private readonly Stopwatch clock = new();
    private readonly Thread worker;

    /// <summary>The encoder thread's own scratch — never touched from anywhere else.</summary>
    private readonly float[] drained;
    private readonly byte[] bgra;

    /// <summary>Whether <see cref="bgra"/> holds a frame, which is what a moment with none to spare repeats.</summary>
    private bool held;

    private long samplesWritten;
    private long frames;
    private long duplicated;
    private volatile string? stopped;
    private volatile bool stopping;
    private int closing;
    private bool disposed;

    public LiveRecorder(RecordingSettings settings)
    {
        if (!settings.HasPicture && !settings.HasSound)
            throw new ArgumentException("A recording has to be of something.", nameof(settings));

        this.settings = settings;

        // Opened before the thread starts, so a format that cannot be written at
        // all — no ffmpeg, a folder nobody may write in — is a constructor that
        // throws rather than a take that stops a moment after it began.
        clip = ClipWriter.Open(settings.Target);

        // From here on the file is open and nobody else holds it, so anything
        // that goes wrong has to close it on the way out — an ffmpeg left with
        // its input never closed would sit waiting for frames for ever.
        try
        {
            if (settings.HasPicture)
            {
                mailbox = new FrameMailbox(settings.Size.Width * settings.Size.Height * 4);
                pacer = new CapturePacer(settings.FramesPerSecond);

                bgra = new byte[settings.Size.Width * settings.Size.Height * 4];
            }
            else
            {
                bgra = [];
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

            worker = new Thread(Run) { IsBackground = true, Name = $"{GlobalConstants.ApplicationName} capture" };
            worker.Start();
        }
        catch
        {
            Close();
            throw;
        }
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
    /// Finishes the file. Blocks until the encoder has drained and closed it,
    /// which is what patches a header, writes an index or muxes the sound in —
    /// a take abandoned without this is not a video.
    /// </summary>
    /// <remarks>
    /// The closing is the worker's own last act rather than something done after
    /// it, because an ffmpeg format finishes by re-muxing the whole file and
    /// that is not work for whichever thread happened to press Stop. What it
    /// cost, or what went wrong doing it, is <see cref="RecordingStatus.Stopped"/>
    /// by the time this returns.
    /// </remarks>
    public void Stop()
    {
        if (stopping) return;

        stopping = true;

        // Long enough for a mux of a long take, which is the one thing here that
        // scales with how much was recorded rather than with a frame.
        worker.Join(Finishing);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        Stop();

        // Only where the worker never got to it — a thread that would not join,
        // or one that died before the loop began.
        Close();
    }

    /// <summary>
    /// Closes the file once. Called by the worker as it leaves, and by
    /// <see cref="Dispose"/> for the case where the worker did not.
    /// </summary>
    private void Close()
    {
        if (Interlocked.Exchange(ref closing, 1) != 0) return;

        try
        {
            clip.Dispose();
        }
        catch (Exception ex)
        {
            // What a format says on the way out — ffmpeg refusing the
            // arguments, an AVI at its 4 GB ceiling — is the only account of
            // why the file is not what was asked for.
            Interlocked.CompareExchange(ref stopped, ex.Message, null);
        }
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
                break;
            }

            if (last) break;

            Thread.Sleep(Idle);
        }

        // Here rather than in Stop, so the whole cost of finishing a file is
        // this thread's and not the caller's.
        Close();
    }

    /// <summary>
    /// Waits for a first frame, throwing away the sound that arrived before it.
    /// Those samples belong to a moment the picture cannot show.
    /// </summary>
    private bool TryStart()
    {
        if (mailbox!.TakeLatest() is { IsEmpty: false } first)
        {
            Flip(first);
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
        if (!fresh.IsEmpty) Flip(fresh);

        // Cannot happen once started — the first frame is flipped before the
        // take begins — but nothing is committed until it is written, so if it
        // ever did the file would simply wait rather than skip a moment for good.
        if (!held) return;

        // The count rather than a loop here, so a format that pays to compress a
        // frame pays once for one repeated.
        clip.WriteFrame(bgra, settings.Size.Width * 4, due);

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

            clip.WriteAudio(drained.AsSpan(0, taken));

            Volatile.Write(ref samplesWritten, samplesWritten + taken);
        }
    }

    /// <summary>
    /// OpenGL hands back RGBA with the first row at the bottom;
    /// <see cref="IClipWriter"/> wants BGRA with the first row at the top. Both
    /// halves of that are one pass, on this thread, where it costs nothing that
    /// anybody is waiting for.
    /// </summary>
    private void Flip(ReadOnlySpan<byte> rgba)
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

        held = true;
    }
}
