using System.Text.RegularExpressions;
using Flyback.Tests;
using Shouldly;
using Xunit;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// <c>tests/mutation-log.tsv</c>, which <c>/mutate</c> appends to and <c>scripts/mutation-score.py</c> scores,
/// stays readable: its header, and on each row a commit, a date and counts that add up.
/// </summary>
[TestCategory(TestCategory.Unmeasured)]
public partial class MutationLogTests
{
    private static readonly string[] Lines = File.ReadAllLines(Repository.Path("tests", "mutation-log.tsv"));

    [Fact]
    public void The_log_opens_with_its_header() =>
        Lines[0].ShouldBe("commit\tdate\tplanted\tcaught\ttests_added");

    [Fact]
    public void Every_row_names_a_commit_and_a_date_and_never_catches_more_than_it_planted()
    {
        var bad = Lines.Skip(1)
            .Where(line => line.Length > 0)
            .Where(line => Row().Match(line) is not { Success: true } row
                || int.Parse(row.Groups["caught"].Value) > int.Parse(row.Groups["planted"].Value)
                || int.Parse(row.Groups["planted"].Value) == 0);

        bad.ShouldBeEmpty("a row is: short hash, yyyy-MM-dd, planted (at least one), caught (at most planted), tests added, tab-separated");
    }

    [GeneratedRegex(@"^[0-9a-f]{7,40}\t\d{4}-\d{2}-\d{2}\t(?<planted>\d+)\t(?<caught>\d+)\t\d+$")]
    private static partial Regex Row();
}
