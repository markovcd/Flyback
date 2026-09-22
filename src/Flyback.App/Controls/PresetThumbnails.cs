using System.Diagnostics.CodeAnalysis;
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
/// <param name="Description">What the patch says it is for, and null where it says nothing or would not open.</param>
/// <param name="Author">Who the patch says made it, and null where it says nobody or would not open.</param>
internal sealed record Thumbnail(byte[]? Pixels, string Words, string? Description = null, string? Author = null)
{
    /// <summary>
    /// A patch that is heard and never seen, which has no frame to take. Shown as a
    /// speaker, the words being what it says when pointed at.
    /// </summary>
    public static Thumbnail SoundOnly { get; } = new(null, "Sound only");

    /// <summary>
    /// A patch with nothing wired to either half of the Output yet. Left bare, its
    /// name and description being what says so.
    /// </summary>
    public static Thumbnail Nothing { get; } = new(null, "");

    /// <summary>A patch that would not build or compile, so there is no frame to show.</summary>
    public static Thumbnail Unavailable { get; } = new(null, "No preview");
}

/// <summary>
/// The still each preset's tile is drawn with, taken from the preset's own patch
/// rather than from a file that would have to be re-shot every time a patch changed.
/// </summary>
/// <remarks>
/// Compiled to IL before its frames are drawn, one preset at a time and only when
/// the gallery is first opened: the window has a live preview and an audio
/// callback to leave cores for, and a gallery nobody opens costs nothing. The frame is taken after a second and a half of frames rather than
/// the first, because a patch that reads the frame before is legitimately black
/// on its first.
/// <para>
/// Each preset gets a sample folder and a picture folder of its own for the run, not
/// the window's: those keep a dictionary that the UI thread is also reading.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "A SemaphoreSlim that never hands out its wait handle holds nothing to free.")]
internal sealed class PresetThumbnails(ModuleCatalog modules, IlCompiler? compiler = null)
{
    public const int Width = 320;
    public const int Height = 180;

    /// <summary>Where saved presets are kept, so one is drawn with the files in its bundle. Null where none are.</summary>
    public PresetLibrary? Saved { get; set; }

    /// <summary>What a patch that reads its previous frame is given to settle in.</summary>
    private const double Settle = 1.5d;

    private const double Step = 1d / 20d;

    /// <summary>
    /// Kept by the preset itself rather than its name: a preset somebody saved can
    /// be saved again under the same name as a different patch, and is then a
    /// different preset to draw.
    /// </summary>
    private readonly Dictionary<PatchPreset, Task<Thumbnail>> drawn = new(ReferenceEqualityComparer.Instance);

    private readonly SemaphoreSlim oneAtATime = new(1);

    /// <summary>
    /// The thumbnail of <paramref name="preset"/>, drawn the first time it is asked
    /// for and remembered after. Called from the UI thread only, which is what lets
    /// the cache be a plain dictionary.
    /// </summary>
    public Task<Thumbnail> Of(PatchPreset preset)
    {
        if (drawn.TryGetValue(preset, out var known)) return known;

        return drawn[preset] = Task.Run(async () =>
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
            var (patch, samples, pictures) = PresetLibrary.Open(preset, Saved, modules);
            var (picture, sound) = patch.Reaches();
            var described = patch.Description;
            var author = patch.Author;

            if (!picture)
                return (sound ? Thumbnail.SoundOnly : Thumbnail.Nothing) with { Description = described, Author = author };

            var video = patch.CompileForVideo(samples: samples, pictures: pictures);

            if (video.HasErrors) return Thumbnail.Unavailable with { Description = described, Author = author };

            compiler?.Compile(video.Program, IlLane.AuditionPicture);

            var stride = Width * 4;
            var pixels = new byte[stride * Height];
            var renderer = new SynthRenderer();

            for (var step = 0; step * Step <= Settle; step++)
                renderer.Render(video.Program, step * Step, Width, Height, pixels, stride);

            return new Thumbnail(pixels, "", described, author);
        }
        catch (Exception)
        {
            // One preset that will not draw is a tile that says so, and not a
            // gallery that never opens.
            return Thumbnail.Unavailable;
        }
    }
}
