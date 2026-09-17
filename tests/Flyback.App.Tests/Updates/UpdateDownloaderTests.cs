using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Flyback.App.Updates;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Updates;

/// <summary>
/// Finding, checking and unpacking a release, against a GitHub that answers from
/// memory. What matters most is what is left on disk when a check fails: nothing
/// that looks installable.
/// </summary>
public sealed class UpdateDownloaderTests : IDisposable
{
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "flyback-updates-" + Guid.NewGuid().ToString("N"));

    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private static readonly Installation Here = new("unused", "Flyback.exe", "win-x64", Bundle: false);

    private static readonly Version Running = new(0, 3, 0);

    private UpdateFolder Folder => new(Path.Combine(scratch, "updates"));

    public void Dispose()
    {
        key.Dispose();
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }

    /// <summary>A package laid out the way the release workflow zips one: the platform's folder at the top.</summary>
    private byte[] Package(string rid = "win-x64")
    {
        var source = Path.Combine(scratch, "source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(source, rid, "plugins", "WinIO"));
        File.WriteAllText(Path.Combine(source, rid, "Flyback.exe"), "new shell");
        File.WriteAllText(Path.Combine(source, rid, "plugins", "WinIO", "WinIO.dll"), "new plugin");

        var zip = Path.Combine(scratch, Guid.NewGuid().ToString("N") + ".zip");
        ZipFile.CreateFromDirectory(source, zip);

        return File.ReadAllBytes(zip);
    }

    private sealed class GitHub : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Asked.Add(url);

            return Task.FromResult(Files.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private const string Download = "https://github.com/markovcd/Flyback/releases/download/v0.4.0/";

    /// <summary>A release of 0.4.0, signed with <see cref="key"/> unless told otherwise.</summary>
    private GitHub Published(byte[]? package = null, byte[]? listed = null, ECDsa? signer = null)
    {
        package ??= Package();
        var name = "flyback-0.4.0-win-x64.zip";

        var checksums = Encoding.UTF8.GetBytes(
            $"{ReleaseSignature.Hex(SHA256.HashData(listed ?? package))}  {name}\n");

        var github = new GitHub();
        github.Files[ReleaseFeed.Latest.AbsoluteUri] = Encoding.UTF8.GetBytes(
            ReleaseFeedTests.ReleaseJson(assets: [name, "SHA256SUMS", "SHA256SUMS.sig"]));
        github.Files[Download + name] = package;
        github.Files[Download + "SHA256SUMS"] = checksums;
        github.Files[Download + "SHA256SUMS.sig"] =
            (signer ?? key).SignData(checksums, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return github;
    }

    private Task<Version?> Run(GitHub github, Version? running = null)
    {
        var http = new HttpClient(github);
        return new UpdateDownloader(http, key, Folder).DownloadAsync(Here, running ?? Running, CancellationToken.None);
    }

    /// <summary>Everything under the updates folder, so a test can say nothing was left behind.</summary>
    private string[] Left() =>
        Directory.Exists(Folder.Root) ? Directory.GetFileSystemEntries(Folder.Root) : [];

    [Fact]
    public async Task A_newer_signed_release_is_unpacked_and_waits()
    {
        var ready = await Run(Published());

        ready.ShouldBe(new Version(0, 4, 0));
        Folder.Pending(Running).ShouldBe(new Version(0, 4, 0));

        var payload = Here.PayloadIn(Folder.VersionFolder(new Version(0, 4, 0)));
        File.ReadAllText(Path.Combine(payload, "Flyback.exe")).ShouldBe("new shell");

        Left().ShouldNotContain(entry => entry.EndsWith(".partial"), "nothing half-done is left");
    }

    [Fact]
    public async Task Nothing_newer_is_nothing_downloaded()
    {
        var github = Published();

        (await Run(github, running: new Version(0, 4, 0))).ShouldBeNull();

        github.Asked.ShouldBe([ReleaseFeed.Latest.AbsoluteUri], "only the question is asked");
        Left().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_release_signed_with_another_key_is_never_downloaded()
    {
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var github = Published(signer: stranger);

        await Should.ThrowAsync<InvalidDataException>(() => Run(github));

        github.Asked.ShouldNotContain(Download + "flyback-0.4.0-win-x64.zip");
        Folder.Pending(Running).ShouldBeNull();
    }

    [Fact]
    public async Task A_package_that_is_not_what_was_signed_is_never_unpacked()
    {
        var github = Published(listed: "a different package"u8.ToArray());

        await Should.ThrowAsync<InvalidDataException>(() => Run(github));

        Folder.Pending(Running).ShouldBeNull();
        Left().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_package_without_this_platforms_copy_is_refused()
    {
        var github = Published(package: Package(rid: "linux-x64"));

        await Should.ThrowAsync<InvalidDataException>(() => Run(github));

        Folder.Pending(Running).ShouldBeNull();
        Left().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_version_already_waiting_is_not_downloaded_again()
    {
        await Run(Published());
        var again = Published();

        (await Run(again)).ShouldBe(new Version(0, 4, 0));

        again.Asked.ShouldBe([ReleaseFeed.Latest.AbsoluteUri]);
    }
}
