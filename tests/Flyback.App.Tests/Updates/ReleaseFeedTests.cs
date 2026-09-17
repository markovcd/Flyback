using System.Text.Json;
using Flyback.App.Updates;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Updates;

/// <summary>Reading GitHub's answer about the latest release, and this program's own version.</summary>
public class ReleaseFeedTests
{
    internal static string ReleaseJson(string tag = "v0.4.0", bool prerelease = false, params string[] assets) =>
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

    private static readonly string[] Complete =
        ["flyback-0.4.0-win-x64.zip", "flyback-0.4.0-linux-x64.zip", "SHA256SUMS", "SHA256SUMS.sig"];

    private static Release? Read(string json, string rid = "win-x64")
    {
        using var document = JsonDocument.Parse(json);
        return ReleaseFeed.Read(document.RootElement, rid);
    }

    [Fact]
    public void A_release_with_this_platforms_package_is_found()
    {
        var release = Read(ReleaseJson(assets: Complete)).ShouldNotBeNull();

        release.Version.ShouldBe(new Version(0, 4, 0));
        release.PackageName.ShouldBe("flyback-0.4.0-win-x64.zip");
        release.Package.AbsoluteUri.ShouldEndWith("/v0.4.0/flyback-0.4.0-win-x64.zip");
        release.Signature.AbsoluteUri.ShouldEndWith("/SHA256SUMS.sig");
    }

    [Fact]
    public void A_release_with_no_package_for_this_platform_is_none()
    {
        Read(ReleaseJson(assets: Complete), rid: "osx-arm64").ShouldBeNull();
    }

    [Fact]
    public void A_release_with_no_signature_is_none()
    {
        Read(ReleaseJson(assets: ["flyback-0.4.0-win-x64.zip", "SHA256SUMS"])).ShouldBeNull();
    }

    [Fact]
    public void A_prerelease_or_an_odd_tag_is_none()
    {
        Read(ReleaseJson(prerelease: true, assets: Complete)).ShouldBeNull();
        Read(ReleaseJson(tag: "nightly", assets: Complete)).ShouldBeNull();
    }

    [Theory]
    [InlineData("0.3.0", "0.3.0")]
    [InlineData("1.12.4", "1.12.4")]
    [InlineData("0.1.0+37fc87f", null)]
    [InlineData("0.4.0-beta", null)]
    [InlineData("0.4", null)]
    [InlineData(null, null)]
    public void Only_a_release_build_has_a_version_to_update_from(string? informational, string? expected)
    {
        ReleaseFeed.Released(informational)?.ToString(3).ShouldBe(expected);

        if (expected is null) ReleaseFeed.Released(informational).ShouldBeNull();
    }

    /// <summary>Versions are compared as numbers, so 0.10.0 is newer than 0.9.0.</summary>
    [Fact]
    public void Versions_compare_as_numbers()
    {
        ReleaseFeed.Parse("v0.10.0").ShouldNotBeNull().ShouldBeGreaterThan(ReleaseFeed.Parse("v0.9.0")!);
    }
}
