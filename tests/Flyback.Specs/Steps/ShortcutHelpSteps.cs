using System.Text.RegularExpressions;
using Flyback.Editor.Inspect;
using Flyback.Tests;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The keys the website's prose names, against the rows of the editor's help.</summary>
[Binding]
public sealed partial class ShortcutHelpSteps
{
    private IReadOnlyList<string> named = [];

    [When("the keys named on the website are read")]
    public void WhenRead() =>
        named = [.. Shortcut().Matches(File.ReadAllText(Repository.Path("site", "index.html")))
            .Select(match => Tag().Replace(match.Value, "")).Distinct()];

    [Then("each of them is listed in the editor's shortcut help")]
    public void ThenEachIsInTheHelp()
    {
        named.ShouldNotBeEmpty();

        var listed = InspectorHelp.Shortcuts(inPage: false, fingers: false)
            .SelectMany(group => group.Rows)
            .SelectMany(row => row.Keys.Split(" / "))
            .ToHashSet(StringComparer.Ordinal);

        named.Where(key => !listed.Contains(key)).ShouldBeEmpty("keys the website names and the help does not");
    }

    /// <summary>A key, or keys held together: <c>&lt;kbd&gt;Ctrl&lt;/kbd&gt;+&lt;kbd&gt;Z&lt;/kbd&gt;</c>.</summary>
    [GeneratedRegex(@"(?:<kbd>[^<]+</kbd>\+?)+")]
    private static partial Regex Shortcut();

    [GeneratedRegex(@"</?kbd>")]
    private static partial Regex Tag();
}
