using System.Text;
using Flyback.App.PluginPackages;
using Flyback.Plugins;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.PluginPackages;

public sealed class PluginInstallerTests : IDisposable
{
    private readonly string plugins = Path.Combine(Path.GetTempPath(), "flyback-plugins-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(plugins)) Directory.Delete(plugins, recursive: true);
    }

    private PluginInstaller Installer(params LoadedPlugin[] loaded) => new(plugins, loaded);

    private static PluginPackage Package(string version = "1.2.0") =>
        PluginPackage.Read(Packages.Described(Packages.Manifest(version: version), "win", "linux", "osx"));

    private string Installed(string file = "") => Path.Combine(plugins, "acme.ripple", file);

    [Fact]
    public void An_installed_plugin_is_in_its_own_folder_from_the_next_start()
    {
        var installer = Installer();

        installer.Refusal(Package(), "win").ShouldBeNull();
        installer.Stage(Package(), "win");

        Directory.Exists(Installed()).ShouldBeFalse("nothing is loaded until the next start, so nothing is moved before it");

        var (installed, problems) = PluginInstaller.Finish(plugins);

        installed.ShouldBe(["Ripple 1.2.0"]);
        problems.ShouldBeEmpty();
        File.ReadAllBytes(Installed(Packages.AssemblyName)).ShouldBe(Packages.Assembly);
        File.Exists(Installed("runtimes/win/native/lib.bin")).ShouldBeTrue();
        File.Exists(Installed("runtimes/linux/native/lib.bin")).ShouldBeFalse("only this system's build is installed");
        Directory.Exists(Path.Combine(plugins, PluginInstaller.PendingName)).ShouldBeFalse();
    }

    [Fact]
    public void A_new_version_replaces_the_old_one_whole()
    {
        Installer().Stage(Package("1.0.0"), "win");
        PluginInstaller.Finish(plugins);
        File.WriteAllText(Installed("left-behind.dll"), "");

        var installer = Installer();

        installer.Replacing("acme.ripple")!.Version.ShouldBe("1.0.0");
        installer.Refusal(Package("2.0.0"), "win").ShouldBeNull();
        installer.Stage(Package("2.0.0"), "win");
        installer.Replacing("acme.ripple")!.Version.ShouldBe("2.0.0", "what waits to be installed is what will be there");

        PluginInstaller.Finish(plugins).Installed.ShouldBe(["Ripple 2.0.0"]);

        File.Exists(Installed("left-behind.dll")).ShouldBeFalse();
        File.ReadAllText(Installed(PluginPackage.ManifestName)).ShouldContain("2.0.0");
    }

    [Fact]
    public void A_plugin_that_did_not_come_from_a_package_is_never_replaced_by_one()
    {
        Directory.CreateDirectory(Installed());
        File.WriteAllText(Installed("Shipped.dll"), "");

        Installer().Refusal(Package(), "win")
            .ShouldBe("There is already a plugin in plugins/acme.ripple that was not installed from a package, and it is left alone.");
    }

    [Fact]
    public void A_folder_filled_by_hand_after_the_install_is_still_left_alone()
    {
        Installer().Stage(Package(), "win");
        Directory.CreateDirectory(Installed());
        File.WriteAllText(Installed("Shipped.dll"), "");

        var (installed, problems) = PluginInstaller.Finish(plugins);

        installed.ShouldBeEmpty();
        problems.ShouldHaveSingleItem().ShouldContain("was not installed from a package");
        File.Exists(Installed("Shipped.dll")).ShouldBeTrue();
    }

    [Fact]
    public void A_package_may_not_take_the_id_of_a_plugin_loaded_from_elsewhere()
    {
        var loaded = new LoadedPlugin(new PluginInfo("acme.ripple", "Ripple (shipped)"), Path.Combine(plugins, "Ripple", "Ripple.dll"));

        Installer(loaded).Refusal(Package(), "win").ShouldBe("Ripple (shipped) already has the id acme.ripple.");
    }

    [Fact]
    public void A_package_may_replace_the_plugin_it_installed_while_that_plugin_is_loaded()
    {
        Installer().Stage(Package("1.0.0"), "win");
        PluginInstaller.Finish(plugins);

        var loaded = new LoadedPlugin(new PluginInfo("acme.ripple", "Ripple"), Installed(Packages.AssemblyName));

        Installer(loaded).Refusal(Package("2.0.0"), "win").ShouldBeNull();
    }

    [Fact]
    public void Where_there_is_no_build_for_this_system_the_package_says_so()
    {
        var package = PluginPackage.Read(Packages.For("linux"));

        Installer().Refusal(package, "win").ShouldBe("It has no build for Windows, only for Linux.");
    }

    [Fact]
    public void An_unpacking_cut_off_part_way_is_cleared_away()
    {
        var cut = Path.Combine(plugins, PluginInstaller.PendingName, ".0123");

        Directory.CreateDirectory(cut);
        File.WriteAllText(Path.Combine(cut, "half.dll"), "");

        PluginInstaller.Finish(plugins).Installed.ShouldBeEmpty();

        Directory.Exists(Path.Combine(plugins, PluginInstaller.PendingName)).ShouldBeFalse();
    }

    [Fact]
    public void Something_waiting_that_no_package_left_is_not_installed()
    {
        var planted = Path.Combine(plugins, PluginInstaller.PendingName, "planted");

        Directory.CreateDirectory(planted);
        File.WriteAllText(Path.Combine(planted, "Planted.dll"), "");

        PluginInstaller.Finish(plugins).Problems.ShouldHaveSingleItem().ShouldStartWith("planted:");

        Directory.Exists(Path.Combine(plugins, "planted")).ShouldBeFalse();
    }

    [Fact]
    public void The_host_never_loads_what_is_waiting_to_be_installed()
    {
        Installer().Stage(Package(), PluginPackage.ThisPlatform);

        var catalog = PluginHost.Load(plugins);

        catalog.Plugins.ShouldBeEmpty();
        catalog.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void What_is_installed_is_what_the_package_said_not_what_the_build_carried()
    {
        var bytes = Packages.Zip(
        [
            ("plugin.json", Encoding.UTF8.GetBytes(Packages.Manifest(version: "1.0.0"))),
            ($"win/{Packages.AssemblyName}", Packages.Assembly),
            ("win/plugin.json", Encoding.UTF8.GetBytes(Packages.Manifest(version: "9.9.9"))),
        ]);

        Installer().Stage(PluginPackage.Read(bytes), "win");
        PluginInstaller.Finish(plugins);

        Installer().Replacing("acme.ripple")!.Version.ShouldBe("1.0.0");
    }
}
