using System.IO.Compression;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Packs;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>A bundle carrying the packs its patch's modules come from, so it opens where they are not installed.</summary>
public class PackBundleTests
{
    private const string Echoes = """
        pack ada.echoes 1.2.0

        /// A wobble.
        module wobble(rate = 3, level = 0.25) = sine(freq: rate, amp: level)
        """;

    private const string Tones = """
        pack ada.tones 1.0.0

        module hum(level = 0.1) = sine(freq: 50, amp: level)
        """;

    private static ModuleCatalog Installed(params string[] packs)
    {
        var catalog = NodeCatalog.BuiltIn;

        foreach (var pack in packs)
        {
            (catalog, var issues) = PackModules.Into(catalog, pack);
            issues.ShouldBeEmpty();
        }

        return catalog;
    }

    private static Patch Built(string source, ModuleCatalog catalog)
    {
        var load = PatchLanguage.Build(source, catalog);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static MemoryStream Packed(Patch patch, ModuleCatalog catalog)
    {
        var archive = new MemoryStream();

        PatchBundle.Write(archive, patch, _ => null, catalog);
        archive.Position = 0;

        return archive;
    }

    [Fact]
    public void A_bundle_carries_each_pack_its_patch_uses_once_by_id()
    {
        var catalog = Installed(Echoes, Tones);
        var patch = Built("ada.echoes.wobble() + ada.echoes.wobble(rate: 5) |> out.left", catalog);

        using var archive = Packed(patch, catalog);
        using var zip = new ZipArchive(archive);

        zip.Entries.Select(e => e.FullName).ShouldBe([PatchBundle.PatchEntry, "packs/ada.echoes.fbks"], ignoreOrder: true);

        using var reading = new StreamReader(zip.GetEntry("packs/ada.echoes.fbks")!.Open());
        reading.ReadToEnd().ShouldBe(Echoes);
    }

    [Fact]
    public void A_bundle_opens_where_its_pack_is_not_installed()
    {
        var catalog = Installed(Echoes);
        var patch = Built("ada.echoes.wobble() |> out.left", catalog);

        using var archive = Packed(patch, catalog);

        var bundle = PatchBundle.Read(archive, NodeCatalog.BuiltIn);

        bundle.Load.ShouldNotBeNull().IsComplete.ShouldBeTrue();
        bundle.Packs.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(new PackSource("ada.echoes", "1.2.0", Echoes));
        bundle.Modules.ShouldNotBeNull().Get("ada.echoes.wobble").ShouldNotBeNull();
        bundle.Patch.Nodes.ShouldContain(n => n.TypeId == "ada.echoes.wobble");
    }

    [Fact]
    public void An_installed_pack_stands_over_the_one_a_bundle_carries()
    {
        var catalog = Installed(Echoes);
        var patch = Built("ada.echoes.wobble() |> out.left", catalog);

        using var archive = Packed(patch, catalog);

        var bundle = PatchBundle.Read(archive, catalog);

        bundle.Load.ShouldNotBeNull().IsComplete.ShouldBeTrue();
        bundle.Packs.ShouldNotBeNull().ShouldBeEmpty();
        bundle.Modules.ShouldBeNull();
    }

    [Fact]
    public void A_saved_patch_names_the_pack_version_it_was_made_with()
    {
        var catalog = Installed(Echoes);
        var patch = Built("ada.echoes.wobble() |> out.left", catalog);

        var load = PatchIO.Read(PatchIO.ToJson(patch, catalog), NodeCatalog.BuiltIn);

        load.MissingProviders.ShouldBe([new ModuleProvider("ada.echoes", "ada.echoes 1.2.0")]);
    }

    [Fact]
    public void A_patch_without_packs_carries_none()
    {
        var patch = Built("sine(freq: 220) |> out.left", NodeCatalog.BuiltIn);

        using var archive = Packed(patch, NodeCatalog.BuiltIn);
        using var zip = new ZipArchive(archive);

        zip.Entries.Select(e => e.FullName).ShouldBe([PatchBundle.PatchEntry]);
    }
}
