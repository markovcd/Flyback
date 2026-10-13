using System.Text.RegularExpressions;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// <c>docs/diagrams/pipeline.md</c> names every workflow as the Actions tab does, beside its file,
/// and names no workflow that is gone.
/// </summary>
[TestCategory(TestCategory.Unmeasured)]
public partial class PipelineDiagramTests
{
    private static readonly string Diagram = File.ReadAllText(Repository.Path("docs", "diagrams", "pipeline.md"));

    public static TheoryData<string> Workflows() =>
        [.. Directory.GetFiles(Repository.Path(".github", "workflows"), "*.yml").Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Every_workflow_is_drawn_by_its_name_and_file(string workflow)
    {
        var name = Name().Match(File.ReadAllText(Repository.Path(".github", "workflows", workflow))).Groups["name"].Value;

        name.ShouldNotBeEmpty($"{workflow} has a name:");
        Diagram.ShouldContain($"{name} · {workflow}", customMessage: $"docs/diagrams/pipeline.md draws {workflow} as \"{name} · {workflow}\"");
    }

    [Fact]
    public void Every_workflow_drawn_is_there() =>
        Drawn().Matches(Diagram).Select(match => match.Groups["file"].Value).Distinct()
            .Where(file => !File.Exists(Repository.Path(".github", "workflows", file)))
            .ShouldBeEmpty("docs/diagrams/pipeline.md draws a workflow that is gone");

    [GeneratedRegex(@"^name:\s*(?<name>.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex Name();

    [GeneratedRegex(@"· (?<file>[\w.-]+\.yml)")]
    private static partial Regex Drawn();
}
