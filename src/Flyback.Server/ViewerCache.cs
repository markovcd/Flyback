using System.Text.RegularExpressions;

namespace Flyback.Server;

/// <summary>
/// How long a browser may keep a file of the web viewer: a file named with its
/// fingerprint for good, and the rest only once it has asked whether it changed.
/// </summary>
/// <remarks>
/// The loader, <c>dotnet.js</c>, has no fingerprint and names every file that has one.
/// Kept without asking, it outlives the build it came with and asks for files a new
/// build no longer has.
/// </remarks>
internal static partial class ViewerCache
{
    public const string Route = "/viewer";

    /// <summary>The Cache-Control a file at <paramref name="path"/> is served with, or null for one outside the viewer.</summary>
    public static string? For(PathString path)
    {
        if (!path.StartsWithSegments(Route)) return null;

        return Fingerprinted().IsMatch(path.Value ?? "") ? "public, max-age=31536000, immutable" : "no-cache";
    }

    [GeneratedRegex(@"\.[a-z0-9]{10}\.[a-z]+$")]
    private static partial Regex Fingerprinted();
}
