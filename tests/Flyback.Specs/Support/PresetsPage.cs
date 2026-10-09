using System.Text.Json.Nodes;
using Reqnroll.UnitTestProvider;
using Shouldly;

namespace Flyback.Specs.Support;

/// <summary>The preset site's <c>presets.js</c>, run under Node on a stand-in document by <c>presets-page.mjs</c>.</summary>
internal static class PresetsPage
{
    /// <summary>
    /// The elements the page built, by id, once every fetch it made has been answered from
    /// <paramref name="answers"/>: each address, with or without its query, to its JSON.
    /// </summary>
    /// <param name="page">The body's <c>data-page</c>: <c>shelf</c> or <c>preset</c>.</param>
    public static JsonObject Open(IUnitTestRuntimeProvider runtime, string page, string search, JsonObject answers)
    {
        var folder = Directory.CreateTempSubdirectory("flyback-presets-page");

        try
        {
            var file = Path.Combine(folder.FullName, "answers.json");
            File.WriteAllText(file, answers.ToJsonString());

            var (exit, printed, said) = NodeScript.Run(
                runtime,
                "the presets page",
                Path.Combine(AppContext.BaseDirectory, "Support", "presets-page.mjs"),
                [SiteFiles.Find("/assets/presets.js")!, page, search, file],
                folder.FullName,
                TimeSpan.FromSeconds(30));

            exit.ShouldBe(0, said);

            return JsonNode.Parse(printed)!.AsObject();
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>Every element under <paramref name="element"/>, itself first.</summary>
    public static IEnumerable<JsonObject> All(JsonNode? element)
    {
        if (element is not JsonObject e) yield break;

        yield return e;

        foreach (var child in e["children"]!.AsArray())
            foreach (var below in All(child))
                yield return below;
    }

    /// <summary>The text of <paramref name="element"/> and everything under it.</summary>
    public static string Text(JsonNode? element) => string.Concat(All(element).Select(e => (string?)e["text"]));

    /// <summary>The elements under <paramref name="element"/> of class <paramref name="name"/>.</summary>
    public static IEnumerable<JsonObject> OfClass(JsonNode? element, string name) =>
        All(element).Where(e => ((string?)e["attributes"]!["class"] ?? "").Split(' ').Contains(name));
}
