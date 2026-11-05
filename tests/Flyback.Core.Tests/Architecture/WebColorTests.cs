using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flyback.Tests;
using Shouldly;

namespace Flyback.Core.Tests.Architecture;

/// <summary>
/// The web viewer's colors and the web editor's loading screen are the default scheme's,
/// written out by hand since neither page is built from the scheme files.
/// </summary>
[TestCategory(TestCategory.Unmeasured)]
public partial class WebColorTests
{
    /// <summary>Every color the default scheme names: its grounds, its text and its accent.</summary>
    private static HashSet<string> Scheme()
    {
        var scheme = JsonNode.Parse(File.ReadAllText(Repository.Path("src", "Flyback.Ui", "Controls", "Schemes", "phosphor.json")))!;

        return
        [
            .. new[] { "grounds", "text" }.SelectMany(group => scheme[group]!.AsObject().Select(pair => pair.Value!.GetValue<string>().ToUpperInvariant())),
            scheme["accent"]!.GetValue<string>().ToUpperInvariant(),
        ];
    }

    [Fact]
    public void The_web_viewer_wears_the_default_scheme()
    {
        var css = File.ReadAllText(Repository.Path("src", "Flyback.Viewer.Web", "wwwroot", "viewer.css"));
        var root = Root().Match(css).Value;

        // The one red a fault is shown in stands apart from every scheme.
        var colors = Declared().Matches(root).Where(m => m.Groups["name"].Value != "sink").Select(m => $"--{m.Groups["name"].Value}: {m.Groups["color"].Value}");

        Strays(colors).ShouldBeEmpty("viewer.css's :root takes the Phosphor scheme's colors");
    }

    [Fact]
    public void The_web_editor_loads_in_the_default_scheme()
    {
        var html = File.ReadAllText(Repository.Path("src", "Flyback.Editor.Web", "wwwroot", "index.html"));

        Strays(Hex().Matches(html).Select(m => m.Value)).ShouldBeEmpty("the loading screen takes the Phosphor scheme's colors");
    }

    private static List<string> Strays(IEnumerable<string> colors)
    {
        var scheme = Scheme();
        return [.. colors.Where(color => !scheme.Contains(Hex().Match(color).Value.ToUpperInvariant()))];
    }

    [GeneratedRegex(@":root\s*\{[^}]*\}")]
    private static partial Regex Root();

    [GeneratedRegex(@"--(?<name>[\w-]+):\s*(?<color>#[0-9A-Fa-f]{6})\b")]
    private static partial Regex Declared();

    [GeneratedRegex(@"#[0-9A-Fa-f]{6}\b")]
    private static partial Regex Hex();
}
