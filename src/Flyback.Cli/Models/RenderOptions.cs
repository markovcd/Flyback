using Flyback.Engine.Render;

namespace Flyback.Cli.Models;

/// <summary>Everything about a render that is not the patch.</summary>
/// <param name="At">Which moment a still is of. Ignored by the two that have a length instead.</param>
/// <param name="Format">
/// Which of <see cref="ClipFormats"/> to write, by id, or null to take it from the
/// extension of <paramref name="Out"/>.
/// </param>
/// <param name="Ffmpeg">
/// Where ffmpeg is, for a format that needs it. Null looks on <c>PATH</c>.
/// </param>
/// <param name="Loudness">
/// Whether to measure the sound as it is written and say how loud it came out.
/// Ignored for a still, which has none.
/// </param>
/// <param name="Interpreted">Keep the programs on the interpreter rather than compiling them.</param>
/// <param name="Backend">What draws the picture.</param>
/// <param name="Oversample">How many times the output rate the sound is evaluated at: one of <see cref="AudioRenderer.Oversamples"/>.</param>
/// <param name="Input">A sound file a Line In hears in place of a microphone, from its start; null for silence.</param>
/// <param name="From">The second a clip or a sound starts at; the patch is played up to it unrecorded.</param>
internal sealed record RenderOptions(
    FileInfo Out,
    int Width = 1920,
    int Height = 1080,
    double At = 0d,
    double Seconds = 10d,
    double Fps = MovieRenderer.DefaultFrameRate,
    int Quality = JpegWriter.DefaultQuality,
    string? Format = null,
    string? Ffmpeg = null,
    bool Loudness = false,
    bool Interpreted = false,
    PictureBackend Backend = PictureBackend.Any,
    int Oversample = AudioRenderer.DefaultOversample,
    FileInfo? Input = null,
    double From = 0d);