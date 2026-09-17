using System.Security.Cryptography;
using System.Text;
using Flyback.App.Updates;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Updates;

/// <summary>
/// The one check standing between a download and an install: that the checksum list
/// was signed by whoever holds the release key.
/// </summary>
public class ReleaseSignatureTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly byte[] Checksums =
        Encoding.UTF8.GetBytes($"{Hash}  flyback-0.4.0-win-x64.zip\n{Hash.ToUpperInvariant()} *flyback-0.4.0-linux-x64.zip\n");

    /// <summary>What <c>openssl dgst -sha256 -sign</c> writes: a DER sequence.</summary>
    private static byte[] Sign(ECDsa key, byte[] data) =>
        key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    [Fact]
    public void A_list_signed_with_the_key_is_read()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var hashes = ReleaseSignature.Verified(Checksums, Sign(key, Checksums), key).ShouldNotBeNull();

        hashes["flyback-0.4.0-win-x64.zip"].ShouldBe(Hash);
        hashes["flyback-0.4.0-linux-x64.zip"].ShouldBe(Hash, "binary-mode lines and upper-case hex read the same");
    }

    [Fact]
    public void A_list_changed_after_signing_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = Sign(key, Checksums);

        var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Checksums).Replace("0.4.0", "0.5.0"));

        ReleaseSignature.Verified(changed, signature, key).ShouldBeNull();
    }

    [Fact]
    public void A_list_signed_with_another_key_is_refused()
    {
        using var ours = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var theirs = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        ReleaseSignature.Verified(Checksums, Sign(theirs, Checksums), ours).ShouldBeNull();
    }

    [Fact]
    public void Bytes_that_are_no_signature_are_refused_rather_than_thrown()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        ReleaseSignature.Verified(Checksums, "not a signature"u8.ToArray(), key).ShouldBeNull();
        ReleaseSignature.Verified(Checksums, [], key).ShouldBeNull();
    }

    [Fact]
    public void A_public_key_is_read_from_pem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        using var read = ReleaseSignature.ReadKey(key.ExportSubjectPublicKeyInfoPem()).ShouldNotBeNull();

        ReleaseSignature.Verified(Checksums, Sign(key, Checksums), read).ShouldNotBeNull();
    }

    /// <summary>A repository without a key yet builds a program that installs nothing, rather than one that fails to start.</summary>
    [Fact]
    public void Text_with_no_key_in_it_is_no_key()
    {
        ReleaseSignature.ReadKey("No release key yet.").ShouldBeNull();
        ReleaseSignature.ReadKey("-----BEGIN PUBLIC KEY-----\nnonsense\n-----END PUBLIC KEY-----").ShouldBeNull();
    }

    /// <summary>
    /// Whatever the build carries is either a key or nothing — never an exception
    /// at startup.
    /// </summary>
    [Fact]
    public void The_embedded_key_can_always_be_asked_for()
    {
        Should.NotThrow(() => ReleaseSignature.EmbeddedKey()?.Dispose());
    }
}
