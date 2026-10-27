using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>Installed plugins kept out of a macOS bundle, which codesign cannot sign with them in it.</summary>
public sealed class BundlePluginsTests : IDisposable
{
    private readonly string root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"flyback-bundle-{Guid.NewGuid():N}")).FullName;

    private string MacOs => Path.Combine(root, "Flyback.app", "Contents", "MacOS");

    private string Shipped => Path.Combine(MacOs, PluginHost.DirectoryName);

    private string Installed => Path.Combine(root, "data", PluginHost.DirectoryName);

    private PluginAllowances Allowances => new(Path.Combine(root, "allowed-plugins.json"));

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void A_folder_the_build_did_not_ship_moves_out_and_a_shipped_one_stays()
    {
        Folder(Shipped, "MacIO");
        Folder(Shipped, "Flyback.Plugins.Speech");
        File.WriteAllText(Path.Combine(MacOs, ShippedList.FileName), "00  MacIO/MacIO.dll\n");

        BundlePlugins.MoveOut(Shipped, Installed, Allowances).ShouldBeEmpty();

        Directory.Exists(Path.Combine(Shipped, "MacIO")).ShouldBeTrue();
        Directory.Exists(Path.Combine(Shipped, "Flyback.Plugins.Speech")).ShouldBeFalse();
        File.Exists(Path.Combine(Installed, "Flyback.Plugins.Speech", "Flyback.Plugins.Speech.dll")).ShouldBeTrue();
    }

    [Fact]
    public void What_was_allowed_for_a_moved_folder_is_allowed_where_it_went()
    {
        var speech = Folder(Shipped, "Flyback.Plugins.Speech");
        File.WriteAllText(Path.Combine(MacOs, ShippedList.FileName), "");
        Allowances.Allow(speech, secrets: true);

        BundlePlugins.MoveOut(Shipped, Installed, Allowances);

        var moved = Path.Combine(Installed, "Flyback.Plugins.Speech");
        Allowances.For(speech).ShouldBeNull();
        var allowed = Allowances.For(moved).ShouldNotBeNull();
        allowed.Secrets.ShouldBeTrue();
        PluginFiles.Of(moved).Changed(allowed.Files).ShouldBeNull();
    }

    [Fact]
    public void Without_the_shipped_list_nothing_moves()
    {
        Folder(Shipped, "Flyback.Plugins.Speech");

        BundlePlugins.MoveOut(Shipped, Installed, Allowances);

        Directory.Exists(Path.Combine(Shipped, "Flyback.Plugins.Speech")).ShouldBeTrue();
    }

    [Fact]
    public void A_bundle_is_found_only_around_its_Contents_MacOS()
    {
        BundlePlugins.Around(MacOs).ShouldBe(Path.Combine(root, "Flyback.app"));
        BundlePlugins.Around(MacOs + Path.DirectorySeparatorChar).ShouldBe(Path.Combine(root, "Flyback.app"));
        BundlePlugins.Around(Path.Combine(root, "Flyback", "Contents", "MacOS")).ShouldBeNull();
        BundlePlugins.Around(Shipped).ShouldBeNull();
    }

    private static string Folder(string plugins, string name)
    {
        var folder = Directory.CreateDirectory(Path.Combine(plugins, name)).FullName;
        File.WriteAllText(Path.Combine(folder, $"{name}.dll"), name);

        return folder;
    }
}
