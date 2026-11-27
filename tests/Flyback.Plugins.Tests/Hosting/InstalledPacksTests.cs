using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Hosting;

/// <summary>The packs in a folder joining the catalog a host installs.</summary>
public sealed class InstalledPacksTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flyback-packs-");

    public void Dispose() => folder.Delete(recursive: true);

    private const string Echoes = """
        pack ada.echoes 1.0.0

        /// A wobble.
        module wobble(rate = 3, level = 0.25) = sine(freq: rate, amp: level)
        """;

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(folder.FullName, name), text);

    private PluginCatalog Loaded() => PluginCatalog.Empty.WithPacks(folder.FullName);

    [Fact]
    public void A_pack_in_the_folder_offers_its_modules_under_its_id()
    {
        Write("echoes.fbks", Echoes);

        var catalog = Loaded();

        catalog.Problems.ShouldBeEmpty();
        catalog.Modules.Get("ada.echoes.wobble").ShouldNotBeNull().Name.ShouldBe("Wobble");
        catalog.Modules.ProviderOf("ada.echoes.wobble").ShouldBe(new("ada.echoes", "ada.echoes 1.0.0"));
    }

    [Fact]
    public void No_folder_leaves_the_catalog_as_it_was()
    {
        var empty = PluginCatalog.Empty;

        empty.WithPacks(null).ShouldBeSameAs(empty);
        empty.WithPacks(Path.Combine(folder.FullName, "absent")).ShouldBeSameAs(empty);
    }

    [Fact]
    public void A_broken_pack_says_its_file_and_line_and_the_others_still_load()
    {
        Write("broken.fbks", "pack ada.broken 1.0.0\nmodule bad(in = 0) = sine(fre: in)\n");
        Write("echoes.fbks", Echoes);

        var catalog = Loaded();

        catalog.Modules.Get("ada.echoes.wobble").ShouldNotBeNull();
        catalog.Modules.Get("ada.broken.bad").ShouldBeNull();

        var problem = catalog.Problems.ShouldHaveSingleItem();
        problem.Source.ShouldBe("broken.fbks");
        problem.Message.ShouldStartWith("line 2: ");
        problem.Folder.ShouldBe(folder.FullName);
    }

    [Fact]
    public void A_second_pack_offering_the_same_module_is_refused()
    {
        Write("a.fbks", Echoes);
        Write("b.fbks", Echoes);

        var catalog = Loaded();

        catalog.Modules.Get("ada.echoes.wobble").ShouldNotBeNull();
        catalog.Problems.ShouldHaveSingleItem().Source.ShouldBe("b.fbks");
    }

    [Fact]
    public void A_file_over_the_limit_is_not_read()
    {
        Write("huge.fbks", Echoes + new string(' ', (int)InstalledPacks.MostBytes));

        var catalog = Loaded();

        catalog.Modules.Get("ada.echoes.wobble").ShouldBeNull();
        catalog.Problems.ShouldHaveSingleItem().Message.ShouldContain("over 1024 KB");
    }

    [Fact]
    public void Only_pack_files_load()
    {
        Write("echoes.txt", Echoes);
        Write(".echoes.fbks", Echoes);

        var catalog = Loaded();

        catalog.Modules.Get("ada.echoes.wobble").ShouldBeNull();
        catalog.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Installing_writes_the_pack_under_its_id_and_says_what_it_offers()
    {
        var (installed, problems) = InstalledPacks.Install(Flyback.Core.Graph.NodeCatalog.BuiltIn, folder.FullName, Echoes);

        problems.ShouldBeEmpty();
        installed.ShouldNotBeNull().ShouldBe(new PackInstalled(
            "ada.echoes", "1.0.0", null, installed.Modules, Path.Combine(folder.FullName, "ada.echoes.fbks")));
        installed.Modules.ShouldBe(["ada.echoes.wobble"]);
        Loaded().Modules.Get("ada.echoes.wobble").ShouldNotBeNull();
    }

    [Fact]
    public void Installing_a_new_version_replaces_the_old_and_says_which_it_was()
    {
        InstalledPacks.Install(Flyback.Core.Graph.NodeCatalog.BuiltIn, folder.FullName, Echoes);

        var (installed, _) = InstalledPacks.Install(Flyback.Core.Graph.NodeCatalog.BuiltIn, folder.FullName, Echoes.Replace("1.0.0", "1.1.0"));

        installed.ShouldNotBeNull().Replaced.ShouldBe("1.0.0");
        folder.GetFiles().ShouldHaveSingleItem().Name.ShouldBe("ada.echoes.fbks");
        Loaded().Modules.ProviderOf("ada.echoes.wobble").ShouldBe(new("ada.echoes", "ada.echoes 1.1.0"));
    }

    [Fact]
    public void A_pack_with_a_mistake_is_not_written()
    {
        var (installed, problems) = InstalledPacks.Install(
            Flyback.Core.Graph.NodeCatalog.BuiltIn, folder.FullName, "pack ada.broken 1.0.0\nmodule bad(in = 0) = sine(fre: in)\n");

        installed.ShouldBeNull();
        problems.ShouldHaveSingleItem().ShouldStartWith("line 2: ");
        folder.GetFiles().ShouldBeEmpty();
    }

    [Fact]
    public void A_patch_is_not_a_pack()
    {
        var (installed, problems) = InstalledPacks.Install(Flyback.Core.Graph.NodeCatalog.BuiltIn, folder.FullName, "sine(freq: 220) |> out.left");

        installed.ShouldBeNull();
        problems.ShouldHaveSingleItem().ShouldContain("not a pack");
    }
}
