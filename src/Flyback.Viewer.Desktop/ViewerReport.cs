using System.Globalization;
using Flyback.Engine.Render;

namespace Flyback.Viewer.Desktop;

/// <summary>What one run measured, said as <c>name: value</c> lines a script can read.</summary>
/// <param name="Seconds">How long the patch played, not counting time paused.</param>
/// <param name="Renderer">What drew the picture, or null for a run with none.</param>
/// <param name="GpuRefusal">Why the graphics card was given up, or null where it was not.</param>
/// <param name="Frames">Frames put on screen.</param>
/// <param name="SlowestFrameMilliseconds">The longest a frame took to draw.</param>
/// <param name="Sound">Whether a sound device ran.</param>
/// <param name="Oversample">How many times the output rate the sound was evaluated at.</param>
/// <param name="Speed">How many times real time the sound rendered at lately, or 0 where nothing was measured.</param>
/// <param name="Timing">Buffers of sound timed, and how many ran late.</param>
internal sealed record ViewerReport(
    double Seconds,
    string? Renderer,
    string? GpuRefusal,
    long Frames,
    double SlowestFrameMilliseconds,
    bool Sound,
    int Oversample,
    double Speed,
    SoundTiming Timing)
{
    /// <summary>Frames a second held over the time played, or 0 before any time has.</summary>
    public double FramesPerSecond => Seconds > 0 ? Frames / Seconds : 0;

    public IEnumerable<string> Lines()
    {
        yield return Line("seconds", Seconds, "0.0");

        if (Renderer is null)
        {
            yield return "picture: none";
        }
        else
        {
            yield return $"picture: {Renderer}";

            if (GpuRefusal is { } refusal) yield return $"gpu-refused: {refusal}";

            yield return Line("fps", FramesPerSecond, "0.0");
            yield return Line("slowest-frame-ms", SlowestFrameMilliseconds, "0.0");
        }

        if (!Sound)
        {
            yield return "sound: none";
            yield break;
        }

        yield return $"sound-oversample: {Oversample}";
        yield return Line("sound-speed", Speed, "0.0");
        yield return string.Create(CultureInfo.InvariantCulture, $"sound-late-buffers: {Timing.Late} of {Timing.Timed}");
    }

    private static string Line(string name, double value, string format) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}: {value.ToString(format, CultureInfo.InvariantCulture)}");
}
