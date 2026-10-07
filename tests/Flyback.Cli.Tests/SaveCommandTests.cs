using System.CommandLine;
using Flyback.Core.Graph;
using Flyback.Cli.Common;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary><c>flyback-cli save</c>: a patch or a shipped preset written as the file its extension names.</summary>
public sealed class SaveCommandTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-cli-save");

    public void Dispose() => folder.Delete(recursive: true);

    private string File(string name) => Path.Combine(folder.FullName, name);

    private static (int Code, string Said) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = Program.Run(
            args,
            new PluginRegistry(() => PluginCatalog.Empty, "nowhere", null),
            new InvocationConfiguration { Output = output, Error = error });

        return (code, output + Environment.NewLine + error);
    }

    /// <summary>Whatever it was saved as, it opens as the preset it was saved from, and compiles to the same sound.</summary>
    [Theory]
    [InlineData("plasma.fbk")]
    [InlineData("plasma.fbks")]
    [InlineData("plasma.fbkb")]
    public void A_preset_saved_as_any_format_opens_as_the_same_instrument(string name)
    {
        var (code, said) = Run("save", "--preset", "Plasma", "--out", File(name));

        code.ShouldBe(Exit.Ok, said);

        var opened = PatchFile.Open(new FileInfo(File(name)));
        opened.Problems.ShouldBeEmpty();

        var preset = Presets.All.Single(p => p.Name == "Plasma").Build(NodeCatalog.BuiltIn);

        opened.Patch.ShouldNotBeNull().Patch.CompileForAudio().Program.Ops
            .ShouldBe(preset.CompileForAudio().Program.Ops);
    }

    [Fact]
    public void A_patch_file_is_saved_as_another_format()
    {
        Run("save", "--preset", "Plasma", "--out", File("plasma.fbk")).Code.ShouldBe(Exit.Ok);

        var (code, said) = Run("save", File("plasma.fbk"), "--out", File("plasma.fbks"));

        code.ShouldBe(Exit.Ok, said);
        PatchFile.Open(new FileInfo(File("plasma.fbks"))).Patch.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(new[] { "save", "--preset", "Plasma", "--out", "{0}plasma.wav" }, "the extension says what to write, .fbk, .fbks or .fbkb")]
    [InlineData(new[] { "save", "--preset", "Plasma" }, "--out says where to save it")]
    [InlineData(new[] { "save", "--out", "{0}plasma.fbk" }, "say what to save")]
    [InlineData(new[] { "save", "--preset", "Nonesuch", "--out", "{0}plasma.fbk" }, "    Whole band")]
    public void What_it_cannot_save_is_refused_saying_why(string[] args, string why)
    {
        var (code, said) = Run([.. args.Select(arg => arg.Replace("{0}", folder.FullName + Path.DirectorySeparatorChar))]);

        code.ShouldBe(Exit.Failed, said);
        said.ShouldContain(why);
        folder.GetFiles().ShouldBeEmpty();
    }

    [Fact]
    public void A_patch_is_never_saved_over_the_file_it_was_read_from()
    {
        Run("save", "--preset", "Plasma", "--out", File("plasma.fbk")).Code.ShouldBe(Exit.Ok);
        var before = System.IO.File.ReadAllText(File("plasma.fbk"));

        var (code, said) = Run("save", File("plasma.fbk"), "--out", File("plasma.fbk"));

        code.ShouldBe(Exit.Failed, said);
        System.IO.File.ReadAllText(File("plasma.fbk")).ShouldBe(before);
    }

    [Fact]
    public void The_presets_it_takes_are_listed()
    {
        var (code, said) = Run("save", "--presets");

        code.ShouldBe(Exit.Ok);
        said.ShouldContain("Whole band");
    }
}
