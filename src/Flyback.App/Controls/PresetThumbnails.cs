using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.App.Controls;

/// <summary>
/// What a preset's tile shows in the place of a picture, or the picture itself.
/// </summary>
/// <param name="Pixels">
/// A BGRA frame <see cref="PresetThumbnails.Width"/> by <see cref="PresetThumbnails.Height"/>,
/// or null for a preset that has none to show.
/// </param>
/// <param name="Words">What the tile says instead. Empty when there are pixels, or nothing to say.</param>
internal sealed record Thumbnail(byte[]? Pixels, string Words)
{
    /// <summary>
    /// A patch with no picture in it — one that is only heard, or one with nothing
    /// wired yet. The tile is left bare, its name and description saying which.
    /// </summary>
    public static Thumbnail Blank { get; } = new(null, "");

    /// <summary>A patch that would not build or compile, so there is no frame to show.</summary>
    public static Thumbnail Unavailable { get; } = new(null, "No preview");
}

/// <summary>
/// The still each preset's tile is drawn with, taken from the preset's own patch
/// rather than from a file that would have to be re-shot every time a patch changed.
/// </summary>
/// <remarks>
/// Drawn by the interpreter, the way <c>flyback render</c> draws a still, one preset
/// at a time and only when the gallery is first opened: the window has a live
/// preview and an audio callback to leave cores for, and a gallery nobody opens
/// costs nothing. The frame is taken after a second and a half of frames rather than
/// the first, because a patch that reads the frame before is legitimately black
/// on its first.
/// <para>
/// Each preset gets a sample folder and a picture folder of its own for the run, not
/// the window's: those keep a dictionary that the UI thread is also reading.
/// </para>
/// </remarks>
internal sealed class PresetThumbnails(ModuleCatalog modules)
{
    public const int Width = 320;
    public const int Height = 180;

    /// <summary>What a patch that reads its previous frame is given to settle in.</summary>
    private const double Settle = 1.5d;

    private const double Step = 1d / 20d;

    /// <summary>Kept by name, because that is what a preset is known by everywhere else.</summary>
    private readonly Dictionary<string, Task<Thumbnail>> drawn = [];

    private readonly SemaphoreSlim oneAtATime = new(1);

    /// <summary>
    /// The thumbnail of <paramref name="preset"/>, drawn the first time it is asked
    /// for and remembered after. Called from the UI thread only, which is what lets
    /// the cache be a plain dictionary.
    /// </summary>
    public Task<Thumbnail> Of(PatchPreset preset)
    {
        if (drawn.TryGetValue(preset.Name, out var known)) return known;

        return drawn[preset.Name] = Task.Run(async () =>
        {
            await oneAtATime.WaitAsync();

            try
            {
                return Draw(preset);
            }
            finally
            {
                oneAtATime.Release();
            }
        });
    }

    private Thumbnail Draw(PatchPreset preset)
    {
        try
        {
            // A preset from a plugin is built here for the same reason the toolbar
            // builds it when it is picked: it needs the modules that plugin added.
            var patch = preset.Build(modules);
            if (!patch.Reaches().Picture) return Thumbnail.Blank;

            var video = patch.CompileForVideo(
                samples: new SampleLibrary(),
                pictures: new ImageLibrary());

            if (video.HasErrors) return Thumbnail.Unavailable;

            var stride = Width * 4;
            var pixels = new byte[stride * Height];
            var renderer = new SynthRenderer();

            for (var step = 0; step * Step <= Settle; step++)
                renderer.Render(video.Program, step * Step, Width, Height, pixels, stride);

            return new Thumbnail(pixels, "");
        }
        catch (Exception)
        {
            // One preset that will not draw is a tile that says so, and not a
            // gallery that never opens.
            return Thumbnail.Unavailable;
        }
    }
}
