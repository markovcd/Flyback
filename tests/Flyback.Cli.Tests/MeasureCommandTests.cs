using System.CommandLine;
using Flyback.Cli.Common;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Tests;

/// <summary><c>measure</c> runs a patch for a few seconds and says what its outputs carried.</summary>
public class MeasureCommandTests
{
    [Theory]
    [InlineData(new[] { "measure", "--preset", "plasma", "Time.t", "--seconds", "0.5" }, Exit.Ok, "Time.t")]
    [InlineData(new[] { "measure", "--preset", "plasma", "Time.length", "--seconds", "0.5" }, Exit.Ok, "no change in 0.5 s")]
    [InlineData(new[] { "measure", "--preset", "plasma", "Coordinates.x", "--seconds", "0.5" }, Exit.Ok, "varies across the picture")]
    [InlineData(new[] { "measure", "--preset", "plasma", "Time", "--seconds", "0.5", "--json" }, Exit.Ok, "\"socket\": \"progress\"")]
    [InlineData(new[] { "measure", "--preset", "plasma", "Nonesuch" }, Exit.Failed, "No module here is called 'Nonesuch'")]
    [InlineData(new[] { "measure", "--preset", "plasma", "Time.nonesuch" }, Exit.Failed, "'Time' has no output called 'nonesuch'")]
    [InlineData(new[] { "measure", "--preset", "plasma", "--seconds", "0" }, Exit.Failed, "--seconds runs above 0")]
    [InlineData(new[] { "measure" }, Exit.Failed, "say what to measure")]
    public void Measure_says_what_an_output_carried(string[] args, int exit, string said)
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
    public void Naming_one_output_reports_that_output_alone()
    {
        var output = new StringWriter();

        Program.Run(
            ["measure", "--preset", "plasma", "Time.t", "--seconds", "0.25"],
            new PluginRegistry(() => PluginCatalog.Empty, "nowhere", null),
            new InvocationConfiguration { Output = output, Error = TextWriter.Null });

        output.ToString().ShouldNotContain("Time.length");
    }
}
