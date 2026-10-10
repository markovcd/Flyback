using System.Text.RegularExpressions;
using Flyback.Core.Graph;
using Flyback.Tests;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The plugin guide's Standard help tables carry the words <see cref="SocketHelp"/> fills in,
/// since a plugin author cannot read the internal class.
/// </summary>
public sealed partial class StandardHelpGuideTests
{
    private const string DomainRow = "any `Domain`";

    [Fact]
    public void The_input_table_matches_the_standard_inputs()
    {
        var table = Table("Input");
        var domain = SocketHelp.Standard(new PortSpec("in", Domain: true), input: true);

        table.ShouldContainKeyAndValue(DomainRow, domain);
        table.Remove(DomainRow);
        table.ShouldBe(SocketHelp.Inputs, ignoreOrder: true);
    }

    [Fact]
    public void The_output_table_matches_the_standard_outputs() =>
        Table("Output").ShouldBe(SocketHelp.Outputs, ignoreOrder: true);

    /// <summary>The rows of the table headed <c>| side | Help |</c> in the Standard help section, one entry per socket name.</summary>
    private static Dictionary<string, string> Table(string side)
    {
        var guide = File.ReadAllText(Repository.Path("docs", "plugin-guide.md"));
        var section = guide[guide.IndexOf("### Standard help", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n### ", 1, StringComparison.Ordinal)];

        var lines = section.Split('\n').SkipWhile(line => !line.StartsWith($"| {side} | Help |", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'));

        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var cells = line.Trim().Trim('|').Split(" | ");
            var help = cells[1].Trim();
            if (cells[0].Trim() == DomainRow)
            {
                rows[DomainRow] = help;
                continue;
            }

            foreach (Match name in SocketName().Matches(cells[0]))
                rows[name.Groups[1].Value] = help;
        }

        rows.ShouldNotBeEmpty($"docs/plugin-guide.md should have a '| {side} | Help |' table under Standard help");
        return rows;
    }

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex SocketName();
}
