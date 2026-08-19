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

    /// <summary>A directory of its own per test, taken away afterwards.</summary>
    private sealed class Scratch : IDisposable
    {
        private readonly string path = Directory.CreateTempSubdirectory("flyback-cli").FullName;

        public FileInfo File(string name) => new(Path.Combine(path, name));

        public void Dispose() => Directory.Delete(path, recursive: true);
    }
}
