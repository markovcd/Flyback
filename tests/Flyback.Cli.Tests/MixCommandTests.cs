using System.CommandLine;
using Flyback.Cli.Common;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Tests;

/// <summary><c>mix</c> listens to a patch for a few seconds and flags the usual mix problems, exiting 1 on a warning.</summary>
public class MixCommandTests
{
    [Theory]
    [InlineData(new[] { "mix", "--preset", "plasma", "--seconds", "0.5" }, Exit.Ok, "no sound to check")]
    [InlineData(new[] { "mix", "--preset", "drone", "--seconds", "1" }, Exit.Ok, "notice no-master-chain")]
    [InlineData(new[] { "mix", "--preset", "drone", "--seconds", "121" }, Exit.Failed, "--seconds runs above 0 and up to 120")]
    [InlineData(new[] { "mix", "--preset", "drone", "--seconds", "1", "--json" }, Exit.Ok, "\"code\": \"no-master-chain\"")]
    [InlineData(new[] { "mix", "--preset", "drone", "--seconds", "0" }, Exit.Failed, "--seconds runs above 0")]
    [InlineData(new[] { "mix", "--preset", "drone", "--from", "-1" }, Exit.Failed, "--from is 0 or later")]
    [InlineData(new[] { "mix" }, Exit.Failed, "say what to check the mix of")]
    public void Mix_flags_the_usual_problems(string[] args, int exit, string said)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = Program.Run(
            args,
            new PluginRegistry(() => PluginCatalog.Empty, "nowhere", null),
            new InvocationConfiguration { Output = output, Error = error });

        code.ShouldBe(exit, error.ToString());
        (output.ToString() + error).ShouldContain(said);
    }

    [Fact]
    public void A_warning_exits_1()
    {
        var patch = Path.Combine(Path.GetTempPath(), $"mix-{Guid.NewGuid():N}.fbks");
        File.WriteAllText(patch, "sine(freq: 110) * 3 |> out.left\nout.volume = 1");

        try
        {
            var output = new StringWriter();

            var code = Program.Run(
                ["mix", patch, "--seconds", "1"],
                new PluginRegistry(() => PluginCatalog.Empty, "nowhere", null),
                new InvocationConfiguration { Output = output, Error = TextWriter.Null });

            code.ShouldBe(Exit.Problems);
            output.ToString().ShouldContain("warning clipping");
        }
        finally
        {
            File.Delete(patch);
        }
    }
}
