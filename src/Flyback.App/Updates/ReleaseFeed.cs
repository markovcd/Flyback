using System.Net;
using System.Reflection;
using System.Text.Json;

namespace Flyback.App.Updates;

/// <summary>A published release, as much of it as installing one needs.</summary>
/// <param name="Version">What the release's tag names, as major.minor.patch.</param>
/// <param name="PackageName">The package for this platform, as the signed checksums name it.</param>
internal sealed record Release(Version Version, string PackageName, Uri Package, Uri Checksums, Uri Signature);

/// <summary>
/// Where releases are published: the repository's GitHub Releases, read through the
/// API, which answers without a login up to sixty times an hour per address — one
/// launch asks once.
/// </summary>
internal static class ReleaseFeed
{
    public static readonly Uri Latest = new("https://api.github.com/repos/markovcd/Flyback/releases/latest");

    /// <summary>What the release workflow calls a platform's package.</summary>
    public static string PackageName(Version version, string rid) =>
        $"flyback-{version.ToString(3)}-{rid}.zip";

    /// <summary>
    /// The version this program is, or null for a build that is not a release.
    /// </summary>
    /// <remarks>
    /// A plain build carries a suffix, and the commit after a plus where there is
    /// one — see Directory.Build.props — and reports the same 0.1.0 whatever it was
    /// built from, so it has no place in the order releases come in and is never
    /// updated.
    /// </remarks>
    public static Version? Running() =>
        Released(typeof(ReleaseFeed).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <inheritdoc cref="Running"/>
    internal static Version? Released(string? informational)
    {
        if (informational is null || informational.Contains('+') || informational.Contains('-')) return null;

        return Parse(informational);
    }

    /// <summary>A tag or version as major.minor.patch, or null for anything else.</summary>
    internal static Version? Parse(string text) =>
        Version.TryParse(text.TrimStart('v'), out var version) && version.Build >= 0 && version.Revision < 0
            ? version
            : null;

    /// <summary>
    /// The latest release, or null where there is none or it has no package for
    /// <paramref name="rid"/>. Throws when GitHub cannot be reached, for the caller
    /// to note and carry on.
    /// </summary>
    public static async Task<Release?> LatestAsync(HttpClient http, string rid, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Latest);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await http.SendAsync(request, cancel);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancel), cancellationToken: cancel);

        return Read(document.RootElement, rid);
    }

    /// <summary>The API's answer, read. A release missing any of its three files is no release to install.</summary>
    internal static Release? Read(JsonElement release, string rid)
    {
        if (Flag(release, "draft") || Flag(release, "prerelease")) return null;

        if (!release.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } name
            || Parse(name) is not { } version)
            return null;

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        var urls = new Dictionary<string, Uri>(StringComparer.Ordinal);

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.TryGetProperty("name", out var assetName) && assetName.GetString() is { } file
                && asset.TryGetProperty("browser_download_url", out var url)
                && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps)
                urls[file] = uri;
        }

        var package = PackageName(version, rid);

        return urls.TryGetValue(package, out var packageUrl)
            && urls.TryGetValue(ReleaseSignature.ChecksumsName, out var checksums)
            && urls.TryGetValue(ReleaseSignature.SignatureName, out var signature)
            ? new Release(version, package, packageUrl, checksums, signature)
            : null;
    }

    private static bool Flag(JsonElement release, string name) =>
        release.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
