using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The rules that make modules-from-plugins safe. Everything here runs on
/// catalogue instances rather than the installed one, so nothing leaks between
/// tests and none of it depends on what is on this machine.
/// </summary>
public class ModuleCatalogTests
{
    private static readonly ModuleProvider Extras = new("test.extras", "Extra modules");

    private static NodeDef Module(string typeId) => new(
        typeId, typeId, "Test",
        [new PortSpec("in")],
        [new PortSpec("out")],
        (em, i) => [em.Mul(i[0], 2f)]);

    [Fact]
    public void The_engines_own_modules_are_attributed_to_the_engine()
    {
        var catalog = NodeCatalog.BuiltIn;

        catalog.Providers.ShouldHaveSingleItem().Id.ShouldBe(NodeCatalog.BuiltInProvider.Id);
        catalog.ProviderOf("output").ShouldBe(NodeCatalog.BuiltInProvider);
    }

    [Fact]
    public void An_added_module_is_found_and_attributed_to_its_provider()
    {
        var added = NodeCatalog.BuiltIn.With(Extras, [Module("test.extras.double")]);

        added.Rejected.ShouldBeEmpty();
        added.Catalog.Get("test.extras.double").ShouldNotBeNull();
        added.Catalog.ProviderOf("test.extras.double").ShouldBe(Extras);
        added.Catalog.HasProvider("test.extras").ShouldBeTrue();
    }

    /// <summary>
    /// A catalogue is what a compiled program was built against. If adding a
    /// plugin could change one that already exists, a running patch and the
    /// catalogue it came from could disagree.
    /// </summary>
    [Fact]
    public void Adding_to_a_catalogue_leaves_it_alone()
    {
        var original = NodeCatalog.BuiltIn;
        var before = original.All.Count;

        original.With(Extras, [Module("test.extras.double")]);

        original.All.Count.ShouldBe(before);
        original.Get("test.extras.double").ShouldBeNull();
    }

    [Fact]
    public void A_module_not_named_after_its_provider_is_refused()
    {
        var added = NodeCatalog.BuiltIn.With(Extras, [Module("somethingelse.double")]);

        added.Rejected.ShouldHaveSingleItem().ShouldContain("somethingelse.double");
        added.Catalog.Get("somethingelse.double").ShouldBeNull();
        added.Catalog.HasProvider("test.extras").ShouldBeFalse();
    }

    /// <summary>One bad module costs that module, not the plugin.</summary>
    [Fact]
    public void The_rest_of_a_providers_modules_still_load()
    {
        var added = NodeCatalog.BuiltIn.With(Extras,
        [
            Module("somethingelse.double"),
            Module("test.extras.good"),
        ]);

        added.Rejected.Count.ShouldBe(1);
        added.Catalog.Get("test.extras.good").ShouldNotBeNull();
    }

    /// <summary>
    /// The one that would actually corrupt a saved patch: a provider claiming a
    /// prefix that existing module ids already sit under could answer for
    /// modules those files mean something else by.
    /// </summary>
    [Fact]
    public void A_provider_cannot_claim_a_prefix_that_is_already_in_use()
    {
        var added = NodeCatalog.BuiltIn.With(new ModuleProvider("osc", "Impostor"), [Module("osc.sine")]);

        added.Rejected.ShouldHaveSingleItem().ShouldContain("osc");
        added.Catalog.Get("osc.sine").ShouldBe(NodeCatalog.BuiltIn.Get("osc.sine"));
    }

    [Fact]
    public void The_engines_own_provider_id_is_reserved()
    {
        var impostor = new ModuleProvider(NodeCatalog.BuiltInProvider.Id, "Impostor");

        NodeCatalog.BuiltIn.With(impostor, [Module("flyback.sneaky")])
            .Rejected.ShouldHaveSingleItem().ShouldContain("reserved");
    }

    [Fact]
    public void The_same_provider_cannot_load_twice()
    {
        var once = NodeCatalog.BuiltIn.With(Extras, [Module("test.extras.a")]).Catalog;

        once.With(Extras, [Module("test.extras.b")])
            .Rejected.ShouldHaveSingleItem().ShouldContain("already loaded");
    }

    [Fact]
    public void A_module_defined_twice_is_added_once()
    {
        var added = NodeCatalog.BuiltIn.With(Extras,
        [
            Module("test.extras.double"),
            Module("test.extras.double"),
        ]);

        added.Rejected.ShouldHaveSingleItem().ShouldContain("twice");
        added.Catalog.All.Count(d => d.TypeId == "test.extras.double").ShouldBe(1);
    }

    [Fact]
    public void A_provider_with_no_usable_modules_is_not_recorded()
    {
        var added = NodeCatalog.BuiltIn.With(Extras, [Module("wrong.prefix")]);

        added.Catalog.Providers.ShouldNotContain(Extras);
    }
}
