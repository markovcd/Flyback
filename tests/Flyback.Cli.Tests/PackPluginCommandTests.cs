using Flyback.Plugins.Hosting;
using Flyback.Plugins.Sample;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary><c>pack-plugin</c>, with a stand-in for <c>dotnet publish</c> wherever a project is built.</summary>
public sealed class PackPluginCommandTests : IDisposable
{
    private readonly string folder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"flyback-pack-plugin-{Guid.NewGuid():N}")).FullName;

    private static readonly string Plugin = typeof(SampleModulesPlugin).Assembly.Location;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private FileInfo Output => new(Path.Combine(folder, "sample.fbkp"));

    /// <summary>A folder holding the sample plugin as its build writes it, with a copy of the host's own beside it.</summary>
    private DirectoryInfo Built(string name = "build")
    {
        var build = Directory.CreateDirectory(Path.Combine(folder, name));

        File.Copy(Plugin, Path.Combine(build.FullName, Path.GetFileName(Plugin)));
        File.Copy(typeof(PluginHost).Assembly.Location, Path.Combine(build.FullName, "Flyback.Plugins.dll"));

        return build;
    }

    private static Published Unreachable(string project, string platform, string into) =>
        throw new InvalidOperationException("a folder is packed without building anything");

    private static (int Code, string Out, string Error) Run(
        FileSystemInfo? source,
        FileInfo output,
        string[] platforms,
        Dictionary<string, DirectoryInfo>? folders = null,
        Func<string, string, string, Published>? publish = null)
    {
        var (writer, error) = (new StringWriter(), new StringWriter());
        var code = PackPluginCommand.Run(source, output, platforms, writer, error, folders, publish ?? Unreachable);

        return (code, writer.ToString(), error.ToString());
    }

    [Fact]
    public void A_folder_already_built_is_packed_without_the_sdk()
    {
        var (code, output, _) = Run(Built(), Output, []);

        code.ShouldBe(Exit.Ok);

        var package = PluginPackage.Read(File.ReadAllBytes(Output.FullName));

        package.Builds.ShouldBe([PluginPackage.AnyPlatform]);
        package.Files("any").ShouldNotContain(f => f.Path == "Flyback.Plugins.dll", "the host supplies its own");
        output.ShouldContain("adds      modules");
        output.ShouldContain($"sha256    {package.Sha256}");
    }

    [Fact]
    public void Each_systems_folder_becomes_that_systems_build()
    {
        var folders = new Dictionary<string, DirectoryInfo> { ["win"] = Built("win"), ["linux"] = Built("linux") };

        Run(null, Output, [], folders).Code.ShouldBe(Exit.Ok);

        PluginPackage.Read(File.ReadAllBytes(Output.FullName)).Builds.ShouldBe(["win", "linux"]);
    }

    [Fact]
    public void A_project_is_built_once_for_each_system_named()
    {
        var project = new FileInfo(Path.Combine(folder, "Sample.csproj"));
        File.WriteAllText(project.FullName, "<Project />");

        var asked = new List<string>();

        var (code, _, _) = Run(project, Output, ["win", "osx"], publish: (_, platform, into) =>
        {
            asked.Add(platform);
            Directory.CreateDirectory(into);
            File.Copy(Plugin, Path.Combine(into, Path.GetFileName(Plugin)));

            return new Published(0, "");
        });

        code.ShouldBe(Exit.Ok);
        asked.ShouldBe(["win", "osx"]);
        PluginPackage.Read(File.ReadAllBytes(Output.FullName)).Builds.ShouldBe(["win", "osx"]);
    }

    [Fact]
    public void A_project_that_does_not_build_writes_nothing_and_says_what_the_sdk_said()
    {
        var project = new FileInfo(Path.Combine(folder, "Sample.csproj"));
        File.WriteAllText(project.FullName, "<Project />");

        var (code, _, error) = Run(project, Output, [], publish: (_, _, _) => new Published(1, "error CS1002: ; expected"));

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("error CS1002");
        Output.Exists.ShouldBeFalse();
    }

    [Fact]
    public void A_folder_with_no_plugin_in_it_writes_nothing()
    {
        var empty = Directory.CreateDirectory(Path.Combine(folder, "empty"));
        File.WriteAllText(Path.Combine(empty.FullName, "readme.txt"), "");

        var (code, _, error) = Run(empty, Output, []);

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("has no plugin assembly at its top");
        Output.Exists.ShouldBeFalse();
    }

    [Fact]
    public void A_source_and_per_system_folders_together_are_refused()
    {
        var (code, _, error) = Run(Built(), Output, [], new() { ["win"] = Built("win") });

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("but not both");
    }

    [Fact]
    public void A_system_no_package_holds_a_build_for_is_refused()
    {
        var (code, _, error) = Run(Built(), Output, ["amiga"]);

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("amiga is not a system");
    }
}
