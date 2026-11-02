using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary><c>flyback-cli complete</c>: what may be written at one place in a text patch.</summary>
public sealed class CompleteCommandTests : IDisposable
{
    private const string Patch =
        """
        let lfo = sine(freq: 0.5)
        saw(fr
        """;

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-cli-complete");

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
    public void The_sockets_of_the_call_the_cursor_is_in_are_listed_with_what_each_is_for()
    {
        var (code, output, error) = Run("complete", Written(Patch), "--at", "2:7");

        code.ShouldBe(Exit.Ok, error);
        output.ShouldStartWith("freq");
        output.ShouldContain("socket");
    }

    [Fact]
    public void The_json_says_where_the_word_starts_and_what_choosing_each_item_writes()
    {
        var (code, output, error) = Run("complete", Written(Patch), "--at", "2:7", "--json");

        code.ShouldBe(Exit.Ok, error);

        var root = JsonDocument.Parse(output).RootElement;
        root.GetProperty("line").GetInt32().ShouldBe(2);
        root.GetProperty("column").GetInt32().ShouldBe(5);
        root.GetProperty("typed").GetString().ShouldBe("fr");

        var freq = root.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("label").GetString() == "freq");
        freq.GetProperty("kind").GetString().ShouldBe("socket");
        freq.GetProperty("insert").GetString().ShouldBe("freq: ");
    }

    [Fact]
    public void A_place_with_nothing_to_offer_says_so()
    {
        var (code, output, _) = Run("complete", Written("# a comm"), "--at", "1:9");

        code.ShouldBe(Exit.Ok);
        output.ShouldContain("nothing to offer here");
    }

    [Theory]
    [InlineData("2", "line:column")]
    [InlineData("0:3", "line:column")]
    [InlineData("a:b", "line:column")]
    public void A_cursor_that_is_not_a_place_is_refused(string at, string why)
    {
        var (code, _, error) = Run("complete", Written(Patch), "--at", at);

        code.ShouldBe(Exit.Failed);
        error.ShouldContain(why);
    }

    [Fact]
    public void A_missing_file_is_refused()
    {
        var (code, _, error) = Run("complete", Path.Combine(folder.FullName, "gone.fbks"), "--at", "1:1");

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("does not exist");
    }
}
