using System.CommandLine;
using Flyback.Cli.Common;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>
/// <c>flyback-cli shot</c> up to the hand-over: the request is checked here, so a bad one is
/// refused before the editor beside it is looked for, and a good one reaches the looking.
/// </summary>
public sealed class ShotCommandTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-cli-shot");

    public void Dispose() => folder.Delete(recursive: true);

    private string At(string name) => System.IO.Path.Combine(folder.FullName, name);

    /// <summary>The editor it is told to use, which is nowhere, so a request that gets that far says so.</summary>
    private string Nowhere => At("no-editor");

    private (int Code, string Said) Run(params string[] args)
    {
        var (output, error) = (new StringWriter(), new StringWriter());

        var code = Program.Run(
            ["shot", .. args, "--editor", Nowhere],
            new PluginRegistry(() => PluginCatalog.Empty, "nowhere", null),
            new InvocationConfiguration { Output = output, Error = error });

        return (code, output + Environment.NewLine + error);
    }

    [Theory]
    [InlineData(new string[] { "--out", "{0}a.png" }, "give a patch or --preset, and not both.")]
    [InlineData(new string[] { "{0}tone.fbk", "--preset", "Plasma", "--out", "{0}a.png" }, "give a patch or --preset, and not both.")]
    [InlineData(new string[] { "--preset", "Plasma", "--out", "{0}a.jpg" }, "a shot is a PNG.")]
    public void A_request_that_does_not_make_sense_is_refused_before_the_editor_is_looked_for(string[] args, string why)
    {
        var (code, said) = Run([.. args.Select(arg => arg.Replace("{0}", folder.FullName + System.IO.Path.DirectorySeparatorChar))]);

        code.ShouldBe(Exit.Failed, said);
        said.ShouldContain(why);
        said.ShouldNotContain("is not here");
    }

    [Fact]
    public void A_preset_nobody_shipped_is_refused_with_the_names_of_the_ones_there_are()
    {
        var (code, said) = Run("--preset", "Nonesuch", "--out", At("a.png"));

        code.ShouldBe(Exit.Failed, said);
        said.ShouldContain("no preset is called 'Nonesuch'");
        said.ShouldContain("    Whole band");
        said.ShouldNotContain("is not here");
    }

    [Fact]
    public void A_patch_that_does_not_open_stops_it_with_the_problems()
    {
        File.WriteAllText(At("broken.fbk"), "this is not a patch");

        var (code, said) = Run(At("broken.fbk"), "--out", At("a.png"));

        code.ShouldBe(Exit.Problems, said);
        said.ShouldNotContain("is not here");
    }

    [Fact]
    public void A_good_request_reaches_the_editor_and_says_where_it_looked()
    {
        var (code, said) = Run("--preset", "Plasma", "--out", At("a.png"));

        code.ShouldBe(Exit.Failed, said);
        said.ShouldContain($"the editor is not here — looked for {Nowhere}");
        File.Exists(At("a.png")).ShouldBeFalse();
    }
}
