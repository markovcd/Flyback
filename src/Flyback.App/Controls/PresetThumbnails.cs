using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;

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
/// <param name="Tags">What the patch is tagged, and null where it has no tags or would not open.</param>
internal sealed record Thumbnail(
    byte[]? Pixels, string Words, string? Description = null, string? Author = null, IReadOnlyList<string>? Tags = null)
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
/// Compiled to IL before its frames are drawn, one preset at a time and only once
/// its tile comes into sight: the window has a live preview and an audio callback
/// to leave cores for, and a tile nobody scrolls to costs nothing. The frame is
/// taken after a second and a half of frames rather than the first, because a
/// patch that reads the frame before is legitimately black on its first.
/// <para>
/// Each preset gets a sample folder and a picture folder of its own for the run, not
/// the window's: those keep a dictionary that the UI thread is also reading.
/// </para>
/// <para>
/// What is drawn is kept on disk under the build that drew it and, for a saved
/// preset, the file's size and time, so a thumbnail is drawn once per preset per
/// build rather than once per run.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "A SemaphoreSlim that never hands out its wait handle holds nothing to free.")]
internal sealed class PresetThumbnails
{
    public const int Width = 320;
    public const int Height = 180;

    private readonly ModuleCatalog modules;
    private readonly IlCompiler? compiler;
    private readonly PresetLibrary? saved;
    private readonly ThumbnailStore? store;

    /// <param name="setup">Its <see cref="EditorSetup.ThumbnailFolder"/> keeps thumbnails between runs. Null keeps them for this run only.</param>
    /// <param name="saved">Where saved presets are kept, so one is drawn with the files in its bundle. Null where none are.</param>
    public PresetThumbnails(PluginCatalog plugins, IlCompiler? compiler = null, EditorSetup? setup = null, PresetLibrary? saved = null)
    {
        modules = plugins.Modules;
        this.compiler = compiler;
        this.saved = saved;

        if (setup?.ThumbnailFolder is not { } folder) return;

        store = new ThumbnailStore(folder);
        _ = Task.Run(store.Prune);
    }

    /// <summary>What a patch that reads its previous frame is given to settle in.</summary>
    private const double Settle = 1.5d;

    private const double Step = 1d / 20d;

    /// <summary>
    /// Kept by the preset itself rather than its name: a preset somebody saved can
    /// be saved again under the same name as a different patch, and is then a
    /// different preset to draw.
    /// </summary>
    private readonly Dictionary<PatchPreset, Task<Thumbnail>> drawn = new(ReferenceEqualityComparer.Instance);

    /// <summary>What each preset's patch says of itself, kept the same way as <see cref="drawn"/>.</summary>
    private readonly Dictionary<PatchPreset, Task<Thumbnail>> said = new(ReferenceEqualityComparer.Instance);

    private readonly SemaphoreSlim oneAtATime = new(1);

    private readonly SemaphoreSlim oneReadAtATime = new(1);

    /// <summary>
    /// Every Flyback assembly loaded, plugins included, so a thumbnail kept on disk
    /// is one this very build would draw.
    /// </summary>
    private static readonly Lazy<string> Build = new(() => string.Join(',', AppDomain.CurrentDomain.GetAssemblies()
        .Where(assembly => assembly.GetName().Name?.StartsWith(nameof(Flyback), StringComparison.Ordinal) ?? false)
        .Select(assembly => assembly.ManifestModule.ModuleVersionId)
        .Order()));

