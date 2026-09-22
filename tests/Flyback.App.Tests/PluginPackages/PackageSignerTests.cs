using System.IO.Compression;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.PluginPackages;

public sealed class PackageSignerTests
{
    /// <summary>A copy of <paramref name="package"/> with <paramref name="change"/> made to its zip after it was signed.</summary>
    private static byte[] Changed(byte[] package, Action<ZipArchive> change)
    {
        using var memory = new MemoryStream();
        memory.Write(package);

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, leaveOpen: true)) change(zip);

        return memory.ToArray();
    }

    [Fact]
    public void A_signed_package_names_the_key_that_signed_it()
    {
        PluginPackage.Read(Packages.For("win")).Signer.ShouldBe(PackageSigner.Of(Packages.Key));
        PluginPackage.Read(Packages.Sign(Packages.Unsigned("win"), Packages.OtherKey)).Signer.ShouldBe(PackageSigner.Of(Packages.OtherKey));
    }

    [Fact]
    public void An_unsigned_package_names_nobody()
    {
        PluginPackage.Read(Packages.Unsigned("win")).Signer.ShouldBeNull();
    }

    [Fact]
    public void A_file_changed_after_signing_is_refused()
    {
        var changed = Changed(Packages.For("win"), zip =>
        {
            zip.GetEntry($"win/{Packages.AssemblyName}")!.Delete();

            using var stream = zip.CreateEntry($"win/{Packages.AssemblyName}").Open();
            stream.Write(Packages.Newer);
        });

        Should.Throw<InvalidDataException>(() => PluginPackage.Read(changed))
            .Message.ShouldBe("Its signature does not match what it holds, so it was changed after it was signed.");
    }

    [Fact]
    public void A_file_added_after_signing_is_refused()
    {
        var added = Changed(Packages.For("win"), zip =>
        {
            using var stream = zip.CreateEntry("win/extra.dll").Open();
            stream.Write([1]);
        });

        Should.Throw<InvalidDataException>(() => PluginPackage.Read(added)).Message.ShouldContain("changed after it was signed");
    }

    [Fact]
    public void Another_keys_signature_moved_onto_a_package_is_refused()
    {
        var moved = Changed(Packages.For("win"), zip =>
        {
            using var from = new ZipArchive(new MemoryStream(Packages.Sign(Packages.Unsigned("win", "linux"), Packages.OtherKey)));
            using var signature = from.GetEntry(PackageSigner.EntryName)!.Open();

            zip.GetEntry(PackageSigner.EntryName)!.Delete();

            using var to = zip.CreateEntry(PackageSigner.EntryName).Open();
            signature.CopyTo(to);
        });

        Should.Throw<InvalidDataException>(() => PluginPackage.Read(moved)).Message.ShouldContain("changed after it was signed");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"key":"AAAA","signature":"AAAA"}""")]
    [InlineData("""{"signature":"AAAA"}""")]
    public void A_signature_that_cannot_be_read_is_refused(string text)
    {
        var package = Packages.Zip([($"win/{Packages.AssemblyName}", Packages.Assembly), (PackageSigner.EntryName, System.Text.Encoding.UTF8.GetBytes(text))]);

        Should.Throw<InvalidDataException>(() => PluginPackage.Read(package)).Message.ShouldBe("Its signature cannot be read.");
    }

    [Fact]
    public void A_key_is_written_as_pem_and_read_back_as_the_same_signer()
    {
        using var key = PackageSigner.Load(PackageSigner.NewKey());

        PluginPackage.Read(PackageSigner.Sign(Packages.Unsigned("win"), key)).Signer.ShouldBe(PackageSigner.Of(key));
    }

    [Fact]
    public void A_file_that_is_not_a_private_key_is_refused()
    {
        Should.Throw<InvalidDataException>(() => PackageSigner.Load("hello")).Message.ShouldContain("plugin-key");
    }

    [Fact]
    public void A_fingerprint_is_the_keys_hash()
    {
        var signer = PackageSigner.Of(Packages.Key);

        signer.Fingerprint.Length.ShouldBe(64);
        PackageSigner.Parse(signer.Key + "\n").ShouldBe(signer);
        PackageSigner.Parse("not a key").ShouldBeNull();
    }
}
