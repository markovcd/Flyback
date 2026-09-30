using System.Runtime.Versioning;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Gpu;

namespace Flyback.Web;

/// <summary>
/// One patch's picture, drawn on the page by the desktop's GPU renderer, from what the
/// worker playing the sound last said of it.
/// </summary>
/// <remarks>
/// Compiled and drawn only once a context asks for it. Meters hold what
/// <see cref="Apply"/> last handed them, which is silence until the sound plays.
/// </remarks>
[SupportedOSPlatform("browser")]
internal sealed class WebPicture
{
    private readonly CompiledPatch picture;
    private readonly LiveValues watching;

    private GpuFrameRenderer? screen;
    private bool settled;

    public WebPicture(Opened opened, int width, int height)
    {
        var (patch, samples, pictures) = opened;

        Resolution = new SurfaceSize(width, height);
        Length = patch.Length;

        picture = patch.CompileForVideo(samples: samples, pictures: pictures, played: true).Program;

        watching = new LiveValues(picture.LiveInputs);
        patch.Seed(watching);

        Undrawn = UndrawnPicture.Why(picture);
    }

    /// <summary>Why the picture is left out, or null where it is drawn.</summary>
    public string? Undrawn { get; }

    public SurfaceSize Resolution { get; }

    /// <summary>How long the patch plays for, in seconds, or null for one that has not said and plays on.</summary>
    public double? Length { get; }

    public int PictureOps => picture.Ops.Length;

    /// <summary>How many floats <see cref="Apply"/> takes.</summary>
    public int StateLength => SoundState.Length(picture);

    /// <summary>True when feedback fell back to eight bits a channel on this GPU.</summary>
    public bool EightBitFeedback => screen?.EightBitFeedback ?? false;

    /// <summary>Whether the picture's shader is still being built.</summary>
    public bool Linking => screen is not null && !settled;

    /// <summary>Takes the Meters' readings the sound's worker packed.</summary>
    public void Apply(ReadOnlySpan<float> state) => SoundState.Read(state, picture, watching);

    /// <summary>Turns a panel knob, 0 to 1.</summary>
    public void Turn(string key, float value) => watching.Set(key, value);

    /// <summary>Where the knob called <paramref name="key"/> is turned to, or null where the picture does not read it.</summary>
    public float? Reading(string key) => watching.Find(key);

    /// <summary>Back to the start of whatever the picture remembers, as a seek leaves it.</summary>
    public void Rewind() => screen?.Rewind();

    /// <summary>
    /// Draws the frame at <paramref name="time"/> into the canvas, letterboxed into
    /// <paramref name="canvas"/>. Null on success.
    /// </summary>
    public string? Draw(IGl gl, double time, SurfaceSize canvas)
    {
        if (Undrawn is not null) return null;

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

        return screen.Render(gl, 0, canvas, Resolution, time, watching);
    }

    /// <summary>
    /// Draws the frame at <paramref name="time"/> at the patch's own resolution and reads
    /// it into <paramref name="rgba"/>, bottom row first. Null on success.
    /// </summary>
    public string? Still(IGl gl, double time, Span<byte> rgba)
    {
        if (Undrawn is not null) return Undrawn;

        if (screen is null || !settled) return "The picture is not ready yet.";

        return screen.Frame(gl, Resolution, time, watching, rgba);
    }
}
