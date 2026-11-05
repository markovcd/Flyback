using Flyback.Tests;

namespace Flyback.Specs.Support;

/// <summary>The website's files as the Worker serves them: the site Hugo built from <c>site/</c>, with the viewer and the editor from their projects' pages.</summary>
internal static class SiteFiles
{
    private static readonly string Root = Repository.Root;

    /// <summary>The file a request for <paramref name="path"/> is answered from, or null where the site has none.</summary>
    public static string? Find(string path)
    {
        var relative = path.TrimStart('/');
        if (relative.Length == 0 || relative.EndsWith('/')) relative += "index.html";

        var (folder, rest) = relative.Split('/', 2) switch
        {
            ["viewer", var tail] => (Path.Combine("src", "Flyback.Viewer.Web", "wwwroot"), tail),
            ["editor", var tail] => (Path.Combine("src", "Flyback.Editor.Web", "wwwroot"), tail),
            _ => (BuiltSite.Root, relative),
        };

        var under = Path.GetFullPath(Path.Combine(Root, folder));
        var file = Path.GetFullPath(Path.Combine(under, rest));
        return file.StartsWith(under, StringComparison.Ordinal) && File.Exists(file) ? file : null;
    }

    public static string Read(string path) =>
        File.ReadAllText(Find(path) ?? throw new FileNotFoundException($"The site has no {path}."));
}
