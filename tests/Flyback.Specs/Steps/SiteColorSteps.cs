using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flyback.Tests;
using Reqnroll;
using Shouldly;

namespace Flyback.Specs.Steps;

/// <summary>The website's colors: written once, in <c>site.css</c>'s <c>:root</c>, and equal to the Graphite scheme's.</summary>
[Binding]
public sealed partial class SiteColorSteps
{
    private static readonly string Site = Path.Combine(Repository.Root, "site");

    private string root = string.Empty;
    private readonly List<string> strays = [];
    private readonly List<string> themeColors = [];

    [When("someone reads the website's pages, stylesheets and scripts")]
    public void WhenTheSiteIsRead()
    {
        foreach (var file in Directory.EnumerateFiles(Site, "*.*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetRelativePath(Site, file).Replace('\\', '/');
            var text = File.ReadAllText(file);

            switch (Path.GetExtension(file))
            {
                case ".css":
                    if (name == "assets/site.css")
                    {
                        var at = Root().Match(text);
                        at.Success.ShouldBeTrue("site.css has no :root block.");
                        root = at.Value;
                        text = text.Remove(at.Index, at.Length);
                    }
                    Collect(name, text, CssColor());
                    break;
                case ".html":
                    foreach (Match meta in ThemeColor().Matches(text)) themeColors.Add($"{name}: {meta.Groups["color"].Value}");
                    foreach (Match style in StyleBlock().Matches(text)) Collect(name, style.Value, CssColor());
                    foreach (Match attribute in ColorAttribute().Matches(text)) strays.Add($"{name}: {attribute.Value}");
                    break;
                case ".js" or ".mjs":
                    Collect(name, text, ScriptColor());
                    break;
            }
        }
    }

    [Then("every color in them is one of site.css's, and those are the Graphite scheme's")]
    public void ThenColorsComeFromOnePlace()
    {
        strays.ShouldBeEmpty("Name a color in site.css's :root and use its var(): " + Environment.NewLine + string.Join(Environment.NewLine, strays));

        var site = Declared().Matches(root).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value.Trim());
        var scheme = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "src", "Flyback.Ui", "Controls", "Schemes", "graphite.json")))!;

        var expected = new Dictionary<string, string>();
        foreach (var group in new[] { "grounds", "text" })
            foreach (var (key, value) in scheme[group]!.AsObject())
                expected[Variable(key)] = value!.GetValue<string>();
        expected["attention"] = scheme["accent"]!.GetValue<string>();

        // The site greys nothing out.
        expected.Remove("inactive");

        var differ = expected
            .Where(e => !site.TryGetValue(e.Key, out var value) || !value.Equals(e.Value, StringComparison.OrdinalIgnoreCase))
            .Select(e => $"--{e.Key}: {(site.TryGetValue(e.Key, out var value) ? value : "missing")}, Graphite has {e.Value}")
            .ToList();
        differ.ShouldBeEmpty(string.Join(Environment.NewLine, differ));

        themeColors.ShouldNotBeEmpty();
        themeColors.Where(t => !t.EndsWith(": " + expected["toolbar"], StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty($"A page's theme-color is the toolbar's, {expected["toolbar"]}.");
    }

    private void Collect(string name, string text, Regex color)
    {
        foreach (Match m in color.Matches(text)) strays.Add($"{name}: {m.Value}");
    }

    /// <summary>A scheme key as site.css names it: <c>gridMajor</c> is <c>grid-major</c>, and a module is a <c>node</c>.</summary>
    private static string Variable(string key) =>
        Kebab().Replace(key.Replace("module", "node", StringComparison.Ordinal), "-$1").ToLowerInvariant();

    [GeneratedRegex(@":root\s*\{[^}]*\}")]
    private static partial Regex Root();

    [GeneratedRegex(@"--(?<name>[a-z-]+):\s*(?<value>[^;]+);")]
    private static partial Regex Declared();

    /// <summary>A hex color, one escaped into a data URL, or an rgb() that is not plain black or white.</summary>
    [GeneratedRegex(@"(?<![\w&-])#[0-9a-fA-F]{3,8}\b|%23[0-9a-fA-F]{3,8}\b|rgba?\((?!0, 0, 0[,)]|255, 255, 255[,)])[^)]*\)")]
    private static partial Regex CssColor();

    [GeneratedRegex(@"['""`]#[0-9a-fA-F]{6}(?:[0-9a-fA-F]{2})?['""`]|rgba?\((?!0, 0, 0[,)]|255, 255, 255[,)])[^)]*\)")]
    private static partial Regex ScriptColor();

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>|\sstyle=""[^""]*""")]
    private static partial Regex StyleBlock();

    [GeneratedRegex(@"\s(?:fill|stroke|stop-color|color|bgcolor)=""#[^""]*""")]
    private static partial Regex ColorAttribute();

    [GeneratedRegex(@"<meta name=""theme-color"" content=""(?<color>[^""]+)""")]
    private static partial Regex ThemeColor();

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex Kebab();
}
