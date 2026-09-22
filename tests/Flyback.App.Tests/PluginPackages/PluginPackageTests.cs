using System.Security.Cryptography;
using System.Text;
using Flyback.App.PluginPackages;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.PluginPackages;

public sealed class PluginPackageTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "flyback-package-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void What_the_package_says_about_itself_is_read_from_its_manifest()
    {
        var package = PluginPackage.Read(Packages.Described(Packages.Manifest(website: "https://example.com/ripple"), "win"));

        package.Manifest.ShouldBe(new PluginManifest("acme.ripple", "Ripple", "1.2.0", "Acme", "Rings on water.", "https://example.com/ripple"));
    }

    [Fact]
    public void Its_hash_is_the_sha256_of_the_file()
    {
        var bytes = Packages.For("win");

        PluginPackage.Read(bytes).Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public void This_systems_own_build_is_used_before_the_one_for_any_system()
    {
        var package = PluginPackage.Read(Packages.For("win", "linux", "any"));

        package.Builds.ShouldBe(["win", "linux", "any"]);
        package.BuildFor("linux").ShouldBe("linux");
        package.BuildFor("osx").ShouldBe("any");
        package.Refusal("osx").ShouldBeNull();
    }

    [Fact]
    public void A_package_with_no_build_for_this_system_says_which_it_has()
    {
        var package = PluginPackage.Read(Packages.For("win", "linux"));

        package.BuildFor("osx").ShouldBeNull();
        package.Refusal("osx").ShouldBe("It has no build for macOS, only for Windows, Linux.");
    }

    [Fact]
    public void A_package_with_no_build_at_all_cannot_be_installed_anywhere()
    {
        var package = PluginPackage.Read(Packages.For());

        package.Builds.ShouldBeEmpty();
        package.Refusal("win").ShouldBe("It holds no plugin for any system.");
    }

    [Fact]
    public void A_folder_without_an_assembly_at_its_top_is_not_a_build()
    {
        var package = PluginPackage.Read(Packages.Zip(
        [
            ("plugin.json", Encoding.UTF8.GetBytes(Packages.Manifest())),
            ("linux/readme.txt", [1]),
            ("linux/deeper/Flyback.Plugins.Picture.dll", Packages.Assembly),
            ("osx/Flyback.Core.dll", Packages.Assembly),
        ]));

        package.Builds.ShouldBeEmpty("a readme, a nested assembly and a copy of the host's own are not a plugin");
    }

    [Fact]
    public void A_plugin_built_against_this_contract_may_be_installed()
    {
        PluginPackage.Read(Packages.For("win", "osx", "linux")).Refusal(PluginPackage.ThisPlatform).ShouldBeNull();
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("win/../../evil.dll")]
    [InlineData("/etc/evil.dll")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("win\\..\\..\\evil.dll")]
    [InlineData("win/Plugin.dll:hidden")]
    [InlineData("win/NUL.dll")]
    [InlineData("win/com1")]
    [InlineData("win/evil.dll.")]
    [InlineData("win/evil.dll ")]
    [InlineData("win//evil.dll")]
    [InlineData("win/./evil.dll")]
    [InlineData("readme\u0007.txt")]
    public void A_package_with_a_file_that_could_land_elsewhere_is_refused_whole(string name)
    {
        var refused = Should.Throw<InvalidDataException>(() => PluginPackage.Read(Packages.With(name)));

        refused.Message.ShouldStartWith("It holds a file named");
    }

    [Fact]
    public void Two_files_whose_names_differ_only_in_case_are_refused()
    {
        var refused = Should.Throw<InvalidDataException>(() => PluginPackage.Read(Packages.With("win/flyback.plugins.picture.dll")));

        refused.Message.ShouldEndWith("twice.");
    }

    [Fact]
    public void A_package_that_does_not_say_what_it_is_is_refused()
    {
        var refused = Should.Throw<InvalidDataException>(() => PluginPackage.Read(Packages.Zip([($"win/{Packages.AssemblyName}", Packages.Assembly)])));

        refused.Message.ShouldBe("It has no plugin.json saying what it is.");
    }

    [Fact]
    public void Something_that_is_not_a_zip_is_refused()
    {
        Should.Throw<InvalidDataException>(() => PluginPackage.Read(Encoding.UTF8.GetBytes("MZ not a zip")))
            .Message.ShouldBe("It is not a zip file.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("../plugins")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    [InlineData("trailing.")]
    [InlineData("CON")]
    [InlineData("nul.plugin")]
    [InlineData("ripple\u202Egnp")]
    public void An_id_a_folder_cannot_safely_be_named_after_is_refused(string id)
    {
        var refused = Should.Throw<InvalidDataException>(() => PluginPackage.Read(Packages.Described(Packages.Manifest(id: id), "win")));

        refused.Message.ShouldStartWith("Its id is not one a folder");
    }

    [Theory]
    [InlineData("{\"id\": \"a\", \"version\": \"1\"}", "name")]
    [InlineData("{\"id\": \"a\", \"name\": \"A\"}", "version")]
    [InlineData("{\"name\": \"A\", \"version\": \"1\"}", "id")]
    [InlineData("{\"id\": \"a\", \"name\": \"\u200B\", \"version\": \"1\"}", "name")]
    public void A_manifest_missing_a_required_field_is_refused(string manifest, string field)
    {
        Should.Throw<InvalidDataException>(() => PluginPackage.Read(Packages.Described(manifest, "win")))
            .Message.ShouldBe($"Its plugin.json does not say what its {field} is.");
    }

    [Fact]
    public void A_manifest_saying_one_thing_twice_is_refused()
    {
        var manifest = "{\"id\": \"a\", \"name\": \"Ripple\", \"name\": \"Flyback update\", \"version\": \"1\"}";

        Should.Throw<InvalidDataException>(() => PluginPackage.Read(Packages.Described(manifest, "win")));
    }

    [Fact]
    public void Nothing_in_the_manifest_can_change_how_the_rest_of_it_is_drawn()
    {
        var manifest = Packages.Manifest(
            name: "Ripple\u202Eexe.txt\u200B",
            author: "Acme\u0007\tInc",
            description: "One\u2028Two\n\n\n\nThree\u0000");

        var read = PluginPackage.Read(Packages.Described(manifest, "win")).Manifest;

        read.Name.ShouldBe("Rippleexe.txt");
        read.Author.ShouldBe("Acme Inc");
        read.Description.ShouldBe("One Two\n\nThree");
    }

    [Fact]
    public void Text_longer_than_it_may_be_shown_is_cut()
    {
        var read = PluginPackage.Read(Packages.Described(Packages.Manifest(name: new string('a', 500)), "win")).Manifest;

        read.Name.Length.ShouldBe(64);
        read.Name.ShouldEndWith("…");
    }

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("file:///C:/Windows", null)]
    [InlineData("https://flyback.example@evil.example/", null)]
    [InlineData("not an address", null)]
    [InlineData("http://example.com:8080/a?b=c", "http://example.com:8080/a?b=c")]
    public void Only_a_plain_web_address_is_kept(string website, string? kept)
    {
        PluginPackage.Read(Packages.Described(Packages.Manifest(website: website), "win")).Manifest.Website.ShouldBe(kept);
    }

    [Fact]
    public void A_host_that_looks_like_another_is_spelled_out_in_punycode()
    {
        // A Cyrillic а in place of the Latin one.
        var kept = PluginPackage.Read(Packages.Described(Packages.Manifest(website: "https://exаmple.com/x"), "win")).Manifest.Website;

        kept.ShouldNotBeNull();
        kept.ShouldStartWith("https://xn--");
        kept.ShouldEndWith(".com/x");
    }

    [Fact]
    public void A_package_larger_than_a_package_may_be_is_refused_before_it_is_opened()
    {
        var bytes = Packages.For("win");

        Should.Throw<InvalidDataException>(() => PluginPackage.Read(bytes, new PackageLimits(bytes.Length - 1, long.MaxValue, 100)))
            .Message.ShouldStartWith("It is larger than");
    }

    [Fact]
    public void A_package_that_unpacks_to_more_than_it_may_is_refused()
    {
        var bomb = Packages.With("win/zeros.bin", new byte[4 << 20]);

        bomb.Length.ShouldBeLessThan(1 << 20, "zeros pack small");

        Should.Throw<InvalidDataException>(() => PluginPackage.Read(bomb, new PackageLimits(1 << 30, 2 << 20, 100)))
            .Message.ShouldStartWith("Unpacked, it is larger than");
    }

    [Fact]
    public void A_package_with_too_many_files_is_refused()
    {
        Should.Throw<InvalidDataException>(() => PluginPackage.Read(Packages.For("win", "linux"), new PackageLimits(1 << 30, 1 << 30, 4)))
            .Message.ShouldBe("It holds more than 4 files.");
    }

    [Fact]
    public async Task A_stream_longer_than_a_package_may_be_is_not_read_to_its_end()
    {
        var endless = new EndlessStream();

        await Should.ThrowAsync<InvalidDataException>(() => PluginPackage.ReadAsync(endless, new PackageLimits(1 << 20, 1 << 20, 10)));

        endless.Served.ShouldBeLessThan(2L << 20);
    }

    [Fact]
    public void Unpacking_writes_only_the_build_asked_for()
    {
        var package = PluginPackage.Read(Packages.For("win", "linux"));
        var target = Path.Combine(folder, "ripple");

        package.Unpack("linux", target);

        Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(target, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ShouldBe(["Flyback.Plugins.Picture.deps.json", Packages.AssemblyName, "runtimes/linux/native/lib.bin"]);

        File.ReadAllBytes(Path.Combine(target, Packages.AssemblyName)).ShouldBe(Packages.Assembly);
    }

    private sealed class EndlessStream : Stream
    {
        public long Served { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Served; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Served += count;
            return count;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
