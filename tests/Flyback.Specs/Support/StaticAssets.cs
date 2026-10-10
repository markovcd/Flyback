using System.Text.Json.Nodes;

namespace Flyback.Specs.Support;

/// <summary>
/// A WebAssembly build's <c>*.staticwebassets.runtime.json</c>: each address the app
/// serves, mapped to the file on disk a build leaves it in, as <c>dotnet run</c> serves it.
/// </summary>
internal sealed class StaticAssets(string[] roots, JsonObject tree)
{
    public static StaticAssets Read(string manifest)
    {
        var read = JsonNode.Parse(File.ReadAllText(manifest))!;
        string[] roots = [.. read["ContentRoots"]!.AsArray().Select(root => (string)root!)];

        return new StaticAssets(roots, read["Root"]!.AsObject());
    }

    /// <summary>The file served at <paramref name="path"/>, relative to the app's root, or null where none is.</summary>
    public string? Find(string path)
    {
        var node = tree;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Contains("..")) return null;

        for (var i = 0; i < parts.Length; i++)
        {
            if (node["Children"]?[parts[i]] is JsonObject child)
            {
                node = child;
                continue;
            }

            // A folder served whole: the rest of the path is under the pattern's root.
            return node["Patterns"]?.AsArray()
                .Select(pattern => Path.Combine(roots[(int)pattern!["ContentRootIndex"]!], Path.Combine(parts[i..])))
                .FirstOrDefault(File.Exists);
        }

        return node["Asset"] is { } asset ? Path.Combine(roots[(int)asset["ContentRootIndex"]!], (string)asset["SubPath"]!) : null;
    }
}
