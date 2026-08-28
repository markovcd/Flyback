using System.IO.Compression;
using System.Text;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The bundle: a patch and everything it names, in one file.
/// </summary>
/// <remarks>
/// What is worth pinning is mostly what a bundle is <em>for</em> — that the patch
/// inside points at the copies beside it rather than at wherever they came from,
/// that the document being packed is not changed by packing it, and that a
/// bundle read back draws what the original drew on a machine holding none of the
/// files. The zip is the framework's and is not tested here; what is tested is
/// the layout inside it, because that is a promise to every other program that
/// might open one.
/// </remarks>
public class PatchBundleTests
{
    private const string Wav = @"C:\sounds\drums.wav";
    private const string Png = @"D:\pictures\moon.png";

    // --- what is in a bundle ---------------------------------------------------

    [Fact]
    public void A_bundle_holds_the_patch_and_the_files_it_names()
    {
        var report = Packed(Both(), out var archive);

        report.Whole.ShouldBeTrue();
        report.Carried.ShouldBe([Wav, Png], ignoreOrder: true);
        report.Missing.ShouldBeEmpty();

        Entries(archive).ShouldBe(
            [PatchBundle.PatchEntry, "files/drums.wav", "files/moon.png"], ignoreOrder: true);
    }

    /// <summary>
    /// The whole trick. A relative path is measured from wherever the patch is,
    /// so a patch whose paths name the copies beside it works the moment it is
    /// unpacked, with nothing else arranged.
    /// </summary>
    [Fact]
    public void The_patch_inside_names_the_copies_beside_it()
    {
        Packed(Both(), out var archive);

        var read = PatchBundle.Read(new MemoryStream(archive), NodeCatalog.BuiltIn);

        read.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.SampleTypeId).Sample
            .ShouldBe("files/drums.wav");

