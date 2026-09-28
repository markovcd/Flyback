using System.Diagnostics;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Gpu;

namespace Flyback.Web;

/// <summary>
/// One patch, playing in a page: its sound rendered a buffer at a time for the
/// page to queue, and its picture drawn by the desktop's GPU renderer.
/// </summary>
/// <remarks>
/// The page's one thread does everything, so nothing here is guarded. The picture
/// is compiled and drawn only once a context asks for it, which leaves the sound
/// runnable where there is no canvas at all.
/// </remarks>
internal sealed class WebPlayer
{
    private readonly CompiledPatch sound;
    private readonly CompiledPatch picture;
    private readonly AudioRenderer speakers;
    private readonly DelayState? memory;
    private readonly LiveValues heard;
    private readonly LiveValues watching;
    private readonly Stopwatch rendering = new();

    private GpuFrameRenderer? screen;
    private bool settled;
    private double rendered;

    public WebPlayer(Opened opened, int width, int height)
    {
        var (patch, samples, pictures) = opened;

        Resolution = new SurfaceSize(width, height);
        Length = patch.Lasts;

        sound = patch.CompileForAudio(samples: samples, pictures: pictures, played: true).Program;
        picture = patch.CompileForVideo(samples: samples, pictures: pictures, played: true).Program;

        speakers = new AudioRenderer { Aspect = SynthRenderer.AspectOf(width, height) };
        speakers.Prepare(sound);
        memory = speakers.DelayMemoryFor(sound);

        heard = new LiveValues(sound.LiveInputs);
        watching = new LiveValues(picture.LiveInputs);

        // Where the panel's knobs rest, since nothing here turns them.
        patch.Seed(heard);
        patch.Seed(watching);
    }

    public SurfaceSize Resolution { get; }

    /// <summary>How long the patch plays for, in seconds.</summary>
    public double Length { get; }

    public int SoundOps => sound.Ops.Length;

    public int PictureOps => picture.Ops.Length;

    public int SampleRate => speakers.SampleRate;

    /// <summary>Where the sound has been rendered up to, in seconds.</summary>
    public double Time => speakers.Time;

    /// <summary>Seconds of sound rendered for each second spent rendering it, or zero before any.</summary>
    public double Speed => rendering.Elapsed.TotalSeconds > 0 ? rendered / rendering.Elapsed.TotalSeconds : 0;

    /// <summary>True when feedback fell back to eight bits a channel on this GPU.</summary>
    public bool EightBitFeedback => screen?.EightBitFeedback ?? false;

    /// <summary>Fills <paramref name="interleavedStereo"/> with the next stretch of sound.</summary>
    public void Hear(Span<float> interleavedStereo)
    {
        rendering.Start();
        speakers.Render(sound, interleavedStereo, memory, heard);
        rendering.Stop();

        rendered += interleavedStereo.Length / 2.0 / speakers.SampleRate;
    }

    /// <summary>How fast the sound renders here, measured on a copy so what is playing is untouched.</summary>
    public double Measure(double seconds)
    {
        var copy = new AudioRenderer { Aspect = speakers.Aspect };
        var lines = copy.DelayMemoryFor(sound);
        var live = new LiveValues(sound.LiveInputs);
        var buffer = new float[1024];
        var buffers = Math.Max(1, (int)(seconds * copy.SampleRate / 512));

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < buffers; i++) copy.Render(sound, buffer, lines, live);

        return buffers * 512.0 / copy.SampleRate / clock.Elapsed.TotalSeconds;
    }

    /// <summary>Back to <paramref name="seconds"/>, with everything the patch remembers emptied.</summary>
    public void SeekTo(double seconds)
    {
        speakers.Reset();
        memory?.Clear();
        speakers.SeekTo(Math.Max(0, seconds));
        screen?.Rewind();
    }

    /// <summary>
    /// Draws the frame at <paramref name="time"/> into the canvas, letterboxed into
    /// <paramref name="canvas"/>, after handing it what the speakers played. Null on success.
    /// </summary>
    public string? Draw(IGl gl, double time, SurfaceSize canvas)
    {
        if (screen is null)
        {
            screen = new GpuFrameRenderer(GlslDialect.GlslEs300, backgroundLinks: true);

            if (screen.Initialise(gl) is { } refused) return refused;
        }

        if (!settled)
        {
            if (screen.SetPatch(gl, picture) is { } failure) return failure;

            settled = !screen.Linking;
        }

        Traces.Refresh(picture, sound, memory);
        Meters.Refresh(sound, memory, watching, heard);

        return screen.Render(gl, 0, canvas, Resolution, time, watching);
    }

    /// <summary>
    /// Draws the frame at <paramref name="time"/> at the patch's own resolution and reads
    /// it into <paramref name="rgba"/>, bottom row first. Null on success.
    /// </summary>
    public string? Still(IGl gl, double time, Span<byte> rgba)
    {
        if (screen is null || !settled) return "The picture is not ready yet.";

        return screen.Frame(gl, Resolution, time, watching, rgba);
    }

    /// <summary>Whether the picture's shader is still being built.</summary>
    public bool Linking => screen is not null && !settled;
}
