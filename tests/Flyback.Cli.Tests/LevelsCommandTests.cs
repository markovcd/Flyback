using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary><c>flyback-cli levels</c>: the groups, def calls and loop passes a text patch opens.</summary>
public sealed class LevelsCommandTests : IDisposable
{
    private const string Patch =
        """
        def voice(freq) = sine(freq: freq) |> drive(drive: 2)
        group "Bass" {
          let bass = voice(55)
        }
        let pads = for pitch in [220, 330] {
          group "Pad" {
            let p = saw(freq: pitch) |> drive(drive: 3)
          }
          p
        }
        bass + sum(pads) |> out.left
        """;

    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-cli-levels");

    public void Dispose() => folder.Delete(recursive: true);

    private string Written(string name, string text)
    {
        var path = Path.Combine(folder.FullName, name);
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
    public void The_levels_are_listed_inside_one_another_with_their_modules()
    {
        var (code, output, error) = Run("levels", Written("song.fbks", Patch));

        code.ShouldBe(Exit.Ok, error);
        output.ReplaceLineEndings("\n").ShouldBe(
            """
            group Bass  line 2
              call  bass = voice  line 3  [Sine, bass]
            loop  pads = for pitch ×2  line 5
              pass  pass 1/2  220  line 5
                group Pad 1  line 6  [Saw, p]
              pass  pass 2/2  330  line 5
                group Pad 2  line 6  [Saw, p]

            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Of_prints_the_levels_a_module_sits_in()
    {
        var (code, output, error) = Run("levels", Written("song.fbks", Patch), "--of", "bass");

        code.ShouldBe(Exit.Ok, error);
        output.Trim().ShouldBe("Bass › bass = voice");
    }

    [Fact]
    public void The_json_nests_each_level_inside_the_one_around_it()
    {
        var (code, output, error) = Run("levels", Written("song.fbks", Patch), "--json");

        code.ShouldBe(Exit.Ok, error);

        var levels = JsonElement.Parse(output);
        levels.GetArrayLength().ShouldBe(2);

        var pass = levels[1].GetProperty("inside")[1];
        pass.GetProperty("kind").GetString().ShouldBe("pass");
        pass.GetProperty("pass").GetInt32().ShouldBe(2);
        pass.GetProperty("passes").GetInt32().ShouldBe(2);
        pass.GetProperty("item").GetString().ShouldBe("330");
        pass.GetProperty("inside")[0].GetProperty("label").GetString().ShouldBe("Pad 2");
    }

    [Fact]
    public void The_json_gives_a_calls_arguments_and_the_wires_leaving_each_level()
    {
        var (code, output, error) = Run("levels", Written("song.fbks", Patch), "--json");

        code.ShouldBe(Exit.Ok, error);

        var bass = JsonElement.Parse(output)[0];
        var call = bass.GetProperty("inside")[0];

        call.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ShouldBe(["freq = 55"]);
        bass.GetProperty("leaving").EnumerateArray().Select(w => w.GetString()).ShouldHaveSingleItem().ShouldStartWith("bass.out → ");
    }

    [Theory]
    [InlineData("song.fbk", "{}", "is not written as text")]
    [InlineData("song.fbks", "sine(freq: 220) |> out.left", "no module is called 'nothing'")]
    public void What_it_cannot_answer_is_refused_saying_why(string name, string text, string why)
    {
        var (code, _, error) = Run("levels", Written(name, text), "--of", "nothing");

        code.ShouldBe(Exit.Failed);
        error.ShouldContain(why);
    }

    [Fact]
    public void Text_that_does_not_read_is_a_problem_with_the_patch()
    {
        var (code, _, error) = Run("levels", Written("song.fbks", "group \"A\" {"));

        code.ShouldBe(Exit.Problems);
        error.ShouldContain("does not read");
    }
}
