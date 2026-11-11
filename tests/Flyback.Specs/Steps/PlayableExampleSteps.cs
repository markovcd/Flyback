using System.CommandLine;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flyback.Plugins.Hosting;
using Flyback.Specs.Support;
using Reqnroll;
using Reqnroll.UnitTestProvider;
using Shouldly;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Specs.Steps;

/// <summary>
/// The patches a page of the built website marks <c>pre data-flyback</c>, each asked
/// of flyback-cli whether it compiles and reaches the picture or the sound.
/// </summary>
[Binding]
public sealed partial class PlayableExampleSteps(IUnitTestRuntimeProvider runtime) : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-playable");

    private string page = string.Empty;
    private IReadOnlyList<(string Title, string Text)> examples = [];

    [When("the examples marked playable on {string} are read")]
    public void WhenRead(string path)
    {
        BuiltSite.Need(runtime);

        page = SiteFiles.Read(path);
        examples = [.. Playable().Matches(page).Select(match => (
            WebUtility.HtmlDecode(match.Groups["title"].Value),
            WebUtility.HtmlDecode(match.Groups["text"].Value)))];
    }

    [Then("there are some, and each is a patch that compiles and reaches the picture or the sound")]
    public void ThenEachPlays()
    {
        examples.ShouldNotBeEmpty();

        var failed = new List<string>();

        foreach (var (title, text) in examples)
        {
            var file = Path.Combine(folder.FullName, $"{Guid.NewGuid():N}.fbks");
            File.WriteAllText(file, text);

            var output = new StringWriter();
            var code = InProcessCli.Run(
                ["info", file, "--json"],
                new PluginRegistry(() => PluginCatalog.Empty, folder.FullName, null),
                new InvocationConfiguration { Output = output, Error = new StringWriter() });

            var wired = code == 0 ? JsonNode.Parse(output.ToString())?["wired"] : null;
            if (wired is null || !((bool?)wired["picture"] == true || (bool?)wired["sound"] == true)) failed.Add(title);
        }

        failed.ShouldBeEmpty("examples marked playable that do not compile or reach neither the picture nor the sound");
    }

    [Then("the page also has colored examples that are not playable")]
    public void ThenFragmentsRemain() => page.ShouldContain("<pre><code class=\"fbks\">");

    public void Dispose() => folder.Delete(recursive: true);

    [GeneratedRegex("""<pre data-flyback="(?<title>[^"]*)"[^>]*>(?<text>.*?)</pre>""", RegexOptions.Singleline)]
    private static partial Regex Playable();
}
