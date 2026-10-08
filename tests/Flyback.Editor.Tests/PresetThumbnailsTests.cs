using System.Text;
using Flyback.Editor.Gallery;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests;

/// <summary>The stills the gallery's tiles are drawn with.</summary>
public sealed class PresetThumbnailsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-thumbnails-" + Guid.NewGuid().ToString("N"));

    private int builds;

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private static PatchPreset Pictured() =>
        Presets.All.First(preset => preset.Build(NodeCatalog.BuiltIn).Reaches().Picture);

    /// <summary>A pictured preset that counts how often its patch is built, which is once per drawing.</summary>
    private PatchPreset Counted()
    {
        var pictured = Pictured();

        return new PatchPreset("Counted " + Guid.NewGuid().ToString("N"), catalog =>
        {
            Interlocked.Increment(ref builds);
            return pictured.Build(catalog);
        }, pictured.Description, pictured.Kind);
    }

    /// <summary>A shelf holding what it is handed, as a build's stills folder would.</summary>
    private sealed class Shelf(Dictionary<string, byte[]> files) : IStillShelf
    {
        public Task<byte[]?> Read(string name) => Task.FromResult(files.GetValueOrDefault(name));
    }

    private static Shelf Stilled(PatchPreset preset, string version, byte[] still) => new(new()
    {
        [StillIndex.FileName] = Encoding.UTF8.GetBytes(new StillIndex(version,
            [new StillEntry(preset.Name, preset.Kind, StillKind.Picture, "still.jpg", "Drawn by the build.")]).Write()),
        ["still.jpg"] = still,
    });

    [Fact]
    public async Task A_preset_the_build_drew_is_shown_by_its_still_without_being_built()
    {
        var preset = Counted();
        byte[] still = [1, 2, 3];

        var tile = await new PresetThumbnails(PluginCatalog.Empty, shelf: Stilled(preset, StillIndex.ThisBuild, still)).Of(preset, TestContext.Current.CancellationToken);

        builds.ShouldBe(0);
        tile.Still.ShouldBe(still);
        tile.Description.ShouldBe("Drawn by the build.");
    }

    [Fact]
    public async Task Stills_another_build_drew_are_passed_over_for_a_drawing()
    {
        var preset = Counted();

        var tile = await new PresetThumbnails(PluginCatalog.Empty, shelf: Stilled(preset, "another build", [1, 2, 3])).Of(preset, TestContext.Current.CancellationToken);

        builds.ShouldBe(1);
        tile.Still.ShouldBeNull();
        tile.Pixels.ShouldNotBeNull();
    }

    /// <summary>A tile drawn from IL is the tile the interpreter draws, to the byte.</summary>
    [Fact]
    public async Task A_compiled_still_is_the_interpreted_one()
    {
        var preset = Pictured();

        using var compiler = new IlCompiler();
        var compiled = await new PresetThumbnails(PluginCatalog.Empty, compiler).Of(preset, TestContext.Current.CancellationToken);
        var interpreted = await new PresetThumbnails(PluginCatalog.Empty).Of(preset, TestContext.Current.CancellationToken);

        compiled.Pixels.ShouldNotBeNull().ShouldBe(interpreted.Pixels);
    }

    [Fact]
    public async Task A_thumbnail_drawn_once_is_found_on_disk_by_the_next_run()
    {
        var preset = Counted();

        var drawn = await new PresetThumbnails(PluginCatalog.Empty, folders: new() { ThumbnailFolder = folder }).Of(preset, TestContext.Current.CancellationToken);
        var found = await new PresetThumbnails(PluginCatalog.Empty, folders: new() { ThumbnailFolder = folder }).Of(preset, TestContext.Current.CancellationToken);

        builds.ShouldBe(1);
        found.Pixels.ShouldNotBeNull().ShouldBe(drawn.Pixels);
        found.Description.ShouldBe(drawn.Description);
        found.Tags.ShouldBe(drawn.Tags);
    }

    [Fact]
    public async Task Which_halves_of_the_Output_a_patch_wires_is_found_on_disk_too()
    {
        var preset = Counted();

        var drawn = await new PresetThumbnails(PluginCatalog.Empty, folders: new() { ThumbnailFolder = folder }).Of(preset, TestContext.Current.CancellationToken);
        var said = await new PresetThumbnails(PluginCatalog.Empty, folders: new() { ThumbnailFolder = folder }).Said(preset);

        builds.ShouldBe(1);
        drawn.Reaches.ShouldBe(preset.Build(NodeCatalog.BuiltIn).Reaches());
        said.Reaches.ShouldBe(drawn.Reaches);
    }

    [Fact]
    public async Task A_still_the_build_drew_says_whether_its_preset_is_heard()
    {
        var preset = Counted();
        var shelf = new Shelf(new()
        {
            [StillIndex.FileName] = Encoding.UTF8.GetBytes(new StillIndex(StillIndex.ThisBuild,
                [new StillEntry(preset.Name, preset.Kind, StillKind.Picture, "still.jpg", Heard: true)]).Write()),
            ["still.jpg"] = [1, 2, 3],
        });

        var said = await new PresetThumbnails(PluginCatalog.Empty, shelf: shelf).Said(preset);

        builds.ShouldBe(0);
        said.Reaches.ShouldBe((true, true));
    }

    [Fact]
    public async Task What_a_patch_says_is_found_on_disk_without_opening_it()
    {
        var preset = Counted();

        var drawn = await new PresetThumbnails(PluginCatalog.Empty, folders: new() { ThumbnailFolder = folder }).Of(preset, TestContext.Current.CancellationToken);
        var said = await new PresetThumbnails(PluginCatalog.Empty, folders: new() { ThumbnailFolder = folder }).Said(preset);

        builds.ShouldBe(1);
        said.Description.ShouldBe(drawn.Description);
    }

    [Fact]
    public async Task What_a_patch_says_is_read_without_drawing_it()
    {
        var preset = Pictured();

        var said = await new PresetThumbnails(PluginCatalog.Empty).Said(preset);

        said.Description.ShouldBe(preset.Build(NodeCatalog.BuiltIn).Description);
        said.Pixels.ShouldBeNull();
    }

    /// <summary>A gallery closed before a tile was reached gives it up, and the next one to ask has it drawn.</summary>
    [Fact]
    public async Task A_thumbnail_given_up_is_drawn_when_asked_for_again()
    {
        var thumbnails = new PresetThumbnails(PluginCatalog.Empty);
        var preset = Counted();

        await Should.ThrowAsync<OperationCanceledException>(() => thumbnails.Of(preset, new CancellationToken(canceled: true)));

        var drawn = await thumbnails.Of(preset, TestContext.Current.CancellationToken);

        drawn.Pixels.ShouldNotBeNull();
        builds.ShouldBe(1);
    }

    /// <summary>The picture it needs may be there next time, so the tile is drawn again rather than kept as it is.</summary>
    [Fact]
    public async Task A_patch_that_does_not_compile_is_not_kept_on_disk()
    {
        var preset = new PatchPreset("Missing " + Guid.NewGuid().ToString("N"), catalog =>
        {
            var patch = new Patch();
            var image = NodeInstance.Create(catalog.Require(NodeCatalog.PictureTypeId), 0, 0);
            var output = NodeInstance.Create(catalog.Require(NodeCatalog.OutputTypeId), 0, 0);

            PictureExtra.Set(image, "gone.png");
            patch.Nodes.Add(image);
            patch.Nodes.Add(output);
            patch.Connect(image.Id, 0, output.Id, NodeCatalog.OutputColorPort);

            return patch;
        }, "");

        var drawn = await new PresetThumbnails(PluginCatalog.Empty, folders: new() { ThumbnailFolder = folder }).Of(preset, TestContext.Current.CancellationToken);

        drawn.Words.ShouldBe(Thumbnail.Unavailable.Words);
        (Directory.Exists(folder) ? Directory.GetFiles(folder, "*.thumb") : []).ShouldBeEmpty();
    }

    /// <summary>A page never draws a still live: a preset with nothing fetched says so instead.</summary>
    [Fact]
    public async Task A_page_with_no_still_to_fetch_says_no_preview_rather_than_drawing_one()
    {
        var preset = Counted();

        var tile = await new PresetThumbnails(PluginCatalog.Empty, host: new() { InPage = true }).Of(preset, TestContext.Current.CancellationToken);

        builds.ShouldBe(0);
        tile.Words.ShouldBe(Thumbnail.Unavailable.Words);
    }

    /// <summary>A page shows a still it fetches, same as any other build.</summary>
    [Fact]
    public async Task A_page_shows_a_fetched_still()
    {
        var preset = Counted();
        byte[] still = [1, 2, 3];

        var tile = await new PresetThumbnails(
            PluginCatalog.Empty,
            host: new() { InPage = true },
            shelf: Stilled(preset, StillIndex.ThisBuild, still)).Of(preset, TestContext.Current.CancellationToken);

        builds.ShouldBe(0);
        tile.Still.ShouldBe(still);
    }

    /// <summary>
    /// A tile reads a thumbnail while another Flyback keeps a fresh one under the
    /// same name, and the fresh one goes in rather than being dropped.
    /// </summary>
    [Fact]
    public void A_thumbnail_being_read_is_still_kept_over()
    {
        var store = new ThumbnailStore(folder);

        store.Keep("abc", new Thumbnail(null, "first"));

        using (new FileStream(Path.Combine(folder, "abc.thumb"), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            store.Keep("abc", new Thumbnail(null, "second"));

        new ThumbnailStore(folder).Find("abc").ShouldNotBeNull().Words.ShouldBe("second");
        Directory.GetFiles(folder).ShouldHaveSingleItem("nothing is left half written");
    }

    [Fact]
    public void A_kept_thumbnail_that_cannot_be_read_is_none()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "abc.thumb"), [1, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);

        new ThumbnailStore(folder).Find("abc").ShouldBeNull();
    }

    [Fact]
    public void A_kept_thumbnail_that_lies_about_its_tags_costs_nothing()
    {
        var store = new ThumbnailStore(folder);

        store.Keep("abc", new Thumbnail(null, "words", Tags: []));

        // The tag count and the byte after it end a thumbnail with no tags and no pixels.
        using (var file = File.OpenWrite(Path.Combine(folder, "abc.thumb")))
        {
            file.Seek(-5, SeekOrigin.End);
            file.Write(BitConverter.GetBytes(int.MaxValue));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        store.Find("abc").ShouldBeNull();
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeLessThan(1 << 20);
    }
}
