using Flyback.Core.Render;
using System.Text.Json;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// The three commands, called the way the argument parser calls them.
/// </summary>
/// <remarks>
/// What is worth testing here is the part that is this program's own: which
/// exit code a shell gets, and whether the two output modes say the same thing.
/// The rendering underneath is Core's and is tested there; the parsing above is
/// System.CommandLine's and is not ours to prove.
/// </remarks>
public class CommandTests
{
    private static Patch Preset(string name) =>
        Presets.All.Single(p => p.Name == name).Build(NodeCatalog.BuiltIn);

    /// <summary>A patch naming a module nothing defines, which is what an error looks like.</summary>
    private static Patch Broken()
    {
        var patch = Preset("Plasma");
        patch.Nodes.Add(new NodeInstance { Id = Guid.NewGuid(), TypeId = "osc.nonesuch" });

        // Wired in, or the compiler never walks to it: an unknown module nothing
        // reaches costs nothing and is rightly not complained about.
        patch.Connect(patch.Nodes[^1].Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);

        return patch;
    }

    private static (int Code, string Out, string Error) Run(
        Func<TextWriter, TextWriter, int> command)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        return (command(output, error), output.ToString(), error.ToString());
    }

    // --- check ---------------------------------------------------------------

    [Fact]
    public void A_patch_with_nothing_wrong_with_it_says_so_and_succeeds()
    {
        var (code, output, _) = Run((o, e) => CheckCommand.Run(Preset("Plasma"), "plasma.fbk", false, o, e));

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("nothing to report");
    }

    /// <summary>
    /// The whole point of the command: a build can fail on it. A warning must
    /// not, or every patch with an unpatched domain would break somebody's
    /// pipeline over something they meant.
    /// </summary>
    [Fact]
    public void An_error_fails_and_a_warning_does_not()
    {
        var broken = Run((o, e) => CheckCommand.Run(Broken(), "broken.fbk", false, o, e));
        broken.Code.ShouldBe(Exit.Problems);

        // The empty preset is the Output alone, which warns and nothing more.
        var empty = Run((o, e) => CheckCommand.Run(Preset("Empty"), "empty.fbk", false, o, e));
        empty.Code.ShouldBe(Exit.Ok);
        empty.Out.ShouldContain("warning");
    }

    [Fact]
    public void An_issue_names_the_module_rather_than_its_id()
    {
        var (_, output, _) = Run((o, e) => CheckCommand.Run(Preset("Empty"), "empty.fbk", false, o, e));

        output.ShouldContain("Output:");
        output.ShouldNotContain(Preset("Empty").Output.Id.ToString());
    }

    [Fact]
    public void The_json_and_the_prose_agree_about_how_bad_it_is()
    {
        var patch = Broken();

        var prose = Run((o, e) => CheckCommand.Run(patch, "broken.fbk", false, o, e));
        var json = Run((o, e) => CheckCommand.Run(patch, "broken.fbk", true, o, e));

        json.Code.ShouldBe(prose.Code);

        using var read = JsonDocument.Parse(json.Out);
        read.RootElement.GetProperty("errors").GetInt32().ShouldBeGreaterThan(0);
        read.RootElement.GetProperty("issues").GetArrayLength().ShouldBe(
            read.RootElement.GetProperty("errors").GetInt32()
            + read.RootElement.GetProperty("warnings").GetInt32());
    }

    /// <summary>Machine-readable means machine-readable: nothing but the document on stdout.</summary>
    [Fact]
    public void Json_output_is_a_document_and_nothing_else()
    {
        var (_, output, _) = Run((o, e) => InfoCommand.Run(Preset("Nebula"), "nebula.fbk", true, o, e));

        Should.NotThrow(() => JsonDocument.Parse(output));
    }

    // --- info ----------------------------------------------------------------

    /// <summary>
    /// The two halves are counted separately, which is the fact this command
    /// exists to show: Nebula is an elaborate picture and silence, so its sound
    /// program is a fraction of the size and says it has nothing wired in.
    /// </summary>
    [Fact]
    public void Each_half_is_costed_on_its_own()
    {
        var (code, output, _) = Run((o, e) => InfoCommand.Run(Preset("Nebula"), "nebula.fbk", false, o, e));

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("picture");
        output.ShouldContain("sound");
        output.ShouldContain("nothing wired in");
    }

    /// <summary>
    /// Said when there is some and left out when there is not, so that a row of
    /// zeroes never stands where a fact should be.
    /// </summary>
    /// <remarks>
    /// An oscillator emits a phase accumulator wherever it is compiled, the
    /// picture included — drawn rather than heard it falls back to a multiply,
    /// but the op is in the program either way. So the patch without one is the
    /// patch with no oscillator in it at all.
    /// </remarks>
    [Fact]
    public void State_is_mentioned_only_by_a_patch_that_has_some()
    {
        var oscillated = Run((o, e) => InfoCommand.Run(Preset("Plasma"), "plasma.fbk", false, o, e));
        var bare = Run((o, e) => InfoCommand.Run(Preset("Empty"), "empty.fbk", false, o, e));

        oscillated.Out.ShouldContain("phase accumulator");
        bare.Out.ShouldNotContain("phase accumulator");
    }

    /// <summary>One of anything is not "1 things".</summary>
    [Fact]
    public void Counts_are_written_in_the_number_they_are()
    {
        Writing.Count(1, "delay line").ShouldBe("1 delay line");
        Writing.Count(2, "delay line").ShouldBe("2 delay lines");
        Writing.Count(0, "error").ShouldBe("0 errors");
    }

    // --- render --------------------------------------------------------------

    [Theory]
    [InlineData(".png")]
    [InlineData(".wav")]
    [InlineData(".avi")]
    public void Every_kind_of_file_is_written_and_is_not_empty(string extension)
    {
        using var directory = new Scratch();
        var file = directory.File($"out{extension}");

        var (code, _, _) = Run((_, e) => RenderCommand.Run(
            Preset("Drone"),
            new RenderOptions(file, 64, 36, Seconds: 0.25d, Fps: 8d),
            e));

        code.ShouldBe(Exit.Ok);
        file.Refresh();
        file.Exists.ShouldBeTrue();
        file.Length.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// A file made of stand-ins looks exactly like a real one, so it is not
    /// written at all.
    /// </summary>
    [Fact]
    public void A_patch_with_errors_is_refused_rather_than_rendered()
    {
        using var directory = new Scratch();
        var file = directory.File("out.png");

        var (code, _, error) = Run((_, e) => RenderCommand.Run(
            Broken(), new RenderOptions(file, 64, 36), e));

        code.ShouldBe(Exit.Problems);
        error.ShouldContain("refusing");
        file.Refresh();
        file.Exists.ShouldBeFalse();
    }

    /// <summary>A warning is a patch somebody may have meant, so it is said and written anyway.</summary>
    [Fact]
    public void A_patch_that_only_warns_is_still_rendered()
    {
        using var directory = new Scratch();
        var file = directory.File("out.png");

        var (code, _, error) = Run((_, e) => RenderCommand.Run(
            Preset("Empty"), new RenderOptions(file, 64, 36), e));

        code.ShouldBe(Exit.Ok);
        error.ShouldContain("warning");
        file.Refresh();
        file.Exists.ShouldBeTrue();
    }

    [Fact]
    public void An_extension_nothing_can_be_written_to_is_said_plainly()
    {
        using var directory = new Scratch();

        var (code, _, error) = Run((_, e) => RenderCommand.Run(
            Preset("Plasma"), new RenderOptions(directory.File("out.mp4"), 64, 36), e));

        code.ShouldBe(Exit.Failed);
        error.ShouldContain(".png");
        error.ShouldContain(".wav");
        error.ShouldContain(".avi");
    }

    [Fact]
    public void A_path_that_cannot_be_written_is_a_failure_rather_than_a_throw()
    {
        var nowhere = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "out.png"));

        var (code, _, error) = Run((_, e) => RenderCommand.Run(
            Preset("Plasma"), new RenderOptions(nowhere, 64, 36), e));

        code.ShouldBe(Exit.Failed);
        error.ShouldNotBeEmpty();
    }

    /// <summary>
    /// The same patch at the same moment is the same bytes. The CLI renders on
    /// the interpreter for exactly this reason — see RenderCommand.
    /// </summary>
    [Fact]
    public void The_same_still_twice_is_the_same_file()
    {
        using var directory = new Scratch();
        var first = directory.File("first.png");
        var second = directory.File("second.png");

        foreach (var file in new[] { first, second })
        {
            RenderCommand.Run(
                    Preset("Nebula"),
                    new RenderOptions(file, 96, 54, At: 1.75d),
                    TextWriter.Null,
                    cancellation: TestContext.Current.CancellationToken)
                .ShouldBe(Exit.Ok);
        }

        File.ReadAllBytes(first.FullName).ShouldBe(File.ReadAllBytes(second.FullName));
    }

    // --- pack ----------------------------------------------------------------

    /// <summary>
    /// The whole case for a bundle, end to end: a patch and its picture packed
    /// into one file, and that file rendered somewhere the picture has never
    /// been. Nothing is unpacked on the way — a build server holding one file
    /// writes one frame.
    /// </summary>
    [Fact]
    public void A_bundle_renders_where_none_of_its_files_are()
    {
        using var made = new Scratch();
        using var elsewhere = new Scratch();

        var picture = made.File("moon.png");
        WritePicture(picture);

        var patch = Preset("Plasma");
        var shown = NodeInstance.Create(NodeCatalog.BuiltIn.Require(NodeCatalog.PictureTypeId), 0, 0);

        shown.Picture = "moon.png";
        patch.Nodes.Add(shown);
        patch.Connect(shown.Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);

        var file = made.File("shown.fbk");
        File.WriteAllText(file.FullName, PatchIo.ToJson(patch, NodeCatalog.BuiltIn));

        var bundle = elsewhere.File("shown.fbkp");

        var (code, output, _) = Run((o, e) => PackCommand.Run(file, bundle, e, o));

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("moon.png");
        bundle.Refresh();
        bundle.Exists.ShouldBeTrue();

        // Nothing beside it but itself, which is the case a loose patch fails.
        Directory.GetFiles(bundle.DirectoryName!).ShouldHaveSingleItem();

        var frame = elsewhere.File("out.png");

        Run((o, e) => RenderCommand.Run(
                Patches.Open(bundle, e)!.Value.Patch,
                new RenderOptions(frame, 32, 18, 0d, 1d, 30d, 80),
                e,
                null,
                default,
                Patches.Open(bundle, e)!.Value.Samples,
                Patches.Open(bundle, e)!.Value.Pictures))
            .Code.ShouldBe(Exit.Ok);

        frame.Refresh();
        frame.Exists.ShouldBeTrue();

        // The picture is a flat red, so the middle of the frame it drew is red —
        // which says the bytes went in, came out and were decoded, rather than
        // merely that a file appeared.
        //
        // The middle rather than a corner, because a square picture on a wide
        // frame is placed at its own shape and the corners are the black beside
        // it. That is the module's rule, not a shortcoming of the bundle.
        var drawn = PngReader.Read(frame.FullName, out _).ShouldNotBeNull();
        var centre = ((drawn.Height / 2) * drawn.Width + drawn.Width / 2) * 3;

        drawn.Pixels[centre].ShouldBe(1f, 0.02f);
        drawn.Pixels[centre + 1].ShouldBe(0f, 0.02f);

        // And a corner is the black beside it, which is the same rule said the
        // other way round.
        drawn.Pixels[0].ShouldBe(0f, 0.02f);
    }

    /// <summary>
    /// A file that has gone is said and the bundle is still written, which is the
    /// same call check makes: the patch opens, compiles and draws without it, so
    /// what is refused is the claim to be self-contained rather than the file.
    /// </summary>
    [Fact]
    public void A_bundle_missing_a_file_is_written_and_reported()
    {
        using var scratch = new Scratch();

        var patch = Preset("Plasma");
        var shown = NodeInstance.Create(NodeCatalog.BuiltIn.Require(NodeCatalog.PictureTypeId), 0, 0);

        shown.Picture = "gone.png";
        patch.Nodes.Add(shown);
        patch.Connect(shown.Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);

        var file = scratch.File("shown.fbk");
        File.WriteAllText(file.FullName, PatchIo.ToJson(patch, NodeCatalog.BuiltIn));

        var bundle = scratch.File("shown.fbkp");
        var (code, _, error) = Run((o, e) => PackCommand.Run(file, bundle, e, o));

        code.ShouldBe(Exit.Problems);
        error.ShouldContain("gone.png");

        bundle.Refresh();
        bundle.Exists.ShouldBeTrue();
    }

    /// <summary>A small red PNG, written the way the program writes every other one.</summary>
    private static void WritePicture(FileInfo file)
    {
        const int width = 4;
        const int height = 4;

        var stride = width * 4;
        var bgra = new byte[stride * height];

        for (var i = 0; i < width * height; i++)
        {
            bgra[i * 4 + 2] = 255;
            bgra[i * 4 + 3] = 255;
        }

        PngWriter.WriteBgra(file.FullName, bgra, width, height, stride);
    }

    /// <summary>A directory of its own per test, taken away afterwards.</summary>
    private sealed class Scratch : IDisposable
    {
        private readonly string path = Directory.CreateTempSubdirectory("flyback-cli").FullName;

        public FileInfo File(string name) => new(Path.Combine(path, name));

        public void Dispose() => Directory.Delete(path, recursive: true);
    }
}
