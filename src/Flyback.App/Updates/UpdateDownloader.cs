using System.IO.Compression;
using System.Security.Cryptography;

namespace Flyback.App.Updates;

/// <summary>
/// Fetches the latest release, if it is newer, and leaves it unpacked and checked in
/// the <see cref="UpdateFolder"/> for the next start to install.
/// </summary>
/// <remarks>
/// The order is what makes it safe: the signature on the checksums is checked
/// before the package is downloaded, and the package against the checksums before
/// it is unpacked, so nothing that did not come from the holder of the release key
/// is ever written anywhere but a <c>.partial</c> file.
/// </remarks>
internal sealed class UpdateDownloader(HttpClient http, ECDsa key, UpdateFolder folder)
{
    /// <summary>More than any checksum list or signature needs, and less than a mistake could fill a disk with.</summary>
    private const long LargestManifest = 64 * 1024;

    /// <summary>A published package is a few hundred megabytes at most.</summary>
    private const long LargestPackage = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// The version now waiting to be installed, or null where there is nothing newer
    /// than <paramref name="running"/> for <paramref name="here"/>. Throws, with a
    /// sentence worth putting in a log, when a release is found and cannot be used.
    /// </summary>
    public async Task<Version?> DownloadAsync(Installation here, Version running, CancellationToken cancel)
    {
        if (await ReleaseFeed.LatestAsync(http, here.Rid, cancel) is not { } release || release.Version <= running)
            return null;

        if (Directory.Exists(folder.VersionFolder(release.Version))) return release.Version;

        Directory.CreateDirectory(folder.Root);
        folder.ClearPartials();

        var checksums = await BytesAsync(release.Checksums, cancel);
        var signature = await BytesAsync(release.Signature, cancel);

        var hashes = ReleaseSignature.Verified(checksums, signature, key)
            ?? throw new InvalidDataException(
                $"Flyback {release.Version} is not signed with the release key, so it was not downloaded.");

        if (!hashes.TryGetValue(release.PackageName, out var expected))
            throw new InvalidDataException($"The signed checksums for Flyback {release.Version} do not list {release.PackageName}.");

        var package = folder.PartialPackage(release.PackageName);
        var unpacked = folder.PartialFolder(release.Version);

        try
        {
            var actual = await DownloadAsync(release.Package, package, cancel);

            if (actual != expected)
                throw new InvalidDataException(
                    $"{release.PackageName} does not match its signed checksum, so it was not installed.");

            ZipFile.ExtractToDirectory(package, unpacked);

            Prepare(here, here.PayloadIn(unpacked));

            Directory.Move(unpacked, folder.VersionFolder(release.Version));
        }
        finally
        {
            File.Delete(package);
            if (Directory.Exists(unpacked)) Directory.Delete(unpacked, recursive: true);
        }

        folder.ClearAllBut(release.Version);

        return release.Version;
    }

    /// <summary>
    /// Makes an unpacked package something that can run: checks it has the shape of
    /// this platform's copy, marks its programs executable, and signs a Mac bundle.
    /// </summary>
    /// <remarks>
    /// Unpacking restores the modes the release workflow zipped, and the marking is
    /// here in case it did not. Signing is not optional on a Mac with Apple silicon,
    /// which will not start an unsigned program at all — and this one has to start,
    /// since the new version is what installs itself (ADR-0088).
    /// </remarks>
    private static void Prepare(Installation here, string payload)
    {
        var shell = Path.Combine(payload, here.Executable);

        if (!File.Exists(shell))
            throw new InvalidDataException($"The package does not have {here.Executable} where this copy does.");

        if (OperatingSystem.IsWindows()) return;

        foreach (var program in new[] { shell, Path.Combine(Path.GetDirectoryName(shell)!, "flyback-cli") })
        {
            if (!File.Exists(program)) continue;

            File.SetUnixFileMode(program, File.GetUnixFileMode(program)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        if (here.Bundle) Updater.Sign(payload);
    }

    private async Task<byte[]> BytesAsync(Uri url, CancellationToken cancel)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > LargestManifest)
            throw new InvalidDataException($"{url} is larger than a checksum list or signature can be.");

        await using var source = await response.Content.ReadAsStreamAsync(cancel);
        using var bytes = new MemoryStream();

        await Copy(source, bytes, LargestManifest, cancel);

        return bytes.ToArray();
    }

    /// <summary>Writes <paramref name="url"/> to <paramref name="path"/>, and answers its SHA-256 as hex.</summary>
    private async Task<string> DownloadAsync(Uri url, string path, CancellationToken cancel)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancel);
        using var sha = SHA256.Create();

        await using (var file = File.Create(path))
        await using (var hashing = new CryptoStream(file, sha, CryptoStreamMode.Write))
            await Copy(source, hashing, LargestPackage, cancel);

        return ReleaseSignature.Hex(sha.Hash!);
    }

    private static async Task Copy(Stream source, Stream target, long limit, CancellationToken cancel)
    {
        var buffer = new byte[81920];
        long total = 0;

        for (int read; (read = await source.ReadAsync(buffer, cancel)) > 0;)
        {
            total += read;

            if (total > limit) throw new InvalidDataException("A download was larger than it could honestly be.");

            await target.WriteAsync(buffer.AsMemory(0, read), cancel);
        }
    }
}
