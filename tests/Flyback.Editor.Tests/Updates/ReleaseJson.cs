using System.Text.Json;

namespace Flyback.Editor.Tests.Updates;

/// <summary>A GitHub release as the API returns it, for the tests of what reads one.</summary>
/// <remarks>Compiled into <c>Flyback.Editor.Desktop.Tests</c> as well, by link.</remarks>
internal static class ReleaseJson
{
    public static string Of(string tag = "v0.4.0", bool prerelease = false, params string[] assets) =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft = false,
            prerelease,
            assets = assets.Select(name => new
            {
                name,
                browser_download_url = $"https://github.com/markovcd/Flyback/releases/download/{tag}/{name}",
            }),
        });
}
