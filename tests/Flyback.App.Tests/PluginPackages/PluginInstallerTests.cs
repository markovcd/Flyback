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

    private static PluginPackage Package() => PluginPackage.Read(Packages.For("win", "linux", "osx"));

    private string Installed(string file = "") => Path.Combine(plugins, Packages.Folder, file);

    [Fact]
    public void An_installed_plugin_is_in_a_folder_named_after_its_assembly_from_the_next_start()
    {
        var installer = Installer();

        installer.Refusal(Package(), "win").ShouldBeNull();
        installer.Stage(Package(), "win");

        Directory.Exists(Installed()).ShouldBeFalse("nothing is loaded until the next start, so nothing is moved before it");

        var (installed, problems) = PluginInstaller.Finish(plugins);

        installed.ShouldHaveSingleItem().ShouldStartWith(Packages.Folder);
        problems.ShouldBeEmpty();
        File.ReadAllBytes(Installed(Packages.AssemblyName)).ShouldBe(Packages.Assembly);
        File.Exists(Installed("runtimes/win/native/readme.txt")).ShouldBeTrue();
        File.Exists(Installed("runtimes/linux/native/readme.txt")).ShouldBeFalse("only this system's build is installed");
        File.ReadAllText(Installed(PluginPackage.MarkerName)).Trim().ShouldBe(Package().Sha256);
        Directory.Exists(Path.Combine(plugins, PluginInstaller.PendingName)).ShouldBeFalse();
    }

    [Fact]
    public void A_new_package_replaces_the_plugin_it_installed_whole()
    {
        Installer().Stage(Package(), "win");
        PluginInstaller.Finish(plugins);
        File.WriteAllText(Installed("left-behind.dll"), "");

        var installer = Installer();

        installer.Replacing(Packages.Folder)!.Assembly.ShouldBe(Packages.Folder);
        installer.Refusal(Package(), "win").ShouldBeNull();
        installer.Stage(Package(), "win");

        PluginInstaller.Finish(plugins).Installed.ShouldHaveSingleItem();

        File.Exists(Installed("left-behind.dll")).ShouldBeFalse();
    }

    [Fact]
    public void A_plugin_that_did_not_come_from_a_package_is_never_replaced_by_one()
    {
        Directory.CreateDirectory(Installed());
        File.WriteAllBytes(Installed(Packages.AssemblyName), Packages.Assembly);

        Installer().Replacing(Packages.Folder).ShouldBeNull();
        Installer().Refusal(Package(), "win")
            .ShouldBe($"There is already a plugin in plugins/{Packages.Folder} that was not installed from a package, and it is left alone.");
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
    public void A_package_may_not_bring_a_second_copy_of_a_plugin_loaded_from_elsewhere()
    {
        var loaded = new LoadedPlugin(new PluginInfo("flyback.picture", "Picture"), Path.Combine(plugins, "Picture", Packages.AssemblyName));

        Installer(loaded).Refusal(Package(), "win").ShouldBe($"Picture is already installed from {Packages.AssemblyName}.");
    }

    [Fact]
    public void A_package_may_replace_the_plugin_it_installed_while_that_plugin_is_loaded()
    {
        Installer().Stage(Package(), "win");
        PluginInstaller.Finish(plugins);

        var loaded = new LoadedPlugin(new PluginInfo("flyback.picture", "Picture"), Installed(Packages.AssemblyName));

        Installer(loaded).Refusal(Package(), "win").ShouldBeNull();
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
        File.WriteAllBytes(Path.Combine(planted, Packages.AssemblyName), Packages.Assembly);

        PluginInstaller.Finish(plugins).Problems.ShouldHaveSingleItem().ShouldStartWith("planted:");

        Directory.Exists(Path.Combine(plugins, "planted")).ShouldBeFalse();
    }

    [Fact]
    public void The_marker_is_what_the_install_wrote_not_what_the_build_carried()
    {
        var package = PluginPackage.Read(Packages.Zip(
        [
            ($"win/{Packages.AssemblyName}", Packages.Assembly),
            ($"win/{PluginPackage.MarkerName}", "forged"u8.ToArray()),
        ]));

        Installer().Stage(package, "win");
        PluginInstaller.Finish(plugins);

        File.ReadAllText(Installed(PluginPackage.MarkerName)).Trim().ShouldBe(package.Sha256);
    }

    [Fact]
    public void The_host_never_loads_what_is_waiting_to_be_installed()
    {
        Installer().Stage(Package(), PluginPackage.ThisPlatform);

        PluginHost.Folders(plugins).ShouldBeEmpty();
    }

    [Fact]
    public void A_packages_plugin_loads_after_every_plugin_no_package_installed()
    {
        Directory.CreateDirectory(Path.Combine(plugins, "Zeta"));
        Directory.CreateDirectory(Path.Combine(plugins, "Alpha"));
        File.WriteAllText(Path.Combine(plugins, "Alpha", PluginPackage.MarkerName), "");

        PluginHost.Folders(plugins).Select(Path.GetFileName).ShouldBe(["Zeta", "Alpha"]);
    }
}
