using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// The still a preset is shown by in a gallery: one frame of its picture, drawn by
/// the processor a second and a half in, since a patch that reads its previous frame
/// is black on its first.
/// </summary>
public static class PresetStill
{
    public const int Width = 320;
    public const int Height = 180;

    private const double Settle = 1.5d;

    private const double Step = 1d / 20d;

    /// <summary>
    /// Draws <paramref name="patch"/>'s still as BGRA, or says why there is none.
    /// </summary>
    /// <param name="compile">Hands the program to a compiler before its frames are drawn, where there is one.</param>
    public static (StillKind Kind, byte[]? Pixels) Draw(
        Patch patch,
        ISampleLibrary? samples = null,
        IImageLibrary? pictures = null,
        Action<CompiledPatch>? compile = null)
    {
        var (picture, sound) = patch.Reaches();

        if (!picture) return (sound ? StillKind.SoundOnly : StillKind.Nothing, null);

        var video = patch.CompileForVideo(samples: samples, pictures: pictures);

        if (video.HasErrors) return (StillKind.Unavailable, null);

        compile?.Invoke(video.Program);

        const int stride = Width * 4;
        var pixels = new byte[stride * Height];
        var renderer = new SynthRenderer();

        for (var step = 0; step * Step <= Settle; step++)
            renderer.Render(video.Program, step * Step, Width, Height, pixels, stride);

        return (StillKind.Picture, pixels);
    }
}
