using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests;

/// <summary>
/// The presets somebody saved: a folder of bundles, one a preset, named by its file.
/// </summary>
public class PresetLibraryTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "flyback-presets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    private PresetLibrary Library() => new(folder);

    /// <summary>Time into a sine into the Output.</summary>
    private static Patch Tone(float hertz = 220f)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = b.Add("time", 0, 0);
        var osc = b.Add("osc.sine", 300, 0, (1, hertz));
        var sink = b.Add(NodeCatalog.OutputTypeId, 600, 0);

        b.Wire(time, 0, osc, 0).Wire(osc, 0, sink, NodeCatalog.OutputLeftPort);

        return b.Patch;
    }

    private static byte[]? Nothing(string path) => null;

    /// <summary>A white picture, as the bytes of the file a patch would name.</summary>
    private static byte[] White()
    {
        var pixels = new byte[4 * 4 * 4];
        Array.Fill(pixels, (byte)255);

        using var file = new MemoryStream();
        PngWriter.WriteBgra(file, pixels, 4, 4, 16);

        return file.ToArray();
    }

    /// <summary>
    /// A preset saved with the picture it shows is opened with it, and its tile is
    /// drawn from it.
    /// </summary>
    [Fact]
    public async Task A_saved_preset_is_opened_and_drawn_with_the_files_in_its_bundle()
    {
        var patch = new Patch();
        var shown = NodeInstance.Create(NodeCatalog.BuiltIn.Require(NodeCatalog.PictureTypeId), 0, 0);
        var sink = NodeInstance.Create(NodeCatalog.BuiltIn.Require(NodeCatalog.OutputTypeId), 300, 0);

        PictureExtra.Set(shown, "white.png");
        patch.Nodes.Add(shown);
        patch.Nodes.Add(sink);
        patch.Connect(shown.Id, 0, sink.Id, NodeCatalog.OutputColorPort);

        var library = Library();
        var saved = library.Save("Shown", patch, name => name == "white.png" ? White() : null, NodeCatalog.BuiltIn);

        var opened = PresetLibrary.Open(saved.Preset, library, NodeCatalog.BuiltIn);

        // Packing renames what it carries, so the name is asked of the patch that came back.
        var named = PictureExtra.Of(opened.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.PictureTypeId));

        opened.Pictures.Find(named).ShouldNotBeNull(opened.Pictures.Explain(named));

        var tile = await new PresetThumbnails(NodeCatalog.BuiltIn) { Saved = library }.Of(saved.Preset);

        tile.Pixels.ShouldNotBeNull();
        tile.Pixels.ShouldContain((byte)255);

        var bare = await new PresetThumbnails(NodeCatalog.BuiltIn).Of(saved.Preset);

        bare.Pixels.ShouldBeNull("without its bundle the picture is a file that cannot be found");
    }

    [Fact]
    public void A_saved_preset_is_a_bundle_listed_by_its_name()
    {
        var library = Library();

        library.All.ShouldBeEmpty("nothing has been saved yet");

        library.Save("Low hum", Tone(), Nothing, NodeCatalog.BuiltIn);

        var saved = library.All.ShouldHaveSingleItem();

        saved.Name.ShouldBe("Low hum");
        saved.Path.ShouldBe(Path.Combine(folder, "Low hum" + PatchBundle.Extension));
        File.Exists(saved.Path).ShouldBeTrue();

        new PresetLibrary(folder).All.ShouldHaveSingleItem().Name.ShouldBe("Low hum", "it is read back off the disk");
    }

    [Fact]
    public void A_saved_preset_builds_the_patch_it_was_saved_from()
    {
        var library = Library();
        var patch = Tone(330f);

        library.Save("Tone", patch, Nothing, NodeCatalog.BuiltIn);

        var built = library.All.Single().Preset.Build(NodeCatalog.BuiltIn);

        built.Nodes.Select(n => n.TypeId).ShouldBe(patch.Nodes.Select(n => n.TypeId));
        built.Connections.Count.ShouldBe(patch.Connections.Count);
        built.Nodes.Single(n => n.TypeId == "osc.sine").InputValues[1].ShouldBe(330f);
    }

    /// <summary>
    /// Saving under a name already saved replaces it — one entry, the new patch —
    /// and is a new preset, so nothing drawn from the old one is shown for it.
    /// </summary>
    [Fact]
    public void Saving_under_a_name_already_saved_replaces_it()
    {
        var library = Library();

        library.Save("Tone", Tone(), Nothing, NodeCatalog.BuiltIn);
        var before = library.All.Single().Preset;

        library.Save("tone", Tone(440f), Nothing, NodeCatalog.BuiltIn);

        var after = library.All.ShouldHaveSingleItem();

        after.Preset.ShouldNotBeSameAs(before);
        after.Preset.Build(NodeCatalog.BuiltIn).Nodes.Single(n => n.TypeId == "osc.sine").InputValues[1].ShouldBe(440f);
    }

    [Fact]
    public void A_preset_stays_the_same_preset_across_a_save_of_another()
    {
        var library = Library();

        library.Save("One", Tone(), Nothing, NodeCatalog.BuiltIn);
        var one = library.All.Single().Preset;

        library.Save("Two", Tone(), Nothing, NodeCatalog.BuiltIn);

        library.All.Select(entry => entry.Name).ShouldBe(["One", "Two"]);
        library.Holding(one).ShouldNotBeNull().Name.ShouldBe("One");
    }

    [Fact]
    public void Removing_one_deletes_its_file()
    {
        var library = Library();
        var saved = library.Save("Gone", Tone(), Nothing, NodeCatalog.BuiltIn);

        library.Remove(saved).ShouldBeTrue();

        library.All.ShouldBeEmpty();
        File.Exists(saved.Path).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("In/Out")]
    [InlineData("a:b")]
    [InlineData("Trailing.")]
    public void A_name_that_cannot_be_a_file_name_is_refused(string name)
    {
        PresetLibrary.Refusal(name).ShouldNotBeNull();

        Should.Throw<ArgumentException>(() => Library().Save(name, Tone(), Nothing, NodeCatalog.BuiltIn));
    }

    /// <summary>A patch dropped into the folder by hand is a preset too.</summary>
    [Fact]
    public void A_plain_patch_in_the_folder_is_listed()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"By hand.{PatchIO.FileExtension}"), PatchIO.ToJson(Tone()));

        var saved = Library().All.ShouldHaveSingleItem();

        saved.Name.ShouldBe("By hand");
        saved.Preset.Build(NodeCatalog.BuiltIn).Nodes.Count.ShouldBe(3);
    }
}
