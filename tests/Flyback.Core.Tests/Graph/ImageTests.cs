using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Image module: the one thing in the catalogue that is not arithmetic.
/// </summary>
/// <remarks>
/// Everything else here computes what it draws, and this reads it off a disk. So
/// what is worth pinning is the seam rather than the sampling — which program is
/// allowed to open a file, what a patch that names one that has gone draws, and
/// that a picture lands exactly where the module says it does, since a placement
/// that is out by half a frame is a picture rather than a fault.
/// </remarks>
public class ImageTests
{
    private const string Image = NodeCatalog.PictureTypeId;

    /// <summary>
    /// A picture with a known color in every corner and a different one in the
    /// middle, so that where it landed can be read off what came back.
    /// </summary>
    private static LoadedImage Swatches { get; } = new(
    [
        1f, 0f, 0f,   0f, 1f, 0f,   // red    green
        0f, 0f, 1f,   1f, 1f, 0f,   // blue   yellow
    ], 2, 2);

    /// <summary>Stands in for the folder, so a test says what a file is without writing one.</summary>
    private sealed class Shelf(params (string Path, LoadedImage Picture)[] held) : IImageLibrary
    {
        public int Asked { get; private set; }

        public LoadedImage? Find(string path)
        {
            Asked++;
            return held.FirstOrDefault(entry => entry.Path == path).Picture;
        }

        public string Explain(string path) => "there is no file there.";
    }

    // --- the module ------------------------------------------------------------

    [Fact]
    public void An_image_carries_a_path_and_a_fresh_one_carries_none()
    {
        var def = NodeCatalog.BuiltIn.Require(Image);

        def.Extra<PictureExtra>().ShouldNotBeNull();
        def.Outputs.Single().Kind.ShouldBe(PortKind.Color);

        // Its position sockets read the pixel's own place with no wire, like
        // every other module that wants one.
        NodeCatalog.BuiltIn.Normalled(def.Inputs[0]).ShouldBe("Coordinates x");

        PictureExtra.Of(NodeInstance.Create(def, 0, 0)).ShouldBe(string.Empty);
    }

    /// <summary>
    /// A patch names its pictures rather than carrying them, so the name has to
    /// survive being written down — under its own key in the store, like
    /// everything else a module carries that is not a knob.
    /// </summary>
    [Fact]
    public void The_path_survives_a_save_and_a_load()
    {
        var patch = new Patch();
        var node = NodeInstance.Create(NodeCatalog.BuiltIn.Require(Image), 40, 50);

        PictureExtra.Set(node, "pictures/moon.png");
        patch.Nodes.Add(node);

        var back = PatchIO.Read(PatchIO.ToJson(patch)).Patch;

        PictureExtra.Of(back.Nodes.Single(n => n.TypeId == Image)).ShouldBe("pictures/moon.png");
    }

    // --- where the picture lands -----------------------------------------------

    /// <summary>
    /// The rule the whole module rests on: the picture fills the height and as
    /// much of the width as its own shape asks for, and is black beyond. A
    /// two-by-two is square, so on a square frame each corner of the frame is a
    /// corner of the picture.
    /// </summary>
    [Theory]
    [InlineData(-0.5f, 0.5f, 1f, 0f, 0f)]    // top left is red
    [InlineData(0.5f, 0.5f, 0f, 1f, 0f)]     // top right is green
    [InlineData(-0.5f, -0.5f, 0f, 0f, 1f)]   // bottom left is blue
    [InlineData(0.5f, -0.5f, 1f, 1f, 0f)]    // and bottom right is yellow
    public void A_picture_fills_the_frame_it_is_the_shape_of(
        float x, float y, float r, float g, float b)
    {
        var seen = Shown(x, y);

        seen.R.ShouldBe(r, 1e-5);
        seen.G.ShouldBe(g, 1e-5);
        seen.B.ShouldBe(b, 1e-5);
    }

    /// <summary>
    /// Black outside rather than the edge held or the picture tiled. Holding the
    /// edge smears the last row across everything beyond it and reads as a fault;
    /// tiling is something a patch says with a Tile.
    /// </summary>
    [Theory]
    [InlineData(1.4f, 0f)]
    [InlineData(-1.4f, 0f)]
    [InlineData(0f, 1.4f)]
    [InlineData(0f, -1.4f)]
    [InlineData(2f, 2f)]
    public void Everywhere_outside_a_picture_is_black(float x, float y)
    {
        var seen = Shown(x, y);

        seen.R.ShouldBe(0d);
        seen.G.ShouldBe(0d);
        seen.B.ShouldBe(0d);
    }