        read.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.PictureTypeId).Picture
            .ShouldBe("files/moon.png");

        read.Files.Keys.ShouldBe(["files/drums.wav", "files/moon.png"], ignoreOrder: true);
    }

    /// <summary>
    /// Packing is a copy, not an edit. Somebody who saved a bundle has not
    /// changed the document they are working on, and the paths on screen still
    /// name the files on their machine.
    /// </summary>
    [Fact]
    public void Packing_leaves_the_patch_that_was_packed_alone()
    {
        var patch = Both();

        Packed(patch, out _);

        patch.Nodes.Single(n => n.TypeId == NodeCatalog.SampleTypeId).Sample.ShouldBe(Wav);
        patch.Nodes.Single(n => n.TypeId == NodeCatalog.PictureTypeId).Picture.ShouldBe(Png);
    }

    /// <summary>
    /// One file named twice is one copy, and both modules end up pointed at it —
    /// which is the difference between a bundle and a folder somebody assembled
    /// by hand.
    /// </summary>
    [Fact]
    public void A_file_named_twice_is_carried_once()
    {
        var patch = new Patch();

        foreach (var _ in new[] { 1, 2 })
        {
            var node = Add(patch, NodeCatalog.PictureTypeId);
            node.Picture = Png;
        }

        var report = Packed(patch, out var archive);

        report.Carried.ShouldBe([Png]);
        Entries(archive).Count(entry => entry.StartsWith("files/")).ShouldBe(1);

        PatchBundle.Read(new MemoryStream(archive), NodeCatalog.BuiltIn).Patch.Nodes
            .Where(n => n.TypeId == NodeCatalog.PictureTypeId)
            .ShouldAllBe(n => n.Picture == "files/moon.png");
    }

    /// <summary>
    /// Two folders may each hold a <c>moon.png</c>, and inside a bundle there is
    /// one folder. Numbered rather than hashed, so what comes out is still a name
    /// somebody recognises.
    /// </summary>
    [Fact]
    public void Two_files_of_the_same_name_are_both_carried()
    {
        var patch = new Patch();

        foreach (var path in new[] { @"C:\one\moon.png", @"C:\two\moon.png" })
        {
            var node = Add(patch, NodeCatalog.PictureTypeId);
            node.Picture = path;
        }

        var report = Packed(patch, out var archive, _ => [1, 2, 3]);

        report.Carried.Count.ShouldBe(2);
        Entries(archive).ShouldContain("files/moon.png");
        Entries(archive).ShouldContain("files/moon (2).png");

        var read = PatchBundle.Read(new MemoryStream(archive), NodeCatalog.BuiltIn);

        read.Patch.Nodes
            .Where(n => n.TypeId == NodeCatalog.PictureTypeId)
            .Select(n => n.Picture)
            .ShouldBe(["files/moon.png", "files/moon (2).png"], ignoreOrder: true);
    }

    /// <summary>
    /// A file that has gone is said rather than refused. The patch it belongs to
    /// still opens, still compiles and still draws — to black or to silence — so a
    /// bundle of it does too, and what the caller does about it is the caller's.
    /// </summary>
    [Fact]
    public void A_file_that_cannot_be_read_is_reported_and_the_bundle_is_still_written()
    {
        var report = Packed(Both(), out var archive, path => path == Wav ? [1, 2, 3] : null);

        report.Whole.ShouldBeFalse();
        report.Carried.ShouldBe([Wav]);
        report.Missing.ShouldBe([Png]);

        var read = PatchBundle.Read(new MemoryStream(archive), NodeCatalog.BuiltIn);

        // The one that was carried is rewritten and the one that was not is left
        // saying what it always said, so a bundle made on the wrong machine can
        // still be repaired by putting the file back where the patch says.
        read.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.SampleTypeId).Sample
            .ShouldBe("files/drums.wav");

        read.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.PictureTypeId).Picture.ShouldBe(Png);
    }

    /// <summary>
    /// What a bundle is about to be asked for, offered on its own — because the
    /// same list is what a bundle being saved as a loose patch has to write out
    /// beside it.
    /// </summary>
    [Fact]
    public void A_patch_says_which_files_it_names()
    {
        PatchBundle.Files(Both(), NodeCatalog.BuiltIn).ShouldBe([Wav, Png], ignoreOrder: true);

        // Once each, however many modules name it.
        var twice = new Patch();

        foreach (var _ in new[] { 1, 2 }) Add(twice, NodeCatalog.PictureTypeId).Picture = Png;

        PatchBundle.Files(twice, NodeCatalog.BuiltIn).ShouldBe([Png]);

        // And nothing at all for a patch that names nothing, which is nearly all
        // of them.
        PatchBundle.Files(Presets.Plasma(NodeCatalog.BuiltIn), NodeCatalog.BuiltIn).ShouldBeEmpty();
    }

    [Fact]
    public void A_patch_naming_nothing_is_a_bundle_of_one_entry()
    {
        var patch = new Patch();
        Add(patch, NodeCatalog.OutputTypeId);

        var report = Packed(patch, out var archive);

        report.Carried.ShouldBeEmpty();
        Entries(archive).ShouldBe([PatchBundle.PatchEntry]);
    }

    // --- reading one back ------------------------------------------------------

    /// <summary>
    /// The point of reading without unpacking: a patch drawn on a machine holding
    /// none of its files loose, out of a single file, with nothing written
    /// anywhere. Which is what the command line does with one.
    /// </summary>
    [Fact]
    public void A_bundle_compiles_and_draws_from_memory()
    {
        var picture = Drawing();

        var patch = new Patch();
        var shown = Add(patch, NodeCatalog.PictureTypeId);
        shown.Picture = Png;

        var output = Add(patch, NodeCatalog.OutputTypeId);
        patch.Connect(shown.Id, 0, output.Id, NodeCatalog.OutputColorPort);

        var archive = new MemoryStream();
        PatchBundle.Write(archive, patch, _ => picture, NodeCatalog.BuiltIn);

        archive.Position = 0;
        var read = PatchBundle.Read(archive, NodeCatalog.BuiltIn);
        var files = BundleFiles.Of(read);

        var drawn = read.Patch.CompileForVideo(NodeCatalog.BuiltIn, pictures: files);

        drawn.Issues.ShouldBeEmpty();
        drawn.Program.Pictures.Count.ShouldBe(1);

        // The middle of a picture that is red on the left and blue on the right.
        var registers = drawn.Program.AllocateRegisters();
        drawn.Program.Evaluate(-0.5d, 0d, 0d, registers, default);

        registers[drawn.Program.OutputBase].ShouldBe(1d, 0.01);
        registers[drawn.Program.OutputBase + 2].ShouldBe(0d, 0.01);
    }

    [Fact]
    public void A_bundle_that_holds_no_patch_is_refused()
    {
        var archive = new MemoryStream();

        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        using (var writing = zip.CreateEntry("files/moon.png").Open())
            writing.Write(Encoding.ASCII.GetBytes("not a patch"));

        archive.Position = 0;

        Should.Throw<InvalidDataException>(() => PatchBundle.Read(archive));
    }

    /// <summary>
    /// The property that makes a bundle a document rather than an archive: it is
    /// opened, worked on and written again without ever being unpacked, and what
    /// comes out the second time is what went in the first — the same picture,
    /// byte for byte, rather than one re-encoded on its way through.
    /// </summary>
    [Fact]
    public void A_bundle_opened_and_saved_again_carries_the_same_bytes()
    {
        var drawing = Drawing();

        var patch = new Patch();
        var shown = Add(patch, NodeCatalog.PictureTypeId);
        shown.Picture = Png;
        Add(patch, NodeCatalog.OutputTypeId);

        var first = new MemoryStream();
        PatchBundle.Write(first, patch, _ => drawing, NodeCatalog.BuiltIn);

        // Opened, and the files held rather than written anywhere.
        first.Position = 0;
        var opened = PatchBundle.Read(first, NodeCatalog.BuiltIn);
        var files = new BundleFiles(opened.Files);

        // Edited, the way the window edits: the patch that came out of the
        // bundle is the document now.
        opened.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.PictureTypeId).Name = "Moon";

        // And saved again, out of what it is carrying rather than off a disk it
        // never touched.
        var second = new MemoryStream();

        var report = PatchBundle.Write(
            second,
            opened.Patch,
            path => files.Bytes.TryGetValue(path, out var bytes) ? bytes : null,
            NodeCatalog.BuiltIn);

        report.Whole.ShouldBeTrue();

        second.Position = 0;
        var again = PatchBundle.Read(second, NodeCatalog.BuiltIn);

        // The same name inside, so a bundle saved twice does not accumulate
        // numbered copies of its own files.
        again.Files.Keys.ShouldBe(["files/moon.png"]);
        again.Files["files/moon.png"].ShouldBe(drawing);

        again.Patch.Nodes.Single(n => n.TypeId == NodeCatalog.PictureTypeId).Name.ShouldBe("Moon");
    }

    /// <summary>
    /// A bundle is not necessarily everything a patch names for ever: somebody
    /// working on one may point a module at a file on their own machine, and
    /// that file is where they said it is. Which is what the folder behind the
    /// archive answers for.
    /// </summary>
    [Fact]
    public void What_the_bundle_does_not_hold_is_looked_for_behind_it()
    {
        var loose = new Loose();

        var files = new BundleFiles(
            new Dictionary<string, byte[]> { ["files/moon.png"] = Drawing() },
            behindPictures: loose);

        ((IImageLibrary)files).Find("files/moon.png").ShouldNotBeNull();
        loose.Asked.ShouldBe(0);

        // Anything else falls through, once, and is then the folder's answer
        // rather than the archive's.
        ((IImageLibrary)files).Find(@"C:\elsewhere\new.png").ShouldNotBeNull();
        loose.Asked.ShouldBe(1);

        files.Explain(@"C:\elsewhere\gone.png").ShouldBe("the folder was asked.");
        files.Explain("files/moon.png").ShouldBe("the bundle holds it, but it could not be read.");
    }

    /// <summary>Stands in for the folder libraries behind an open bundle.</summary>
    private sealed class Loose : IImageLibrary
    {
        public int Asked { get; private set; }

        public LoadedImage? Find(string path)
        {
            Asked++;
            return new LoadedImage([1f, 1f, 1f], 1, 1);
        }

        public string Explain(string path) => "the folder was asked.";
    }

    [Fact]
    public void The_library_says_what_it_does_and_does_not_hold()
    {
        var files = new BundleFiles(new Dictionary<string, byte[]>
        {
            ["files/moon.png"] = Drawing(),
            ["files/broken.png"] = [1, 2, 3],
        });

        ((IImageLibrary)files).Find("files/moon.png").ShouldNotBeNull();
        ((IImageLibrary)files).Find("files/nowhere.png").ShouldBeNull();

        files.Explain("files/nowhere.png").ShouldBe("the bundle does not hold it.");
        files.Explain("files/broken.png").ShouldBe("the bundle holds it, but it could not be read.");

        // Asked twice, decoded once — the same cache the folder libraries keep
        // and for the same reason.
        ((IImageLibrary)files).Find("files/moon.png")
            .ShouldBeSameAs(((IImageLibrary)files).Find("files/moon.png"));
    }

    // --- harness ---------------------------------------------------------------

    private static BundleReport Packed(Patch patch, out byte[] archive, Func<string, byte[]?>? open = null)
    {
        var stream = new MemoryStream();

        var report = PatchBundle.Write(
            stream, patch, open ?? (_ => [1, 2, 3]), NodeCatalog.BuiltIn);

        archive = stream.ToArray();
        return report;
    }

    private static List<string> Entries(byte[] archive)
    {
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);

        return [.. zip.Entries.Select(entry => entry.FullName)];
    }

    /// <summary>A patch naming one of each kind of file there is.</summary>
    private static Patch Both()
    {
        var patch = new Patch();

        var player = Add(patch, NodeCatalog.SampleTypeId);
        player.Sample = Wav;

        var shown = Add(patch, NodeCatalog.PictureTypeId);
        shown.Picture = Png;

        Add(patch, NodeCatalog.OutputTypeId);

        return patch;
    }

    /// <summary>A real PNG, so that what comes out of a bundle can be drawn.</summary>
    private static byte[] Drawing()
    {
        const int width = 4;
        const int height = 2;

        var stride = width * 4;
        var bgra = new byte[stride * height];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var at = y * stride + x * 4;

            bgra[at + 0] = x < width / 2 ? (byte)0 : (byte)255;   // blue on the right
            bgra[at + 2] = x < width / 2 ? (byte)255 : (byte)0;   // red on the left
            bgra[at + 3] = 255;
        }

        var file = new MemoryStream();
        PngWriter.WriteBgra(file, bgra, width, height, stride);

        return file.ToArray();
    }

    private static NodeInstance Add(Patch patch, string typeId)
    {
        var node = NodeInstance.Create(NodeCatalog.BuiltIn.Require(typeId), 0, 0);
        patch.Nodes.Add(node);

        return node;
    }
}
