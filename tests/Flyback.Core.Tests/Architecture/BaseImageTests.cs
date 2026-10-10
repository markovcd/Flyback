using System.Text.RegularExpressions;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// Every image the Dockerfile starts from is pinned by digest, where Dependabot moves it, and a
/// workflow or a script that runs the SDK or Node names it exactly as the Dockerfile does.
/// </summary>
public partial class BaseImageTests
{
    public static TheoryData<string> Runners() =>
    [
        .. new[] { Path.Combine(".github", "workflows"), "scripts", "worker" }
            .SelectMany(folder => Directory.GetFiles(Repository.Path(folder))
                .Where(file => file.EndsWith(".yml", StringComparison.Ordinal) || file.EndsWith(".sh", StringComparison.Ordinal))
                .Select(file => Path.Combine(folder, Path.GetFileName(file))))
            .Order(StringComparer.Ordinal)
    ];

    [Fact]
    public void Every_image_the_Dockerfile_starts_from_is_pinned_by_digest()
    {
        var images = Images();

        images.ShouldNotBeEmpty();
        images.Where(image => !Digest().IsMatch(image)).ShouldBeEmpty("a tag is the publisher's to move");
    }

    [Theory]
    [MemberData(nameof(Runners))]
    public void Every_SDK_or_Node_image_a_workflow_or_script_runs_is_the_Dockerfiles(string file)
    {
        var images = Images();

        var strays = Run().Matches(File.ReadAllText(Repository.Path(file)))
            .Select(match => match.Value)
            .Where(image => !images.Contains(image));

        strays.ShouldBeEmpty($"{file} runs an image the Dockerfile does not pin, so Dependabot never moves it");
    }

    /// <summary>The images the Dockerfile's FROM lines name; a stage's own name has no tag.</summary>
    private static List<string> Images() =>
    [
        .. File.ReadAllLines(Repository.Path("Dockerfile"))
            .Select(line => From().Match(line))
            .Where(from => from.Success && from.Groups["image"].Value.Contains(':'))
            .Select(from => from.Groups["image"].Value)
    ];

    [GeneratedRegex(@"^FROM\s+(?<image>\S+)")]
    private static partial Regex From();

    [GeneratedRegex(@"@sha256:[0-9a-f]{64}$")]
    private static partial Regex Digest();

    [GeneratedRegex(@"(mcr\.microsoft\.com/dotnet/[\w.-]+|\bnode):[\w.-]+(@sha256:[0-9a-f]{64})?")]
    private static partial Regex Run();
}
