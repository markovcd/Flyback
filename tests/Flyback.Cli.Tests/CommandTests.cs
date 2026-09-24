using System.CommandLine;
using Flyback.Core.Render;
using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// The commands, called the way the argument parser calls them.
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

    /// <summary>
    /// What a build asks for when a warning is not something it means to carry.
    /// The same patch and the same report either way — only the number the shell
    /// gets changes.
    /// </summary>
    [Fact]
    public void Strict_fails_on_a_warning_and_plain_does_not()
    {
        var plain = Run((o, e) => CheckCommand.Run(Preset("Empty"), "empty.fbk", false, o, e));
        var strict = Run((o, e) => CheckCommand.Run(Preset("Empty"), "empty.fbk", false, o, e, strict: true));

        plain.Code.ShouldBe(Exit.Ok);
        strict.Code.ShouldBe(Exit.Problems);
        strict.Out.ShouldBe(plain.Out);
    }

    /// <summary>An error is an error whether or not anybody asked for strictness.</summary>
    [Fact]
    public void Strict_changes_nothing_about_a_patch_that_already_fails()
    {
        Run((o, e) => CheckCommand.Run(Broken(), "broken.fbk", false, o, e, strict: true))
            .Code.ShouldBe(Exit.Problems);
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

    /// <summary>
    /// Text that does not build is a patch with something wrong with it, said
    /// where in the text, and in JSON when JSON was asked for.
    /// </summary>
    [Fact]
    public void Text_that_does_not_build_is_reported_by_line_as_json()
    {
        using var directory = new Scratch();
        var file = directory.File("twice.fbks");

        File.WriteAllText(file.FullName, "x |> color.hsv(hue: _) |> out.color\ny |> color.hsv(hue: _) |> out.color\n");

        var output = new StringWriter();
        var code = Program.Run(
            ["check", file.FullName, "--json"],
            new Plugins(() => PluginCatalog.Empty, "nowhere", null),
            new InvocationConfiguration { Output = output, Error = TextWriter.Null });

        code.ShouldBe(Exit.Problems);

        using var read = JsonDocument.Parse(output.ToString());
        var issue = read.RootElement.GetProperty("issues").EnumerateArray().ShouldHaveSingleItem();

        issue.GetProperty("severity").GetString().ShouldBe("error");
        issue.GetProperty("code").GetString().ShouldBe("wired-twice");
        issue.GetProperty("line").GetInt32().ShouldBe(2);
        issue.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("already wired on line 1");
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

    [Theory]
    [InlineData(".wav")]
    [InlineData(".avi")]
    public void Asked_for_its_loudness_a_render_says_how_loud_it_came_out(string extension)
    {
        using var directory = new Scratch();

        var (code, output, _) = Run((o, e) => RenderCommand.Run(
            Preset("Drone"),
            new RenderOptions(directory.File($"out{extension}"), 64, 36, Seconds: 1d, Fps: 8d, Loudness: true),
            e,
            output: o));

        code.ShouldBe(Exit.Ok);
        output.ShouldMatch(@"loudness: -?\d+\.\d LUFS integrated, -?\d+\.\d dBTP true peak");
    }

    [Fact]
    public void A_still_has_no_loudness_to_report()
    {
        using var directory = new Scratch();

        var (_, output, _) = Run((o, e) => RenderCommand.Run(
            Preset("Drone"), new RenderOptions(directory.File("out.png"), 64, 36, Loudness: true), e, output: o));

        output.ShouldContain("no sound to measure");
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
            Preset("Plasma"), new RenderOptions(directory.File("out.gif"), 64, 36), e));

        code.ShouldBe(Exit.Failed);
        error.ShouldContain(".png");
        error.ShouldContain(".wav");
        error.ShouldContain(".avi");
        error.ShouldContain(".mp4");
    }

    /// <summary>A --format nothing defines is refused, not quietly read as the extension's.</summary>
    [Fact]
    public void A_format_nothing_defines_is_said_plainly()
    {
        using var directory = new Scratch();

        var file = directory.File("out.mp4");

        var (code, _, error) = Run((_, e) => RenderCommand.Run(
            Preset("Plasma"), new RenderOptions(file, 64, 36, Seconds: 0.2d, Format: "h265"), e));

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("h265");
        error.ShouldContain(ClipFormats.H265Mp4.Id);

        file.Refresh();
        file.Exists.ShouldBeFalse();
    }

    /// <summary>
    /// The formats written here have one container each. ffmpeg's are left to
    /// ffmpeg, which reads the container off the name.
    /// </summary>
    [Fact]
    public void A_format_written_here_is_refused_under_another_extension()
    {
        using var directory = new Scratch();

        var file = directory.File("out.mp4");

        var (code, _, error) = Run((_, e) => RenderCommand.Run(
            Preset("Plasma"), new RenderOptions(file, 64, 36, Seconds: 0.2d, Format: ClipFormats.MotionJpegAvi.Id), e));

        code.ShouldBe(Exit.Failed);
        error.ShouldContain(ClipFormats.MotionJpegAvi.Extension);

        file.Refresh();
        file.Exists.ShouldBeFalse();
    }

    /// <summary>
    /// A format ffmpeg writes, asked for on a machine with no ffmpeg. Said before
    /// anything is rendered, and said with what to do about it — ADR-0089.
    /// </summary>
    [Fact]
    public void A_format_needing_ffmpeg_says_so_when_there_is_none()
    {
        using var directory = new Scratch();

        var file = directory.File("out.mp4");

        var (code, _, error) = Run((_, e) => RenderCommand.Run(
            Preset("Plasma"),
            new RenderOptions(file, 64, 36, Seconds: 0.2d, Ffmpeg: Missing),
            e));

        // Only where this machine has no ffmpeg of its own to fall back on, since
        // a path that is not there falls back to PATH by design.
        if (Ffmpeg.OnPath() is not null) return;

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("ffmpeg");
        error.ShouldContain("--ffmpeg");

        file.Refresh();
        file.Exists.ShouldBeFalse();
    }

    /// <summary>A path no ffmpeg is at, which is what pointing --ffmpeg at nothing looks like.</summary>
    private static string Missing => Path.Combine(Path.GetTempPath(), $"no-ffmpeg-{Guid.NewGuid():N}");

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

    /// <summary>A render runs compiled, and the interpreter is the reference it has to match.</summary>
    [Theory]
    [InlineData("Nebula", ".png")]
    [InlineData("Drone", ".wav")]
    [InlineData("Drone", ".avi")]
    public void A_compiled_render_is_the_same_file_as_an_interpreted_one(string preset, string extension)
    {
        using var directory = new Scratch();
        var compiled = directory.File($"compiled{extension}");
        var interpreted = directory.File($"interpreted{extension}");

        foreach (var (file, off) in new[] { (compiled, false), (interpreted, true) })
        {
            var (code, _, error) = Run((_, e) => RenderCommand.Run(
                Preset(preset),
                new RenderOptions(file, 96, 54, At: 1.75d, Seconds: 0.5d, Fps: 8d, Interpreted: off),
                e,
                cancellation: TestContext.Current.CancellationToken));

            code.ShouldBe(Exit.Ok);
            error.ShouldNotContain("interpreted");
        }

        File.ReadAllBytes(compiled.FullName).ShouldBe(File.ReadAllBytes(interpreted.FullName));
    }

    // --- print ---------------------------------------------------------------

    /// <summary>
    /// The claim the language rests on, made about one patch rather than about
    /// the presets — which is the whole of what the verb adds to a build.
    /// </summary>
    [Fact]
    public void A_printing_reads_back_as_the_same_instrument()
    {
        var (code, output, _) = Run((o, e) => PrintCommand.Run(
            Preset("Whole band"), new FileInfo("band.fbk"), null, check: true, o, e));

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("the same instrument");
    }

    /// <summary>
    /// A module with no definition has no socket names to write a call from, so
    /// the printer leaves it out — and a printing quietly short of a module is
    /// the one thing this must not hand back as though it were whole. Said, and
    /// written anyway, exactly as pack writes a bundle short of a file.
    /// </summary>
    [Fact]
    public void A_module_nothing_defines_is_reported_and_the_printing_says_so()
    {
        var (code, output, error) = Run((o, e) => PrintCommand.Run(
            Broken(), new FileInfo("broken.fbk"), null, check: false, o, e));

        code.ShouldBe(Exit.Problems);
        error.ShouldContain("osc.nonesuch");
        output.ShouldContain("out.color");
    }

    /// <summary>Standard output where no file was named, so a printing can be piped.</summary>
    [Fact]
    public void With_no_file_named_the_patch_goes_to_standard_output()
    {
        var (code, output, error) = Run((o, e) => PrintCommand.Run(
            Preset("Plasma"), new FileInfo("plasma.fbk"), null, check: false, o, e));

        code.ShouldBe(Exit.Ok);
        error.ShouldBeEmpty();
        output.ShouldContain("out.color");
    }

    /// <summary>
    /// And nothing else in it. A printing that is piped somewhere must be a
    /// patch and not a patch with a sentence about itself on top.
    /// </summary>
    [Fact]
    public void What_is_written_to_a_file_is_what_would_have_been_piped()
    {
        using var directory = new Scratch();
        var file = directory.File("plasma.fbks");

        var piped = Run((o, e) => PrintCommand.Run(
            Preset("Plasma"), new FileInfo("plasma.fbk"), null, check: false, o, e));

        var written = Run((o, e) => PrintCommand.Run(
            Preset("Plasma"), new FileInfo("plasma.fbk"), file, check: false, o, e));

        written.Code.ShouldBe(Exit.Ok);
        written.Out.ShouldBeEmpty();
        File.ReadAllText(file.FullName).ShouldBe(piped.Out);
    }

    /// <summary>
    /// Comments and groups do not survive a printing, so the one path this must
    /// not write to is the one it was read from.
    /// </summary>
    [Fact]
    public void The_file_being_read_is_not_written_over()
    {
        using var directory = new Scratch();
        var file = directory.File("plasma.fbks");

        File.WriteAllText(file.FullName, "# mine\nx |> out.color\n");

        var (code, _, error) = Run((o, e) => PrintCommand.Run(
            Preset("Plasma"), file, file, check: false, o, e));

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("the file being read");
        File.ReadAllText(file.FullName).ShouldContain("# mine");
    }

    // --- compare -------------------------------------------------------------

    /// <summary>A picture and a sound, so either half can be turned on its own.</summary>
    private const string Hello = """
        let slowly = t * 0.2
        let signal = y |> sine(freq: 1.1, phase: slowly)
        x |> sine(freq: 0.9, phase: slowly)
          |> add(a: _, signal)
          |> remap(-2..2, 0..1)
          |> hsv(hue: _, saturation: 0.85, value: 1)
          |> out.color
        t |> sine(freq: 220) |> out.left
        """;

    private static Opened Written(string source)
    {
        var built = Core.Language.PatchLanguage.Build(source);
        built.Ok.ShouldBeTrue(built.Report);

        return new Opened(built.Patch, new SampleLibrary(), new ImageLibrary());
    }

    private static (int Code, string Out, string Error) Compare(Opened was, Opened now, bool json = false) =>
        Run((o, e) => CompareCommand.Run(was, "was.fbks", now, "now.fbks", new CompareOptions(0.5, 64, 36, Json: json), o, e));

    /// <summary>
    /// A patch and its printing read back, which share no ids and not always a
    /// register, and still play the same samples and draw the same pixels.
    /// </summary>
    [Fact]
    public void A_patch_and_its_printing_are_the_same_instrument()
    {
        var patch = Preset("Whole band");
        var printed = Core.Language.PatchLanguage.Build(Core.Language.PatchPrinter.Print(patch)).Patch;

        var (code, output, _) = Compare(
            new Opened(patch, new SampleLibrary(), new ImageLibrary()),
            new Opened(printed, new SampleLibrary(), new ImageLibrary()));

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("are the same instrument");
    }

    /// <summary>Only the half that was turned parts, and a script can read which.</summary>
    [Fact]
    public void A_turned_pitch_parts_the_sound_and_leaves_the_picture()
    {
        var (code, output, _) = Compare(
            Written(Hello), Written(Hello.Replace("freq: 220", "freq: 221", StringComparison.Ordinal)), json: true);

        code.ShouldBe(Exit.Problems);

        using var document = JsonDocument.Parse(output);
        document.RootElement.GetProperty("same").GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("sound").GetProperty("channel").GetString().ShouldBe("left");
        document.RootElement.TryGetProperty("picture", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_turned_color_parts_the_picture_and_leaves_the_sound()
    {
        var (code, output, _) = Compare(
            Written(Hello), Written(Hello.Replace("saturation: 0.85", "saturation: 0.8", StringComparison.Ordinal)));

        code.ShouldBe(Exit.Problems);
        output.ShouldContain("picture  parts at 0.000 s in frame 0");
        output.ShouldNotContain("sound    parts");
    }

    /// <summary>A stand-in plays, so two broken patches could agree about nothing real.</summary>
    [Fact]
    public void A_patch_with_errors_is_not_compared()
    {
        var broken = new Opened(Broken(), new SampleLibrary(), new ImageLibrary());

        var (code, output, error) = Compare(broken, broken);

        code.ShouldBe(Exit.Problems);
        output.ShouldBeEmpty();
        error.ShouldContain("refusing to compare");
    }

    // --- modules -------------------------------------------------------------

    /// <summary>
    /// The question a patch short of a plugin raises and nothing else answers.
    /// Every module comes out under whoever defines it, engine or plugin.
    /// </summary>
    [Fact]
    public void Every_module_is_listed_under_the_provider_that_defines_it()
    {
        var thing = new NodeDef("test.thing", "Thing", ModuleCategories.Sources, [], [], (_, _) => []);
        var catalog = NodeCatalog.BuiltIn.With(new ModuleProvider("test", "Test"), [thing]).Catalog;

        var (code, output, _) = Run((o, _) => ModulesCommand.Run(catalog, true, o));

        code.ShouldBe(Exit.Ok);

        using var document = JsonDocument.Parse(output);
        var modules = document.RootElement.GetProperty("modules").EnumerateArray().ToArray();

        Provider("test.thing").ShouldBe("test");
        Provider(NodeCatalog.OutputTypeId).ShouldBe(NodeCatalog.BuiltInProvider.Id);

        document.RootElement.GetProperty("providers").EnumerateArray()
            .Select(provider => provider.GetProperty("id").GetString())
            .ShouldContain("test");

        string? Provider(string typeId) => modules
            .Single(module => module.GetProperty("typeId").GetString() == typeId)
            .GetProperty("provider")
            .GetString();
    }

    /// <summary>A socket's name is what somebody is looking for, so the document carries it.</summary>
    [Fact]
    public void A_module_brings_its_socket_names_with_it()
    {
        var (_, output, _) = Run((o, _) => ModulesCommand.Run(NodeCatalog.BuiltIn, true, o));

        using var document = JsonDocument.Parse(output);

        document.RootElement.GetProperty("modules").EnumerateArray()
            .Single(module => module.GetProperty("typeId").GetString() == NodeCatalog.OutputTypeId)
            .GetProperty("inputs")
            .EnumerateArray()
            .Select(port => port.GetString())
            .ShouldContain("color");
    }

    /// <summary>Whatever the document lists, the prose lists — it is one catalog.</summary>
    [Fact]
    public void The_prose_and_the_json_say_the_same_modules()
    {
        var prose = Run((o, _) => ModulesCommand.Run(NodeCatalog.BuiltIn, false, o));
        var json = Run((o, _) => ModulesCommand.Run(NodeCatalog.BuiltIn, true, o));

        using var document = JsonDocument.Parse(json.Out);

        var listed = document.RootElement.GetProperty("modules").EnumerateArray()
            .Select(module => module.GetProperty("typeId").GetString()!)
            .ToArray();

        listed.Length.ShouldBe(NodeCatalog.BuiltIn.All.Count);

        foreach (var typeId in listed) prose.Out.ShouldContain(typeId);
    }

    /// <summary>
    /// What somebody writing a patch by hand needs and the list does not carry:
    /// where each socket sits unwired, how far it turns, and in the unit its
    /// knob is read in.
    /// </summary>
    [Fact]
    public void One_module_is_described_socket_by_socket()
    {
        var (code, output, _) = Run((o, e) => ModulesCommand.Describe(NodeCatalog.BuiltIn, "env.adsr", false, o, e));

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("ADSR  env.adsr");
        output.ShouldContain("sound only");
        output.ShouldContain(NodeCatalog.BuiltIn.Require("env.adsr").Description);
        output.ShouldMatch(@"attack\s+10 ms\s+100 µs to 31\.62 s");
    }

    /// <summary>The palette's name finds a module as well as its type id, whichever case it is typed in.</summary>
    [Fact]
    public void A_module_is_found_by_its_name_as_well_as_its_type_id()
    {
        var byName = Run((o, e) => ModulesCommand.Describe(NodeCatalog.BuiltIn, "sine", true, o, e));
        var byId = Run((o, e) => ModulesCommand.Describe(NodeCatalog.BuiltIn, "osc.sine", true, o, e));

        byName.Code.ShouldBe(Exit.Ok);
        byName.Out.ShouldBe(byId.Out);
    }

    /// <summary>A module this build lacks fails the shell and names the ones it might have meant.</summary>
    [Fact]
    public void A_module_nothing_defines_fails_and_offers_the_nearest()
    {
        var (code, output, error) = Run((o, e) => ModulesCommand.Describe(NodeCatalog.BuiltIn, "osc.sin", false, o, e));

        code.ShouldBe(Exit.Failed);
        output.ShouldBeEmpty();
        error.ShouldContain("osc.sine");
    }

    /// <summary>What a module carries besides its sockets is half of a Sequencer, so it is said.</summary>
    [Fact]
    public void A_module_says_what_it_carries_besides_its_sockets()
    {
        var (_, output, _) = Run((o, e) => ModulesCommand.Describe(NodeCatalog.BuiltIn, "seq.values", true, o, e));

        using var document = JsonDocument.Parse(output);

        document.RootElement.GetProperty("carries").EnumerateArray()
            .Select(extra => extra.GetProperty("key").GetString())
            .ShouldContain("notes");
    }

    /// <summary>
    /// The sockets the description marks as piped are the ones the language wires
    /// a bare <c>|&gt;</c> into, for every module, and a module with none marked
    /// refuses a bare pipe rather than guessing.
    /// </summary>
    [Fact]
    public void The_piped_socket_is_where_the_language_lands_a_pipe()
    {
        var wrong = new List<string>();

        foreach (var def in NodeCatalog.BuiltIn.All.Where(def => def.Inputs.Count > 0 && def.TypeId != NodeCatalog.OutputTypeId))
        {
            var (_, output, _) = Run((o, e) => ModulesCommand.Describe(NodeCatalog.BuiltIn, def.TypeId, true, o, e));

            using var document = JsonDocument.Parse(output);
            var marked = document.RootElement.GetProperty("piped").EnumerateArray().Select(p => p.GetString()).ToArray();

            // A position, so a module that takes one has something to take.
            var load = Core.Language.PatchLanguage.Build(
                $"let source = space.rotate()\nlet stage = source |> {def.TypeId}()",
                NodeCatalog.BuiltIn);

            if (marked.Length == 0)
            {
                if (!load.Report.Contains("say where the pipe lands")) wrong.Add($"{def.TypeId}: marked nothing, and was not refused");
                continue;
            }

            var source = load.Patch.Nodes.First(node => node.TypeId == "space.rotate").Id;

            // A small Maths module is folded into an Expression, so there is no node left to land on.
            if (load.Patch.Nodes.LastOrDefault(node => node.TypeId == def.TypeId)?.Id is not { } stage) continue;

            var landed = load.Patch.Connections
                .Where(wire => wire.SourceNode == source && wire.TargetNode == stage)
                .OrderBy(wire => wire.TargetPort)
                .Select(wire => def.Inputs[wire.TargetPort].Name)
                .ToArray();

            if (!landed.SequenceEqual(marked))
                wrong.Add($"{def.TypeId}: marked {string.Join(",", marked)}, landed {string.Join(",", landed)} {load.Report}");
        }

        wrong.ShouldBeEmpty();
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

        PictureExtra.Set(shown, "moon.png");
        patch.Nodes.Add(shown);
        patch.Connect(shown.Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);

        var file = made.File("shown.fbk");
        File.WriteAllText(file.FullName, PatchIO.ToJson(patch, NodeCatalog.BuiltIn));

        var bundle = elsewhere.File("shown.fbkb");

        var (code, output, _) = Run((o, e) => PackCommand.Run(file, bundle, e, o));

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("moon.png");
        bundle.Refresh();
        bundle.Exists.ShouldBeTrue();

        // Nothing beside it but itself, which is the case a loose patch fails.
        Directory.GetFiles(bundle.DirectoryName!).ShouldHaveSingleItem();

        var frame = elsewhere.File("out.png");

        Run((_, e) => RenderCommand.Run(
                Patches.Open(bundle, e)!.Value.Patch,
                new RenderOptions(frame, 32, 18, 0d, 1d, 30d, 80),
                e,
                null,
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
        var center = (drawn.Height / 2 * drawn.Width + drawn.Width / 2) * 3;

        drawn.Pixels[center].ShouldBe(1f, 0.02f);
        drawn.Pixels[center + 1].ShouldBe(0f, 0.02f);

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

        PictureExtra.Set(shown, "gone.png");
        patch.Nodes.Add(shown);
        patch.Connect(shown.Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);

        var file = scratch.File("shown.fbk");
        File.WriteAllText(file.FullName, PatchIO.ToJson(patch, NodeCatalog.BuiltIn));

        var bundle = scratch.File("shown.fbkb");
        var (code, _, error) = Run((o, e) => PackCommand.Run(file, bundle, e, o));

        code.ShouldBe(Exit.Problems);
        error.ShouldContain("gone.png");

        bundle.Refresh();
        bundle.Exists.ShouldBeTrue();
    }

    /// <summary>
    /// The two output modes agree about what was carried and what was not, the
    /// way check's two do about how bad a patch is.
    /// </summary>
    [Fact]
    public void The_json_and_the_prose_agree_about_what_a_bundle_holds()
    {
        using var scratch = new Scratch();

        var picture = scratch.File("moon.png");
        WritePicture(picture);

        var patch = Preset("Plasma");
        var shown = NodeInstance.Create(NodeCatalog.BuiltIn.Require(NodeCatalog.PictureTypeId), 0, 0);

        PictureExtra.Set(shown, "moon.png");
        patch.Nodes.Add(shown);
        patch.Connect(shown.Id, 0, patch.Output.Id, NodeCatalog.OutputColorPort);

        var file = scratch.File("shown.fbk");
        File.WriteAllText(file.FullName, PatchIO.ToJson(patch, NodeCatalog.BuiltIn));

        var prose = Run((o, e) => PackCommand.Run(file, scratch.File("prose.fbkb"), e, o));
        var json = Run((o, e) => PackCommand.Run(file, scratch.File("json.fbkb"), e, o, json: true));

        json.Code.ShouldBe(prose.Code);

        using var document = JsonDocument.Parse(json.Out);

        document.RootElement.GetProperty("whole").GetBoolean().ShouldBeTrue();
        document.RootElement.GetProperty("carried").EnumerateArray()
            .Select(carried => carried.GetString())
            .ShouldContain(carried => carried!.Contains("moon.png"));
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

    // --- --size ---------------------------------------------------------------

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("640X360", 640, 360)]
    [InlineData("8192x8192", 8192, 8192)]
    public void A_size_it_can_draw_is_read(string text, int width, int height) =>
        FrameSize.Of(text).ShouldBe((width, height));

    /// <summary>
    /// A frame is asked for as a number somebody typed, and every buffer behind
    /// it is that number times four. 27000x27000 overflows the multiplication, so
    /// the refusal is a sentence rather than an arithmetic exception with the
    /// output file's name in front of it.
    /// </summary>
    [Theory]
    [InlineData("0x0")]
    [InlineData("-4x-4")]
    [InlineData("1920")]
    [InlineData("wide x tall")]
    [InlineData("27000x27000")]
    [InlineData("100000x100000")]
    public void A_size_it_cannot_draw_is_refused(string text) => FrameSize.Of(text).ShouldBeNull();

    [Fact]
    public void A_frame_too_large_is_refused_for_being_too_large()
    {
        FrameSize.Refuse("27000x27000").ShouldContain("729,000,000 pixels");
        FrameSize.Refuse("nonsense").ShouldContain("WIDTHxHEIGHT");
    }

    /// <summary>A directory of its own per test, taken away afterwards.</summary>
    private sealed class Scratch : IDisposable
    {
        private readonly string path = Directory.CreateTempSubdirectory("flyback-cli").FullName;

        public FileInfo File(string name) => new(Path.Combine(path, name));

        public void Dispose() => Directory.Delete(path, recursive: true);
    }
}
