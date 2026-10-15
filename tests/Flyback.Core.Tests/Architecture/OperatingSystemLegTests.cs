using System.Text.RegularExpressions;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// A test that only Windows or only macOS can run is run there: its project is in the solution
/// both native legs test, Build starts Windows on every push and macOS by hand, the Cloud gate's all
/// runs both, and the test is not filed under a category the legs leave out.
/// </summary>
/// <remarks>A test that skips everywhere CI runs it passes without ever having run.</remarks>
[TestCategory(TestCategory.Unmeasured)]
public partial class OperatingSystemLegTests
{
    private static readonly string NativeLegs = Repository.Path(".github", "workflows", "native-tests.yml");

    [Fact]
    public void Every_test_project_is_in_the_solution_the_legs_test()
    {
        var solution = File.ReadAllText(Repository.Path("Flyback.slnx"));

        var missing = Projects()
            .Where(project => File.ReadAllText(project).Contains("<IsTestProject>true</IsTestProject>", StringComparison.Ordinal))
            .Select(project => Path.GetRelativePath(Repository.Root, project).Replace('\\', '/'))
            .Where(project => !solution.Contains($"Path=\"{project}\"", StringComparison.Ordinal));

        missing.ShouldBeEmpty("no leg runs a test project Flyback.slnx leaves out");
        File.ReadAllText(NativeLegs).ShouldContain("dotnet test --solution Flyback.slnx");
        File.ReadAllText(Repository.Path("Dockerfile")).ShouldContain("dotnet test --solution Flyback.slnx");
    }

    [Fact]
    public void Build_runs_windows_on_every_push_and_macos_when_its_box_is_ticked()
    {
        var build = File.ReadAllLines(Repository.Path(".github", "workflows", "ci.yml")).ToList();
        var call = build.FindIndex(line => line.Trim() == "uses: ./.github/workflows/native-tests.yml");

        call.ShouldBeGreaterThan(0, "ci.yml starts native-tests.yml");
        build.Skip(call + 1).TakeWhile(line => line.StartsWith("    ", StringComparison.Ordinal)).Select(line => line.Trim())
            .ShouldContain("machines: ${{ inputs.macos && '[\"windows\",\"macos\"]' || '[\"windows\"]' }}");

        var dispatch = build.SkipWhile(line => line.Trim() != "workflow_dispatch:").TakeWhile(line => line.Trim() != "permissions:").ToList();
        var macos = dispatch.FindIndex(line => line.Trim() == "macos:");
        macos.ShouldBeGreaterThan(0, "Build has a macos box under Run workflow");
        dispatch.Skip(macos + 1).Take(3).ShouldContain(line => line.Trim() == "type: boolean");
    }

    [Fact]
    public void The_cloud_gate_runs_macos_with_everything_else() =>
        File.ReadAllText(Repository.Path(".github", "workflows", "cloud-gate.yml"))
            .ShouldContain("inputs.os == 'all' && '[\"windows\",\"macos\"]'");

    [Fact]
    public void A_test_only_windows_or_macos_runs_is_not_filed_under_a_category_their_legs_leave_out()
    {
        var leftOut = LeftOut().Matches(File.ReadAllText(NativeLegs)).Select(match => match.Groups["category"].Value).ToList();
        leftOut.ShouldNotBeEmpty();

        var sources = Sources().Where(file => OsOnly(File.ReadAllText(file))).ToList();
        sources.ShouldNotBeEmpty("the search finds the Windows-only tests that exist");

        var hidden = sources
            .Where(file => leftOut.Any(category => File.ReadAllText(file).Contains($"TestCategory.{category}", StringComparison.OrdinalIgnoreCase)))
            .Select(file => Path.GetRelativePath(Repository.Root, file));

        hidden.ShouldBeEmpty($"the Windows and macOS legs leave out {string.Join(" and ", leftOut)}; move the test to a class of its own");
    }

    [Theory]
    [InlineData("Assert.SkipUnless(OperatingSystem.IsWindows(), \"the registry is Windows'\");", true)]
    [InlineData("Assert.SkipUnless(OperatingSystem.IsMacOS(), \"CoreMIDI\");", true)]
    [InlineData("Assert.SkipWhen(!OperatingSystem.IsWindows(), \"DPAPI\");", true)]
    [InlineData("Assert.SkipUnless(OperatingSystem.IsLinux(), \"the gate runs it\");", false)]
    [InlineData("Assert.SkipWhen(OperatingSystem.IsWindows(), \"a shell script\");", false)]
    public void The_search_knows_a_test_only_windows_or_macos_runs(string source, bool osOnly) =>
        OsOnly(source).ShouldBe(osOnly);

    private static bool OsOnly(string source) => OnlyThere().IsMatch(source);

    private static IEnumerable<string> Projects() => Files("*.csproj");

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Repository.Path("tests"), "*.cs", SearchOption.AllDirectories).Where(Built);

    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(Repository.Root, pattern, SearchOption.AllDirectories).Where(Built);

    private static bool Built(string file) =>
        !file.Replace('\\', '/').Split('/').Any(part => part is "bin" or "obj" or "node_modules" or ".git" or ".claude");

    [GeneratedRegex(@"Skip(Unless\(\s*|When\(\s*!\s*)OperatingSystem\.Is(Windows|MacOS)\(\)")]
    private static partial Regex OnlyThere();

    [GeneratedRegex(@"--filter-not-trait ""Category=(?<category>\w+)""")]
    private static partial Regex LeftOut();
}
