using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Flyback.App.Updates;

/// <summary>
/// Whether a release was published by whoever holds Flyback's release key.
/// </summary>
/// <remarks>
/// A release carries <c>SHA256SUMS</c>, one line per package in the form
/// <c>sha256sum</c> writes, and <c>SHA256SUMS.sig</c>, an ECDSA P-256 signature
/// over that file as <c>openssl dgst -sha256 -sign</c> writes it (DER). Signing the
/// list rather than each package keeps it to one signature a release, and the list
/// names every package by its version, so a genuine list from an older release
/// cannot be passed off as a newer one.
/// <para>
/// Only verification happens here, and only through the platform's own ECDSA —
/// nothing is invented, which is the line ADR-0034 draws (ADR-0088).
/// </para>
/// </remarks>
internal static class ReleaseSignature
{
    public const string ChecksumsName = "SHA256SUMS";
    public const string SignatureName = "SHA256SUMS.sig";

    /// <summary>What the public key is compiled in as — see the project file.</summary>
    private const string KeyResource = "release-key.pem";

    /// <summary>
    /// The public key this build trusts, or null where none was compiled in, in
    /// which case nothing is ever installed.
    /// </summary>
    public static ECDsa? EmbeddedKey()
    {
        using var stream = typeof(ReleaseSignature).Assembly.GetManifestResourceStream(KeyResource);

        if (stream is null) return null;

        using var reader = new StreamReader(stream);

        return ReadKey(reader.ReadToEnd());
    }

    /// <summary>
    /// A public key from PEM text, or null for text with no key in it — the
    /// placeholder a repository without a key yet carries.
    /// </summary>
    public static ECDsa? ReadKey(string pem)
    {
        if (!pem.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal)) return null;

        var key = ECDsa.Create();

        try
        {
            key.ImportFromPem(pem);
            return key;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            key.Dispose();
            return null;
        }
    }

    /// <summary>
    /// The package hashes <paramref name="checksums"/> lists, keyed by file name —
    /// or null unless <paramref name="signature"/> is <paramref name="key"/>'s
    /// signature over exactly those bytes.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Verified(byte[] checksums, byte[] signature, ECDsa key)
    {
        bool genuine;

        try
        {
            genuine = key.VerifyData(checksums, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            // Bytes that are not a DER signature at all say the same as a wrong one.
            genuine = false;
        }

        return genuine ? Parse(Encoding.UTF8.GetString(checksums)) : null;
    }

    /// <summary>
    /// Reads <c>sha256sum</c>'s output. A line that is not a hash and a name is
    /// skipped rather than refused: the signature already vouches for the file, and
    /// what matters is only whether the package being installed is in it.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Parse(string checksums)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in checksums.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2 || parts[0].Length != 64 || !IsHex(parts[0])) continue;

            // sha256sum marks a file read in binary mode with a leading asterisk.
            hashes[parts[1].TrimStart('*')] = parts[0].ToLowerInvariant();
        }

        return hashes;
    }

    private static bool IsHex(string text) =>
        text.All(c => char.IsAsciiHexDigit(c));

    /// <summary>A hash as <c>sha256sum</c> prints one.</summary>
    public static string Hex(byte[] hash) =>
        string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
}