    /// <summary>
    /// A wide picture is wide. It spans its own aspect either side of the middle,
    /// so a sixteen-by-nine one exactly fills a sixteen-by-nine frame — which is
    /// what makes a still this program exported land where it came from.
    /// </summary>
    [Fact]
    public void A_wide_picture_reaches_further_across_than_a_square_one()
    {
        var wide = new LoadedImage([1f, 1f, 1f, 1f, 1f, 1f], 2, 1);

        // Half a unit past the right edge of a square picture, and well inside a
        // picture twice as wide as it is tall.
        Shown(1.5f, 0f, wide).R.ShouldBe(1d, 1e-5);
        Shown(1.5f, 0f).R.ShouldBe(0d);

        // And off the end of even that one.
        Shown(2.4f, 0f, wide).R.ShouldBe(0d);
    }

    [Fact]
    public void A_picture_is_read_between_its_pixels_rather_than_at_the_nearest()
    {
        // Halfway between the red and the green along the top row.
        var seen = Shown(0f, 0.5f);

        seen.R.ShouldBe(0.5d, 0.01);
        seen.G.ShouldBe(0.5d, 0.01);
        seen.B.ShouldBe(0d, 1e-6);
    }

    // --- which program may read a file -----------------------------------------

    /// <summary>
    /// The speakers have nothing to do with a picture, so the compiler hands
    /// their walk no library at all — the file is not opened, no complaint is
    /// made about one that has gone, and the module lowers to black.
    /// </summary>
    [Fact]
    public void The_speakers_never_open_a_picture()
    {
        var shelf = new Shelf(("moon.png", Swatches));
        var patch = Patch("moon.png");

        var heard = patch.CompileForAudio(NodeCatalog.BuiltIn, pictures: shelf);

        heard.Program.Pictures.ShouldBeEmpty();
        heard.Program.Ops.ShouldNotContain(op => op.Code == OpCode.SamplePicture);
        heard.Issues.ShouldBeEmpty();
        shelf.Asked.ShouldBe(0);

        // And the screen does, which is the other half of the same sentence.
        patch.CompileForVideo(NodeCatalog.BuiltIn, pictures: shelf).Program.Pictures.Count.ShouldBe(1);
        shelf.Asked.ShouldBe(1);
    }

    [Fact]
    public void A_picture_that_is_not_there_is_black_and_says_so_by_name()
    {
        var missing = Patch("gone.png").CompileForVideo(NodeCatalog.BuiltIn, pictures: new Shelf());

        missing.Issues.ShouldContain(issue =>
            issue.Message.Contains("gone.png") && issue.Severity == IssueSeverity.Error);

        missing.Program.Pictures.ShouldBeEmpty();
        Read(missing.Program, 0f, 0f).R.ShouldBe(0d);

        // Nothing chosen at all is a warning rather than an error: a module just
        // placed has no file yet and has done nothing wrong.
        var empty = Patch(string.Empty).CompileForVideo(NodeCatalog.BuiltIn, pictures: new Shelf());

        empty.Issues.Single().Severity.ShouldBe(IssueSeverity.Warning);
    }

    /// <summary>
    /// Two modules showing one file share one texture, which is what stops a
    /// patch that puts a photograph in four places uploading it four times.
    /// </summary>
    [Fact]
    public void One_file_shown_twice_is_uploaded_once()
    {
        var other = new LoadedImage([0f, 0f, 0f], 1, 1);
        var shelf = new Shelf(("a.png", Swatches), ("b.png", other));

        var patch = new Patch();
        var output = Add(patch, NodeCatalog.OutputTypeId);
        // Into a Mixer rather than all onto one socket, because a second wire
        // into the same place replaces the first and two of these would be dead
        // code rather than two pictures.
        var mix = Add(patch, "math.mixer");
        var into = 0;

        foreach (var path in new[] { "a.png", "a.png", "b.png" })
        {
            var node = Add(patch, Image);
            PictureExtra.Set(node, path);
            patch.Connect(node.Id, 0, mix.Id, into);
            into += 2;
        }

        patch.Connect(mix.Id, 0, output.Id, NodeCatalog.OutputColorPort);

        var drawn = patch.CompileForVideo(NodeCatalog.BuiltIn, pictures: shelf).Program;

        drawn.Ops.Count(op => op.Code == OpCode.SamplePicture).ShouldBe(3);
        drawn.Pictures.Count.ShouldBe(2);
    }

    // --- the shader ------------------------------------------------------------

    /// <summary>
    /// The claim ADR-0052 made and could not act on: a clip is a buffer the
    /// shader has nowhere to put, and a picture is a texture, which is what a
    /// shader is made of. So this is the first op to bring a file into a program
    /// and keep the GPU.
    /// </summary>
    [Theory]
    [InlineData(GlslDialect.GlslEs300)]
    [InlineData(GlslDialect.Glsl150)]
    public void A_picture_lowers_to_a_texture_read(GlslDialect dialect)
    {
        var drawn = Patch("moon.png")
            .CompileForVideo(NodeCatalog.BuiltIn, pictures: new Shelf(("moon.png", Swatches)))
            .Program;

        drawn.Tables.ShouldBeEmpty();

        var shader = GlslEmitter.Emit(drawn, dialect);

        shader.PictureCount.ShouldBe(1);
        shader.PatchFragment.ShouldContain("uniform sampler2D uPicture0;");
        shader.PatchFragment.ShouldContain("uniform float uPictureAspect0;");
        shader.PatchFragment.ShouldContain("pic0(");
    }

