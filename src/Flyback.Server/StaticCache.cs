using System.Text.RegularExpressions;

namespace Flyback.Server;

/// <summary>
/// How long a cache may keep a file the site serves as it lies: a web viewer file named
/// with its fingerprint for good, and everything else only once it has asked whether it changed.
/// </summary>
/// <remarks>
/// A page, a script or a render keeps its name across releases and renders, so a copy kept
/// without asking outlives the one it was. The viewer's loader, <c>dotnet.js</c>, is such a
/// file, and it names every fingerprinted one.
/// </remarks>
internal static partial class StaticCache
{
    public const string ViewerRoute = "/viewer";

    public const string EditorRoute = "/editor";

    /// <summary>The Cache-Control a file at <paramref name="path"/> is served with.</summary>
    public static string For(PathString path) =>
        path.StartsWithSegments(ViewerRoute) && Fingerprinted().IsMatch(path.Value ?? "")
            ? "public, max-age=31536000, immutable"
            : "no-cache";

    [GeneratedRegex(@"\.[a-z0-9]{10}\.[a-z]+$")]
    private static partial Regex Fingerprinted();
}
