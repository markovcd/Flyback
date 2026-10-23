using System.Text.RegularExpressions;
using Flyback.Tests;
using Shouldly;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// Every image the Dockerfile starts from is pinned by digest, where Dependabot moves it, and a
/// workflow or a script that runs the SDK or Node reads it from there with scripts/base-image.sh.
/// </summary>
[TestCategory(TestCategory.Unmeasured)]
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
    public void A_workflow_or_script_reads_the_SDK_or_Node_image_from_the_Dockerfile(string file)
    {
        var named = Run().Matches(File.ReadAllText(Repository.Path(file))).Select(match => match.Value);

        named.ShouldBeEmpty($"{file} names an image Dependabot moves only in the Dockerfile; run \"$(scripts/base-image.sh <stage>)\"");
    }

    [Theory]
    [MemberData(nameof(Runners))]
    public void Every_stage_base_image_sh_is_asked_for_starts_from_a_pinned_image(string file)
    {
        var stages = File.ReadAllLines(Repository.Path("Dockerfile"))
            .Select(line => From().Match(line))
            .Where(from => from.Success && from.Groups["image"].Value.Contains(':'))
            .Select(from => from.Groups["stage"].Value)
            .ToList();

        var unknown = Asked().Matches(File.ReadAllText(Repository.Path(file)))
            .Select(match => match.Groups["stage"].Value)
            .Where(stage => !stages.Contains(stage));

        unknown.ShouldBeEmpty($"{file} asks base-image.sh for a stage the Dockerfile does not start from an image");
    }

    /// <summary>The images the Dockerfile's FROM lines name; a stage's own name has no tag.</summary>
    private static List<string> Images() =>
    [
        .. File.ReadAllLines(Repository.Path("Dockerfile"))
            .Select(line => From().Match(line))
            .Where(from => from.Success && from.Groups["image"].Value.Contains(':'))
            .Select(from => from.Groups["image"].Value)
    ];

    [GeneratedRegex(@"^FROM\s+(?<image>\S+)(\s+AS\s+(?<stage>\S+))?")]
    private static partial Regex From();

    [GeneratedRegex(@"@sha256:[0-9a-f]{64}$")]
    private static partial Regex Digest();

    [GeneratedRegex(@"(mcr\.microsoft\.com/dotnet/[\w.-]+|\bnode):[\w.-]+(@sha256:[0-9a-f]{64})?")]
    private static partial Regex Run();

    [GeneratedRegex(@"base-image\.sh\s+(?<stage>[\w-]+)")]
    private static partial Regex Asked();
}
