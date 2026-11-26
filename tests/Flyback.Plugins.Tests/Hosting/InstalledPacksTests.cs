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
}