    /// <summary>
    /// The thumbnail of <paramref name="preset"/>, found on disk or drawn the first
    /// time it is asked for, and remembered after. Called from the UI thread only,
    /// which is what lets the cache be a plain dictionary.
    /// </summary>
    /// <param name="cancel">Gives up waiting behind the others; a drawing already begun finishes.</param>
    public Task<Thumbnail> Of(PatchPreset preset, CancellationToken cancel = default)
    {
        if (drawn.TryGetValue(preset, out var known) && !known.IsCanceled) return known;

        var path = saved?.Holding(preset)?.Path;

        return drawn[preset] = Task.Run(async () =>
        {
            var key = Key(preset, path);

            if (key is not null && store?.Find(key) is { } kept) return kept;

            await oneAtATime.WaitAsync(cancel);

            try
            {
                var thumbnail = Draw(preset);

                // One that would not draw may next time, with the plugin back or the file readable.
                if (key is not null && thumbnail.Words != Thumbnail.Unavailable.Words) store?.Keep(key, thumbnail);

                return thumbnail;
            }
            finally
            {
                oneAtATime.Release();
            }
        }, cancel);
    }

    /// <summary>Whether <paramref name="preset"/>'s thumbnail has been asked for.</summary>
    public bool IsAsked(PatchPreset preset) => drawn.ContainsKey(preset);

    /// <summary>
    /// What <paramref name="preset"/>'s patch says of itself — its description, author
    /// and tags — without drawing it: what a tile shows and is found by before it
    /// has been scrolled to. Its <see cref="Thumbnail.Pixels"/> are not to be relied on.
    /// </summary>
    public Task<Thumbnail> Said(PatchPreset preset)
    {
        if (drawn.TryGetValue(preset, out var known) && known.IsCompletedSuccessfully) return known;

        if (said.TryGetValue(preset, out var heard)) return heard;

        var path = saved?.Holding(preset)?.Path;

        return said[preset] = Task.Run(async () =>
        {
            if (Key(preset, path) is { } key && store?.Find(key, pixels: false) is { } kept) return kept;

            await oneReadAtATime.WaitAsync();

            try
            {
                var (patch, _, _) = PresetLibrary.Open(preset, saved, modules);

                return new Thumbnail(null, "", patch.Description, patch.Author, patch.Tags);
            }
            catch (Exception)
            {
                return Thumbnail.Nothing;
            }
            finally
            {
                oneReadAtATime.Release();
            }
        });
    }

    /// <summary>
    /// What a thumbnail is kept on disk under, or null for a saved preset whose file
    /// has gone.
    /// </summary>
    private string? Key(PatchPreset preset, string? path)
    {
        if (store is null) return null;

        var from = "built";

        if (path is not null)
        {
            var file = new FileInfo(path);

            if (!file.Exists) return null;

            from = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
        }

        var drawnFrom = string.Join('\n', Build.Value, preset.Kind, preset.Name, preset.Description, from);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(drawnFrom)), 0, 16);
    }

    private Thumbnail Draw(PatchPreset preset)
    {
        try
        {
            // A preset from a plugin is built here for the same reason the toolbar
            // builds it when it is picked: it needs the modules that plugin added.
            var (patch, samples, pictures) = PresetLibrary.Open(preset, saved, modules);
            var (picture, sound) = patch.Reaches();
            var described = patch.Description;
            var author = patch.Author;
            var tags = patch.Tags;

            if (!picture)
                return (sound ? Thumbnail.SoundOnly : Thumbnail.Nothing) with { Description = described, Author = author, Tags = tags };

            var video = patch.CompileForVideo(samples: samples, pictures: pictures);

            if (video.HasErrors) return Thumbnail.Unavailable with { Description = described, Author = author, Tags = tags };

            compiler?.Compile(video.Program, IlLane.AuditionPicture);

            var stride = Width * 4;
            var pixels = new byte[stride * Height];
            var renderer = new SynthRenderer();

            for (var step = 0; step * Step <= Settle; step++)
                renderer.Render(video.Program, step * Step, Width, Height, pixels, stride);

            return new Thumbnail(pixels, "", described, author, tags);
        }
        catch (Exception)
        {
            // One preset that will not draw is a tile that says so, and not a
            // gallery that never opens.
            return Thumbnail.Unavailable;
        }
    }
}
