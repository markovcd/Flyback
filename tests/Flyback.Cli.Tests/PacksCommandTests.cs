using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Tests;

/// <summary><c>packs install</c> and <c>packs list</c>: putting a module pack where every editor finds it, from a script.</summary>
public sealed class PacksCommandTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"flyback-cli-packs-{Guid.NewGuid():N}")).FullName;

    private string Folder => Path.Combine(root, "packs");

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Written(string name, string text)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private const string Echoes = "pack ada.echoes 1.0.0\nmodule wobble(rate = 3) = sine(freq: rate)\n";

    [Fact]
    public void Installing_a_pack_copies_it_into_the_folder_and_names_its_modules()
    {
        var (code, output, _) = Run("packs", "install", Written("echoes.fbks", Echoes), "--folder", Folder, "--json");

        code.ShouldBe(Exit.Ok);

        var answer = JsonElement.Parse(output);
        answer.GetProperty("id").GetString().ShouldBe("ada.echoes");
        answer.GetProperty("modules")[0].GetString().ShouldBe("ada.echoes.wobble");
        File.ReadAllText(Path.Combine(Folder, "ada.echoes.fbks")).ShouldBe(Echoes);
    }

    [Fact]
    public void A_pack_with_a_mistake_fails_and_says_the_line()
    {
        var (code, _, error) = Run("packs", "install", Written("bad.fbks", "pack ada.bad 1.0.0\nmodule m(in = 0) = sine(fre: in)\n"), "--folder", Folder);

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("bad.fbks: line 2:");
        Directory.Exists(Folder).ShouldBeFalse();
    }

    [Fact]
    public void The_list_says_each_pack_and_each_problem()
    {
        Run("packs", "install", Written("echoes.fbks", Echoes), "--folder", Folder);
        File.WriteAllText(Path.Combine(Folder, "broken.fbks"), "pack ada.broken 1.0.0\nmodule bad(in = 0) = sine(fre: in)\n");

        var (code, output, _) = Run("packs", "list", "--folder", Folder, "--json");

        code.ShouldBe(Exit.Ok);

        var answer = JsonElement.Parse(output);
        answer.GetProperty("packs")[0].GetProperty("id").GetString().ShouldBe("ada.echoes");
        answer.GetProperty("problems")[0].GetProperty("file").GetString().ShouldBe("broken.fbks");
    }

    private (int Code, string Out, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var registry = new PluginRegistry(() => PluginCatalog.Empty, Path.Combine(root, PluginHost.DirectoryName), null);

        var code = Program.Run(args, registry, new InvocationConfiguration { Output = output, Error = error });

        return (code, output.ToString(), error.ToString());
    }
}
