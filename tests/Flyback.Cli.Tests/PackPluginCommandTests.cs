using Flyback.Plugins.Hosting;
using Flyback.Plugins.Sample;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary><c>pack-plugin</c>, with a stand-in for the SDK wherever a project is built.</summary>
public sealed class PackPluginCommandTests : IDisposable
{
    private readonly string folder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"flyback-pack-plugin-{Guid.NewGuid():N}")).FullName;

    private static readonly string Plugin = typeof(SampleModulesPlugin).Assembly.Location;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private FileInfo Output => new(Path.Combine(folder, "sample.fbkp"));

    private PluginPackage Written => PluginPackage.Read(File.ReadAllBytes(Output.FullName));

    /// <summary>Puts the sample plugin where a build of it would be, with a copy of the host's own beside it.</summary>
    private string Build(string at)
    {
        var build = Directory.CreateDirectory(Path.Combine(folder, at)).FullName;

        File.Copy(Plugin, Path.Combine(build, Path.GetFileName(Plugin)));
        File.Copy(typeof(PluginHost).Assembly.Location, Path.Combine(build, "Flyback.Plugins.dll"));

        return build;
    }

    private FileInfo Project()
    {
        var project = new FileInfo(Path.Combine(folder, "project", "Sample.csproj"));

        project.Directory!.Create();
        File.WriteAllText(project.FullName, "<Project />");

        return project;
    }

    /// <summary>The SDK as far as this command asks it anything: which runtimes, and a publish that writes the sample plugin.</summary>
    private sealed class Sdk(string runtimes = "", int publishCode = 0)
    {
        public List<string?> Published { get; } = [];

        public Published Run(IReadOnlyList<string> arguments)
        {
            if (arguments[0] == "msbuild")
                return new Published(0, $$"""{ "Properties": { "RuntimeIdentifiers": "{{runtimes}}", "RuntimeIdentifier": "" } }""");

            var runtime = arguments.Contains("-r") ? arguments[arguments.ToList().IndexOf("-r") + 1] : null;
            var into = arguments[arguments.ToList().IndexOf("-o") + 1];

            Published.Add(runtime);

            if (publishCode != 0) return new Published(publishCode, "error CS1002: ; expected");

            Directory.CreateDirectory(into);
            File.Copy(Plugin, Path.Combine(into, Path.GetFileName(Plugin)));

            return new Published(0, "");
        }
    }

    private static Published NoSdk(IReadOnlyList<string> arguments) =>
        throw new InvalidOperationException("a folder already built is packed without the SDK");

    private (int Code, string Out, string Error) Run(FileSystemInfo source, Func<IReadOnlyList<string>, Published>? dotnet = null)
    {
        var (writer, error) = (new StringWriter(), new StringWriter());
        var code = PackPluginCommand.Run(source, Output, writer, error, dotnet ?? NoSdk);

        return (code, writer.ToString(), error.ToString());
    }

    [Fact]
    public void A_folder_the_sdk_built_into_is_packed_as_it_lays_its_builds_out()
    {
        var root = Build("net10.0");
        Build("net10.0/publish");
        Build("net10.0/win-x64/publish");
        Build("net10.0/linux-x64");

        var (code, output, _) = Run(new DirectoryInfo(root));

        code.ShouldBe(Exit.Ok);
        Written.Builds.ShouldBe(["win", "linux", "any"]);
        Written.Files("any").ShouldNotContain(f => f.Path.Contains('/'), "the other runtimes' builds are not part of the portable one");
        Written.Files("any").ShouldNotContain(f => f.Path == "Flyback.Plugins.dll", "the host supplies its own");
        output.ShouldContain("adds      modules");
        output.ShouldContain($"sha256    {Written.Sha256}");
    }

    [Fact]
    public void A_folder_with_only_a_portable_build_is_packed_for_any_system()
    {
        Run(new DirectoryInfo(Build("net10.0"))).Code.ShouldBe(Exit.Ok);

        Written.Builds.ShouldBe([PluginPackage.AnyPlatform]);
    }

    [Fact]
    public void Two_runtimes_for_one_system_are_refused_by_name()
    {
        Build("net10.0/win-x64");
        Build("net10.0/win-arm64");

        var (code, _, error) = Run(new DirectoryInfo(Path.Combine(folder, "net10.0")));

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("win-x64 is a second build for Windows");
        Output.Exists.ShouldBeFalse();
    }

    [Fact]
    public void A_folder_with_no_plugin_in_it_writes_nothing()
    {
        var empty = Directory.CreateDirectory(Path.Combine(folder, "empty"));
        File.WriteAllText(Path.Combine(empty.FullName, "readme.txt"), "");

        var (code, _, error) = Run(empty);

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("holds no plugin build");
        Output.Exists.ShouldBeFalse();
    }

    [Fact]
    public void A_project_is_published_once_for_each_runtime_it_names()
    {
        var sdk = new Sdk("win-x64;osx-arm64;linux-x64");

        Run(Project(), sdk.Run).Code.ShouldBe(Exit.Ok);

        sdk.Published.ShouldBe(["win-x64", "osx-arm64", "linux-x64"]);
        Written.Builds.ShouldBe(["win", "osx", "linux"]);
    }

    [Fact]
    public void A_project_that_names_no_runtime_is_published_once_for_any_system()
    {
        var sdk = new Sdk();

        Run(Project(), sdk.Run).Code.ShouldBe(Exit.Ok);

        sdk.Published.ShouldBe([null]);
        Written.Builds.ShouldBe([PluginPackage.AnyPlatform]);
    }

    [Fact]
    public void A_folder_holding_one_project_is_that_project()
    {
        var sdk = new Sdk();

        Run(Project().Directory!, sdk.Run).Code.ShouldBe(Exit.Ok);

        sdk.Published.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_runtime_for_a_system_a_package_has_no_place_for_is_refused()
    {
        var (code, _, error) = Run(Project(), new Sdk("android-arm64").Run);

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("android-arm64 is not for Windows, macOS or Linux");
    }

    [Fact]
    public void A_project_that_does_not_build_writes_nothing_and_says_what_the_sdk_said()
    {
        var (code, _, error) = Run(Project(), new Sdk(publishCode: 1).Run);

        code.ShouldBe(Exit.Failed);
        error.ShouldContain("error CS1002");
        Output.Exists.ShouldBeFalse();
    }
}
