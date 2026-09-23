using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

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

    /// <summary>A tile drawn from IL is the tile the interpreter draws, to the byte.</summary>
    [Fact]
    public async Task A_compiled_still_is_the_interpreted_one()
    {
        var preset = Pictured();

        using var compiler = new IlCompiler();
        var compiled = await new PresetThumbnails(NodeCatalog.BuiltIn, compiler).Of(preset, TestContext.Current.CancellationToken);
        var interpreted = await new PresetThumbnails(NodeCatalog.BuiltIn).Of(preset, TestContext.Current.CancellationToken);

        compiled.Pixels.ShouldNotBeNull().ShouldBe(interpreted.Pixels);
    }

    [Fact]
    public async Task A_thumbnail_drawn_once_is_found_on_disk_by_the_next_run()
    {
        var preset = Counted();

        var drawn = await new PresetThumbnails(NodeCatalog.BuiltIn, folder: folder).Of(preset, TestContext.Current.CancellationToken);
        var found = await new PresetThumbnails(NodeCatalog.BuiltIn, folder: folder).Of(preset, TestContext.Current.CancellationToken);

        builds.ShouldBe(1);
        found.Pixels.ShouldNotBeNull().ShouldBe(drawn.Pixels);
        found.Description.ShouldBe(drawn.Description);
        found.Tags.ShouldBe(drawn.Tags);
    }

    [Fact]
    public async Task What_a_patch_says_is_found_on_disk_without_opening_it()
    {
        var preset = Counted();

        var drawn = await new PresetThumbnails(NodeCatalog.BuiltIn, folder: folder).Of(preset, TestContext.Current.CancellationToken);
        var said = await new PresetThumbnails(NodeCatalog.BuiltIn, folder: folder).Said(preset);

        builds.ShouldBe(1);
        said.Description.ShouldBe(drawn.Description);
    }

    [Fact]
    public async Task What_a_patch_says_is_read_without_drawing_it()
    {
        var preset = Pictured();

        var said = await new PresetThumbnails(NodeCatalog.BuiltIn).Said(preset);

        said.Description.ShouldBe(preset.Build(NodeCatalog.BuiltIn).Description);
        said.Pixels.ShouldBeNull();
    }

    /// <summary>A gallery closed before a tile was reached gives it up, and the next one to ask has it drawn.</summary>
    [Fact]
    public async Task A_thumbnail_given_up_is_drawn_when_asked_for_again()
    {
        var thumbnails = new PresetThumbnails(NodeCatalog.BuiltIn);
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

        var drawn = await new PresetThumbnails(NodeCatalog.BuiltIn, folder: folder).Of(preset, TestContext.Current.CancellationToken);

        drawn.Words.ShouldBe(Thumbnail.Unavailable.Words);
        (Directory.Exists(folder) ? Directory.GetFiles(folder, "*.thumb") : []).ShouldBeEmpty();
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
}
