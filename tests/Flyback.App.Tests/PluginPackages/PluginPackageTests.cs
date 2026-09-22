using System.Security.Cryptography;
using System.Text;
using Flyback.Plugins.Hosting;
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
    public void What_a_plugin_is_comes_from_its_assembly()
    {
        var plugin = PluginPackage.Read(Packages.For("win")).Description("win");

        plugin.Assembly.ShouldBe(Packages.Folder);
        plugin.Name.ShouldBe(Packages.Folder, "the picture plugin names no product of its own");
        plugin.Version.ShouldNotBeNullOrEmpty();
        plugin.Version.ShouldNotContain("+", customMessage: "the commit the SDK appends is left off");
    }

    [Fact]
    public void What_a_plugin_adds_comes_from_the_registry_methods_its_code_calls()
    {
        PluginPackage.Read(Packages.For("win")).Description("win").Adds.ShouldBe(["modules", "presets"]);
    }

    [Fact]
    public void A_plugin_that_names_nothing_outside_Flyback_reaches_nothing()
    {
        PluginPackage.Read(Packages.For("win")).Description("win").Reaches.ShouldBeEmpty();
    }

    [Fact]
    public void What_an_assembly_carried_beside_the_plugin_reaches_counts_too()
    {
        var package = PluginPackage.Read(Packages.With("win/Helper.dll", Packages.Networking));

        package.Description("win").Reaches.ShouldContain("the network");
    }

    [Fact]
    public void A_native_library_in_a_build_is_native_code()
    {
        var package = PluginPackage.Read(Packages.With("win/runtimes/win-x64/native/sound.dll", Encoding.UTF8.GetBytes("MZ not managed")));

        package.Description("win").Reaches.ShouldBe([AssemblyFacts.NativeCode]);
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
        package.DescriptionFor("osx")!.Assembly.ShouldBe(Packages.Folder, "what it is can still be shown");
    }

    [Fact]
    public void A_package_with_no_build_at_all_cannot_be_installed_anywhere()
    {
        var package = PluginPackage.Read(Packages.Zip([("readme.txt", [1])]));

        package.Builds.ShouldBeEmpty();
        package.DescriptionFor("win").ShouldBeNull();
        package.Refusal("win").ShouldBe("It holds no plugin for any system.");
    }

    [Fact]
    public void A_folder_without_a_plugin_assembly_at_its_top_is_not_a_build()
    {
        var package = PluginPackage.Read(Packages.Zip(
        [
            ("linux/readme.txt", [1]),
            ("linux/deeper/Flyback.Plugins.Picture.dll", Packages.Assembly),
            ("osx/Flyback.Core.dll", File.ReadAllBytes(typeof(Flyback.Core.Graph.NodeDef).Assembly.Location)),
            ("any/Helper.dll", Packages.Networking),
        ]));

        package.Builds.ShouldBeEmpty("a readme, a nested assembly, the host's own and one that is no plugin are not a build");
    }

    [Fact]
    public void A_build_with_two_plugin_assemblies_is_refused()
    {
        var copy = Packages.Zip(
        [
            ($"win/{Packages.AssemblyName}", Packages.Assembly),
            ("win/Second.dll", Packages.Assembly),
        ]);

        Should.Throw<InvalidDataException>(() => PluginPackage.Read(copy)).Message.ShouldContain("2 plugin assemblies");
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
    public void Something_that_is_not_a_zip_is_refused()
    {
        Should.Throw<InvalidDataException>(() => PluginPackage.Read(Encoding.UTF8.GetBytes("MZ not a zip")))
            .Message.ShouldBe("It is not a zip file.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("../plugins")]
    [InlineData(".hidden")]
    [InlineData("trailing.")]
    [InlineData("CON")]
    [InlineData("nul.plugin")]
    [InlineData("ripple\u202Egnp")]
    public void A_name_a_folder_cannot_safely_take_is_refused(string name)
    {
        PluginDescription.ValidFolder(name).ShouldBeFalse();
    }

    [Fact]
    public void Nothing_an_assembly_says_can_change_how_the_rest_is_drawn()
    {
        PluginDescription.Line("Ripple\u202Eexe.txt\u200B", 64).ShouldBe("Rippleexe.txt");
        PluginDescription.Line("Acme\u0007\tInc", 64).ShouldBe("Acme Inc");
        PluginDescription.Paragraph("One\u2028Two\n\n\n\nThree\u0000", 1000).ShouldBe("One Two\n\nThree");
    }

    [Fact]
    public void Text_longer_than_it_may_be_shown_is_cut()
    {
        var cut = PluginDescription.Line(new string('a', 500), 64);

        cut.Length.ShouldBe(64);
        cut.ShouldEndWith("…");
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
            .ShouldBe(["Flyback.Plugins.Picture.deps.json", Packages.AssemblyName, "runtimes/linux/native/readme.txt"]);

        File.ReadAllBytes(Path.Combine(target, Packages.AssemblyName)).ShouldBe(Packages.Assembly);
    }

    [Fact]
    public void Packing_writes_forward_slashes_and_leaves_out_the_folders_it_is_told_to()
    {
        var build = Path.Combine(folder, "build");

        Directory.CreateDirectory(Path.Combine(build, "runtimes", "win-x64"));
        Directory.CreateDirectory(Path.Combine(build, "linux-x64"));
        File.WriteAllBytes(Path.Combine(build, Packages.AssemblyName), Packages.Assembly);
        File.WriteAllBytes(Path.Combine(build, "runtimes", "win-x64", "readme.txt"), [1]);
        File.WriteAllBytes(Path.Combine(build, "linux-x64", Packages.AssemblyName), Packages.Assembly);

        var package = PluginPackage.Read(PluginPackage.Pack([("any", build)], new HashSet<string> { "linux-x64" }));

        package.Files("any").Select(f => f.Path).Order(StringComparer.Ordinal)
            .ShouldBe([Packages.AssemblyName, "runtimes/win-x64/readme.txt"]);
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
