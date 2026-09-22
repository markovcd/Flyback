using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// Whoever holds the private half of <paramref name="Key"/>: the author of every
/// package it signed (ADR-0132).
/// </summary>
/// <param name="Key">The public key, as a base64 SubjectPublicKeyInfo.</param>
internal sealed record PackageSigner(string Key)
{
    /// <summary>
    /// Whether keys are checked. A Debug build installs unsigned packages and lets one
    /// author's package replace another's, so a plugin can be tried without a key.
    /// </summary>
    public static bool Checked { get; } = !Debug;

#if DEBUG
    private const bool Debug = true;
#else
    private const bool Debug = false;
#endif

    /// <summary>The entry at a package's top that holds its signature.</summary>
    public const string EntryName = "signature.json";

    /// <summary>The SHA-256 of the key, which is what a person compares.</summary>
    public string Fingerprint => Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(Key)));

    /// <summary>The signer <paramref name="text"/> names, or null for text that is not a key.</summary>
    public static PackageSigner? Parse(string text)
    {
        var key = text.Trim();

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);

            return new PackageSigner(key);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    /// <summary>A new P-256 key, as the PEM an author keeps.</summary>
    public static string NewKey()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return ecdsa.ExportPkcs8PrivateKeyPem();
    }

    /// <summary>The private key in <paramref name="pem"/>.</summary>
    /// <exception cref="InvalidDataException">Where it holds no P-256 private key.</exception>
    public static ECDsa Load(string pem)
    {
        var ecdsa = ECDsa.Create();

        try
        {
            ecdsa.ImportFromPem(pem);
            _ = ecdsa.ExportParameters(includePrivateParameters: true);

            if (ecdsa.KeySize != 256) throw new CryptographicException();

            return ecdsa;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            ecdsa.Dispose();
            throw new InvalidDataException("It holds no P-256 private key. Make one with flyback-cli plugin-key.");
        }
    }

    /// <summary>The signer of <paramref name="key"/>'s signatures.</summary>
    public static PackageSigner Of(ECDsa key) => new(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));

    /// <summary>A copy of the package in <paramref name="zip"/> with <paramref name="key"/>'s signature added.</summary>
    public static byte[] Sign(byte[] zip, ECDsa key)
    {
        using var memory = new MemoryStream();
        memory.Write(zip);

        using (var archive = new ZipArchive(memory, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry(EntryName)?.Delete();

            var signature = key.SignData(Manifest(archive, long.MaxValue), HashAlgorithmName.SHA256);
            var entry = archive.CreateEntry(EntryName);

            // Fixed, like the entries a build's files become, so nothing but the signature varies.
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

            using var stream = entry.Open();
            JsonSerializer.Serialize(stream, new Signature(Of(key).Key, Convert.ToBase64String(signature)));
        }

        return memory.ToArray();
    }

    /// <summary>Who signed <paramref name="archive"/>, or null where nobody did.</summary>
    /// <exception cref="InvalidDataException">Where its signature cannot be read or does not match what it holds.</exception>
    public static PackageSigner? Verify(ZipArchive archive, long budget)
    {
        if (archive.GetEntry(EntryName) is not { } entry) return null;

        Signature? read;

        try
        {
            if (entry.Length > 4096) throw new JsonException();

            using var stream = entry.Open();
            read = JsonSerializer.Deserialize<Signature>(stream);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Its signature cannot be read.");
        }

        if (read?.Key is not { } text || read.Value is not { } value || Parse(text) is not { } signer)
            throw new InvalidDataException("Its signature cannot be read.");

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(signer.Key), out _);

        byte[] signature;

        try
        {
            signature = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new InvalidDataException("Its signature cannot be read.");
        }

        if (!ecdsa.VerifyData(Manifest(archive, budget), signature, HashAlgorithmName.SHA256))
            throw new InvalidDataException("Its signature does not match what it holds, so it was changed after it was signed.");

        return signer;
    }

    /// <summary>
    /// What a signature covers: every entry but the signature, by name, each as the
    /// SHA-256 of its bytes and its name on a line.
    /// </summary>
    private static byte[] Manifest(ZipArchive archive, long budget)
    {
        var text = new StringBuilder();
        var buffer = new byte[81920];

        foreach (var entry in archive.Entries.Where(e => e.FullName != EntryName).OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var stream = entry.Open();
            int read;

            while ((read = stream.Read(buffer)) > 0)
            {
                budget -= read;

                if (budget < 0) throw new InvalidDataException("Unpacked, it is larger than a plugin package may be.");

                hash.AppendData(buffer, 0, read);
            }

            text.Append(Convert.ToHexStringLower(hash.GetHashAndReset())).Append("  ").Append(entry.FullName).Append('\n');
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private sealed record Signature(
        [property: System.Text.Json.Serialization.JsonPropertyName("key")] string? Key,
        [property: System.Text.Json.Serialization.JsonPropertyName("signature")] string? Value);
}
