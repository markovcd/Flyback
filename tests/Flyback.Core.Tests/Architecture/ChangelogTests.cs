using System.Text.RegularExpressions;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// <c>CHANGELOG.md</c>'s <c>## Unreleased</c> is written the way the <c>changelog</c> skill says, so a release
/// renames it without grooming: every bullet under one of the release headings, in their order, and none
/// carrying an ADR or a clause explaining itself.
/// </summary>
[TestCategory(TestCategory.Unmeasured)]
public partial class ChangelogTests
{
    private static readonly string[] Headings =
        ["Modules", "Presets", "Assistant", "Editor", "Viewer", "Command line", "Installing", "Performance", "Fixes"];

    private static readonly string[] Unreleased = File.ReadAllLines(Repository.Path("CHANGELOG.md"))
        .SkipWhile(line => line != "## Unreleased")
        .Skip(1)
        .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
        .ToArray();

    [Fact]
    public void Unreleased_uses_the_release_headings_in_their_order()
    {
        var headings = Unreleased.Where(line => line.StartsWith("### ", StringComparison.Ordinal)).Select(line => line[4..]).ToList();

        headings.Except(Headings).ShouldBeEmpty($"a heading in Unreleased is one of: {string.Join(", ", Headings)}");
        headings.ShouldBe(headings.OrderBy(heading => Array.IndexOf(Headings, heading)).ToList(), "Unreleased's headings keep the release order");
    }

    [Fact]
    public void Every_unreleased_bullet_sits_under_a_heading() =>
        Unreleased.TakeWhile(line => !line.StartsWith("### ", StringComparison.Ordinal))
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .ShouldBeEmpty("a bullet goes under the heading it belongs to");

    [Fact]
    public void No_unreleased_bullet_cites_an_adr_or_explains_itself() =>
        Unreleased.Where(line => line.StartsWith("- ", StringComparison.Ordinal) && Explains().IsMatch(line))
            .ShouldBeEmpty("a bullet names what changed: no ADR, and no clause starting \"which\", \"so that\" or \"because\"");

    [GeneratedRegex(@"ADR-\d|, which\b|\bso that\b|\bbecause\b", RegexOptions.IgnoreCase)]
    private static partial Regex Explains();
}
