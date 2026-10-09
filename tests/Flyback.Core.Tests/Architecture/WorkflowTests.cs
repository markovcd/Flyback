using System.Text.RegularExpressions;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// Every workflow under <c>.github/workflows</c> keeps the rules in <c>docs/agents/rules/pipeline.md</c>:
/// it says what it does, declares its permissions, pins every action to a commit and times out every job.
/// </summary>
public partial class WorkflowTests
{
    public static TheoryData<string> Workflows() =>
        [.. Directory.GetFiles(Repository.Path(".github", "workflows"), "*.yml").Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Every_workflow_opens_with_a_comment(string workflow) =>
        Lines(workflow)[0].ShouldStartWith("#", customMessage: $"{workflow} says what it does, what triggers it and why, first");

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Every_workflow_declares_its_permissions(string workflow) =>
        Lines(workflow).ShouldContain(line => line.StartsWith("permissions:", StringComparison.Ordinal), $"{workflow} has no permissions: of its own");

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Every_action_is_pinned_to_a_commit_with_its_version_beside_it(string workflow)
    {
        var unpinned = Lines(workflow)
            .Select(line => Uses().Match(line))
            .Where(used => used.Success && !used.Groups["action"].Value.StartsWith("./", StringComparison.Ordinal))
            .Where(used => !Pinned().IsMatch(used.Groups["action"].Value) || !Version().IsMatch(used.Groups["rest"].Value))
            .Select(used => used.Groups["action"].Value);

        unpinned.ShouldBeEmpty($"{workflow}: a tag is the action author's to move");
    }

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Every_job_has_a_timeout(string workflow)
    {
        var lines = Lines(workflow);
        var start = lines.FindIndex(line => line.StartsWith("jobs:", StringComparison.Ordinal));
        var jobs = lines.Skip(start + 1).TakeWhile(line => line.Length == 0 || line[0] is ' ' or '#').ToList();

        var untimed = new List<string>();

        for (var i = 0; i < jobs.Count; i++)
        {
            if (Job().Match(jobs[i]) is not { Success: true } job) continue;

            var body = jobs.Skip(i + 1).TakeWhile(line => !Job().IsMatch(line)).ToList();

            // A job that calls a reusable workflow may not set one; the workflow's own jobs do.
            if (body.Any(line => line.StartsWith("    uses:", StringComparison.Ordinal))) continue;

            if (!body.Any(line => line.StartsWith("    timeout-minutes:", StringComparison.Ordinal)))
                untimed.Add(job.Groups["name"].Value);
        }

        untimed.ShouldBeEmpty($"{workflow}: the default is six hours of a hang");
    }

    [GeneratedRegex(@"^\s*(-\s+)?uses:\s*(?<action>\S+)(?<rest>.*)$")]
    private static partial Regex Uses();

    [GeneratedRegex(@"^[\w.-]+/[\w./-]+@[0-9a-f]{40}$")]
    private static partial Regex Pinned();

    [GeneratedRegex(@"^\s+#\s*v\d")]
    private static partial Regex Version();

    [GeneratedRegex(@"^  (?<name>[A-Za-z0-9_-]+):\s*$")]
    private static partial Regex Job();

    private static List<string> Lines(string workflow) =>
        [.. File.ReadAllLines(Repository.Path(".github", "workflows", workflow))];
}
