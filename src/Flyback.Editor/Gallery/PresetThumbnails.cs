using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Flyback.Plugins.Hosting;
using Flyback.Ui;

namespace Flyback.Editor.Gallery;

/// <summary>
/// The still each preset's tile is drawn with: the one this build's pipeline drew
/// (ADR-0163) where there is one, and otherwise drawn here from the preset's own patch.
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
    public const int Width = PresetStill.Width;
    public const int Height = PresetStill.Height;

    private readonly ModuleCatalog modules;
    private readonly IlCompiler? compiler;
    private readonly PresetLibrary? saved;
    private readonly Lazy<ThumbnailStore>? store;
    private readonly IStillShelf? shelf;

    /// <summary>A page never draws a still live: no preview beats one thread split with the patch.</summary>
    private readonly bool inPage;

    /// <summary>The build's index, read the first time a thumbnail is asked for; null where there is none or it is another build's.</summary>
    private Task<StillIndex?>? index;

    /// <param name="folders">Its <see cref="EditorFolders.ThumbnailFolder"/> keeps thumbnails between runs. Null keeps them for this run only.</param>
    /// <param name="host">Whether the gallery is in a page.</param>
    /// <param name="saved">Where saved presets are kept, so one is drawn with the files in its bundle. Null where none are.</param>
    /// <param name="shelf">Where the build's stills are. Null draws every preset here.</param>
    public PresetThumbnails(
        PluginCatalog plugins,
        IlCompiler? compiler = null,
        EditorFolders? folders = null,
        EditorHost? host = null,
        PresetLibrary? saved = null,
        IStillShelf? shelf = null)
    {
        modules = plugins.Modules;
        this.compiler = compiler;
        this.saved = saved;
        this.shelf = shelf;
        inPage = host?.InPage ?? false;

        if (folders?.ThumbnailFolder is not { } folder) return;

        // Opened, and pruned in the background, by the first thumbnail asked for.
        store = new Lazy<ThumbnailStore>(() =>
        {
            var opened = new ThumbnailStore(folder);
            _ = Task.Run(opened.Prune);

            return opened;
        });
    }

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
            if (path is null)
            {
                if (await Still(preset) is { } still) return still;

                // A page fetches a build's still over the network; it never renders one itself.
                if (inPage) return Thumbnail.Unavailable;
            }

            var key = Key(preset, path);

            if (key is not null && store?.Value.Find(key) is { } kept) return kept;

            await oneAtATime.WaitAsync(cancel);

            try
            {
                var thumbnail = Draw(preset);

                // One that would not draw may next time, with the plugin back or the file readable.
                if (key is not null && thumbnail.Words != Thumbnail.Unavailable.Words) store?.Value.Keep(key, thumbnail);

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
            if (path is null && await Entry(preset) is { } entry) return Tile(entry);

            if (Key(preset, path) is { } key && store?.Value.Find(key, pixels: false) is { } kept) return kept;

            await oneReadAtATime.WaitAsync();

            try
            {
                var (patch, _, _) = PresetLibrary.Open(preset, saved, modules);

                return new Thumbnail(null, "", patch.Description, patch.Author, patch.Tags) { Reaches = patch.Reaches() };
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

    /// <summary>The build's still of <paramref name="preset"/>, or null where it has none to offer.</summary>
    private async Task<Thumbnail?> Still(PatchPreset preset)
    {
        if (await Entry(preset) is not { } entry) return null;

        if (entry.File is null) return Tile(entry);

        return shelf is not null && await shelf.Read(entry.File) is { } file ? Tile(entry) with { Still = file } : null;
    }

    private async Task<StillEntry?> Entry(PatchPreset preset) =>
        shelf is null ? null : (await (index ??= Index(shelf)))?.Of(preset.Name, preset.Kind);

    private static async Task<StillIndex?> Index(IStillShelf shelf) =>
        await shelf.Read(StillIndex.FileName) is { } bytes && StillIndex.Read(Encoding.UTF8.GetString(bytes)) is { Current: true } read
            ? read
            : null;

    /// <summary>The tile an entry makes, but for its picture.</summary>
    private static Thumbnail Tile(StillEntry entry)
    {
        var (words, reaches) = entry.Still switch
        {
            StillKind.SoundOnly => (Thumbnail.SoundOnly, (false, true)),
            StillKind.Unavailable => (Thumbnail.Unavailable, ((bool, bool)?)null),
            StillKind.Picture => (Thumbnail.Nothing, (true, entry.Heard)),
            _ => (Thumbnail.Nothing, (false, false)),
        };

        return words with { Description = entry.Description, Author = entry.Author, Tags = entry.Tags, Reaches = reaches };
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
            var (kind, pixels) = PresetStill.Draw(patch, samples, pictures, program => compiler?.Compile(program, IlLane.AuditionPicture));

            var said = Tile(new StillEntry(preset.Name, preset.Kind, kind, null, patch.Description, patch.Author, patch.Tags, patch.Reaches().Sound));

            return said with { Pixels = pixels };
        }
        catch (Exception)
        {
            // One preset that will not draw is a tile that says so, and not a
            // gallery that never opens.
            return Thumbnail.Unavailable;
        }
    }
}
