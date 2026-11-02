using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary><c>flyback-cli hover</c>: what the word at one place in a text patch is, and the call it is in.</summary>
public sealed class HoverCommandTests : IDisposable
{
    private const string Patch =
        """
        let lfo = sine(freq: 0.5)
        lfo |> drive(drive: 2, mix: 1) |> out.left
        """;

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-cli-hover");

    public void Dispose() => folder.Delete(recursive: true);

    private string Written(string text)
    {
        var path = Path.Combine(folder.FullName, "song.fbks");
        File.WriteAllText(path, text);
        return path;
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = Program.Run(
            args,
            new PluginRegistry(() => PluginCatalog.Empty, "nowhere", null),
            new InvocationConfiguration { Output = output, Error = error });

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void A_socket_in_a_call_is_said_with_the_calls_sockets_marked()
    {
        var (code, output, error) = Run("hover", Written(Patch), "--at", "2:16");

        code.ShouldBe(Exit.Ok, error);
        output.ShouldStartWith("drive · input of Drive");
        output.ShouldContain("in drive(…), Drive (audio.drive):");
        output.ShouldContain("  |> in");
        output.ShouldContain("  >  drive");
    }

    [Fact]
    public void The_json_has_the_word_and_the_signature()
    {
        var (code, output, error) = Run("hover", Written(Patch), "--at", "1:13", "--json");

        code.ShouldBe(Exit.Ok, error);

        var root = JsonDocument.Parse(output).RootElement;
        root.GetProperty("hover").GetProperty("title").GetString().ShouldBe("sine · Sine (osc.sine)");
        root.TryGetProperty("signature", out _).ShouldBeFalse("outside a call there is no signature, and nothing is written for it");
    }

    [Fact]
    public void A_place_with_nothing_to_say_says_so()
    {
        var (code, output, _) = Run("hover", Written("# a comment"), "--at", "1:5");

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("nothing to say here");
    }

    [Fact]
    public void A_cursor_that_is_not_a_place_is_refused()
    {
        var (code, _, error) = Run("hover", Written(Patch), "--at", "x");

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("line:column");
    }
}
