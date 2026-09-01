using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// A patch that uses a plugin's module records which plugin, so opening it
/// somewhere that plugin is absent says so instead of quietly losing modules.
/// </summary>
public class PatchProvenanceTests
{
    private static readonly ModuleProvider Extras = new("test.extras", "Extra modules");

    private static readonly NodeDef Doubler = new(
        "test.extras.double", "Double", "Test",
        [new PortSpec("in")],
        [new PortSpec("out")],
        (em, i) => [em.Mul(i[0], 2f)]);

    /// <summary>A catalogue with the plugin, and one without — the two machines.</summary>
    private static ModuleCatalog With() => NodeCatalog.BuiltIn.With(Extras, [Doubler]).Catalog;

    private static ModuleCatalog Without() => NodeCatalog.BuiltIn;

    private static Patch PatchUsing(NodeDef def)
    {
        var patch = new Patch();
        patch.Nodes.Add(NodeInstance.Create(def, 0, 0));
        return patch;
    }

    [Fact]
    public void A_patch_of_built_in_modules_records_nothing()
    {
        var json = PatchIO.ToJson(PatchUsing(NodeCatalog.BuiltIn.Require("output")), With());

        json.ShouldNotContain("Requires");
    }

    [Fact]
    public void A_patch_using_a_plugin_records_it_by_id_and_name()
    {
        var patch = PatchUsing(Doubler);

        PatchIO.ToJson(patch, With());

        patch.Requires.ShouldHaveSingleItem().ShouldBe(Extras);
    }

    [Fact]
    public void The_same_plugin_is_recorded_once_however_many_modules_are_used()
    {
        var patch = PatchUsing(Doubler);
        patch.Nodes.Add(NodeInstance.Create(Doubler, 100, 0));

        PatchIO.ToJson(patch, With());

        patch.Requires!.Count.ShouldBe(1);
    }

    [Fact]
    public void It_opens_cleanly_where_the_plugin_is_installed()
    {
        var json = PatchIO.ToJson(PatchUsing(Doubler), With());

        var loaded = PatchIO.Read(json, With());

        loaded.IsComplete.ShouldBeTrue();

        // Two, because reading gives every patch its Output. What the plugin
        // contributed is the other one.
        loaded.Patch.Nodes.Select(n => n.TypeId)
            .ShouldBe(["test.extras.double", NodeCatalog.OutputTypeId]);
    }

    [Fact]
    public void It_names_the_missing_plugin_where_it_is_not()
    {
        var json = PatchIO.ToJson(PatchUsing(Doubler), With());

        var loaded = PatchIO.Read(json, Without());

        loaded.IsComplete.ShouldBeFalse();
        loaded.MissingProviders.ShouldHaveSingleItem().ShouldBe(Extras);
        loaded.Summary.ShouldContain("Extra modules");
        loaded.Summary.ShouldContain("test.extras");
        loaded.Detail.ShouldContain("Extra modules");
    }

    /// <summary>
    /// The stamp is a claim the file makes about itself, so it is not trusted on
    /// its own. A file that was hand-edited, or written before a module was
    /// renamed, has modules that cannot be built without any plugin being
    /// missing — and that has to be reported too.
    /// </summary>
    [Fact]
    public void A_module_that_does_not_exist_is_reported_even_with_nothing_missing()
    {
        var json = """
            {
              "Nodes": [ { "Id": "8f9d1d3e-0000-4000-8000-000000000001", "TypeId": "nowhere.at.all" } ],
              "Connections": []
            }
            """;

        var loaded = PatchIO.Read(json, Without());

        loaded.IsComplete.ShouldBeFalse();
        loaded.MissingProviders.ShouldBeEmpty();
        loaded.UnknownModules.ShouldHaveSingleItem().ShouldBe("nowhere.at.all");
    }

    [Fact]
    public void A_patch_saved_before_any_of_this_still_opens()
    {
        var json = """
            {
              "Nodes": [ { "Id": "8f9d1d3e-0000-4000-8000-000000000002", "TypeId": "output" } ],
              "Connections": []
            }
            """;

        PatchIO.Read(json, Without()).IsComplete.ShouldBeTrue();
    }
}