    /// <summary>
    /// A patch with two of them declares two, and the second is named after its
    /// own position — which is the contract between this text and whatever binds
    /// the textures.
    /// </summary>
    [Fact]
    public void Every_picture_gets_a_sampler_of_its_own()
    {
        var shelf = new Shelf(("a.png", Swatches), ("b.png", new LoadedImage([1f, 1f, 1f], 1, 1)));

        var patch = new Patch();
        var output = Add(patch, NodeCatalog.OutputTypeId);
        var mix = Add(patch, "math.mixer");
        var into = 0;

        foreach (var path in new[] { "a.png", "b.png" })
        {
            var node = Add(patch, Image);
            PictureExtra.Set(node, path);
            patch.Connect(node.Id, 0, mix.Id, into);
            into += 2;
        }

        patch.Connect(mix.Id, 0, output.Id, NodeCatalog.OutputColorPort);

        var shader = GlslEmitter.Emit(
            patch.CompileForVideo(NodeCatalog.BuiltIn, pictures: shelf).Program,
            GlslDialect.GlslEs300);

        shader.PictureCount.ShouldBe(2);
        shader.PatchFragment.ShouldContain("uPicture1");
        shader.PatchFragment.ShouldContain("pic1(");
    }

    // --- end to end ------------------------------------------------------------

    /// <summary>
    /// The property the whole thing is for, and the one that ties the reader, the
    /// writer, the placement and the module together: a frame this program
    /// exported, read back in, is the frame it was — in the same place, at the
    /// same size, to the eight bits it was written with.
    /// </summary>
    [Fact]
    public void A_frame_exported_and_read_back_is_the_frame_it_was()
    {
        const int width = 64;
        const int height = 36;

        var plasma = Presets.Plasma(NodeCatalog.BuiltIn).CompileForVideo(NodeCatalog.BuiltIn).Program;

        var stride = width * 4;
        var pixels = new byte[stride * height];
        new SynthRenderer().Render(plasma, 0.7d, width, height, pixels, stride);

        var file = new MemoryStream();
        PngWriter.WriteBgra(file, pixels, width, height, stride);
        file.Position = 0;

        var read = PngReader.Read(file, out _).ShouldNotBeNull();

        // Shown through the module, at the aspect the frame was rendered at, and
        // read back at the middle of each pixel.
        var shown = Patch("frame.png")
            .CompileForVideo(NodeCatalog.BuiltIn, pictures: new Shelf(("frame.png", read)))
            .Program;

        var registers = shown.AllocateRegisters();
        var aspect = (double)width / height;

        for (var y = 0; y < height; y += 7)
        for (var x = 0; x < width; x += 7)
        {
            var at = ((x + 0.5d) / width * 2d - 1d) * aspect;
            var down = 1d - (y + 0.5d) / height * 2d;

            shown.Evaluate(at, down, 0d, registers, default, aspect: aspect);

            var wrote = y * stride + x * 4;

            registers[shown.OutputBase].ShouldBe(pixels[wrote + 2] / 255d, 1e-6, $"red at {x},{y}");
            registers[shown.OutputBase + 1].ShouldBe(pixels[wrote + 1] / 255d, 1e-6, $"green at {x},{y}");
            registers[shown.OutputBase + 2].ShouldBe(pixels[wrote + 0] / 255d, 1e-6, $"blue at {x},{y}");
        }
    }

    // --- harness ---------------------------------------------------------------

    private static (double R, double G, double B) Shown(float x, float y, LoadedImage? picture = null)
    {
        var shelf = new Shelf(("moon.png", picture ?? Swatches));
        var drawn = Patch("moon.png").CompileForVideo(NodeCatalog.BuiltIn, pictures: shelf).Program;

        return Read(drawn, x, y);
    }

    private static (double R, double G, double B) Read(CompiledPatch program, double x, double y)
    {
        var registers = program.AllocateRegisters();
        program.Evaluate(x, y, 0d, registers, default);

        return (
            registers[program.OutputBase],
            registers[program.OutputBase + 1],
            registers[program.OutputBase + 2]);
    }

    private static Patch Patch(string path)
    {
        var patch = new Patch();

        var node = Add(patch, Image);
        PictureExtra.Set(node, path);

        var output = Add(patch, NodeCatalog.OutputTypeId);
        patch.Connect(node.Id, 0, output.Id, NodeCatalog.OutputColorPort);

        return patch;
    }

    private static NodeInstance Add(Patch patch, string typeId)
    {
        var node = NodeInstance.Create(NodeCatalog.BuiltIn.Require(typeId), 0, 0);
        patch.Nodes.Add(node);

        return node;
    }
}
