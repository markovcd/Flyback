using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The whole path for a module that came from outside the engine: loaded from a
/// separate assembly on disk, folded into the catalogue, compiled, and named in
/// a saved patch. None of it is stubbed.
/// </summary>
public class ModulePluginTests
{
    private const string Provider = "flyback.sample";
    private const string Ripple = "flyback.sample.ripple";

    private static ModuleCatalog Shipped() => PluginHost.Load().Modules;

    [Fact]
    public void A_plugins_modules_reach_the_catalogue()
    {
        var catalog = Shipped();

        catalog.HasProvider(Provider).ShouldBeTrue();
        catalog.Get(Ripple).ShouldNotBeNull();
    }

    [Fact]
    public void The_engines_own_modules_are_still_there()
    {
        Shipped().Get("output").ShouldNotBeNull();
    }

    [Fact]
    public void A_plugin_module_is_attributed_to_the_plugin()
    {
        Shipped().ProviderOf(Ripple)!.Name.ShouldBe("Sample modules");
    }

    /// <summary>
    /// The point of the whole exercise: a module defined in another assembly
    /// lowers to ops in the same flat program as a built-in one.
    /// </summary>
    [Fact]
    public void A_plugin_module_compiles_into_the_program()
    {
        var catalog = Shipped();

        var patch = new Patch();
        var ripple = NodeInstance.Create(catalog.Require(Ripple), 0, 0);
        var output = NodeInstance.Create(catalog.Require("output"), 200, 0);

        patch.Nodes.Add(ripple);
        patch.Nodes.Add(output);
        patch.Connect(ripple.Id, 0, output.Id, 0);

        var result = patch.CompileForVideo(catalog);

        result.Issues.ShouldBeEmpty();
        result.Program.Ops.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_patch_using_it_records_the_plugin_and_reopens()
    {
        var catalog = Shipped();

        var patch = new Patch();
        patch.Nodes.Add(NodeInstance.Create(catalog.Require(Ripple), 0, 0));

        var json = PatchIo.ToJson(patch, catalog);

        json.ShouldContain(Provider);
        PatchIo.Read(json, catalog).IsComplete.ShouldBeTrue();

        var elsewhere = PatchIo.Read(json, NodeCatalog.BuiltIn);
        elsewhere.IsComplete.ShouldBeFalse();
        elsewhere.MissingProviders.ShouldHaveSingleItem().Id.ShouldBe(Provider);
    }
    /// <summary>
    /// Every section the engine names has something in it once the shipped
    /// plugins are loaded, so the list does not accumulate words for modules that
    /// were moved or removed.
    /// </summary>
    /// <remarks>
    /// Asked here rather than in the engine's own tests because three of the
    /// sections are held entirely by plugins — Forms by Picture, Shaping by
    /// Voice, Time effects by Effects — and that is the arrangement rather than
    /// an accident of it. The names are declared centrally precisely so a plugin
    /// files into a section the engine already knows instead of inventing a word
    /// beside one that exists; a section the engine names and does not fill is
    /// what that looks like from the engine's side.
    /// </remarks>
    [Fact]
    public void Every_section_the_engine_names_is_filled_by_something_shipped()
    {
        var catalog = PluginHost.Load().Modules;

        foreach (var category in ModuleCategories.All)
            catalog.All.ShouldContain(
                def => def.Category == category,
                $"'{category}' is a section with no modules in it");
    }

    /// <summary>
    /// And nothing that ships in the box files itself under a word the engine
    /// does not know.
    /// </summary>
    /// <remarks>
    /// A plugin from elsewhere may — see the engine's own tests, where a section
    /// nothing knows sorts last rather than being refused, and see the Sample
    /// fixture beside this one, which files its modules under a section of its
    /// own precisely to prove that works. What ships should not, because every
    /// one of these had a section to go in.
    /// </remarks>
    [Fact]
    public void Nothing_shipped_invents_a_section_of_its_own()
    {
        string[] shipped = ["flyback.picture", "flyback.voice", "flyback.effects"];

        var catalog = PluginHost.Load().Modules;

        var ours = catalog.All.Where(def =>
            catalog.ProviderOf(def.TypeId) is { } from
            && (from.Id == NodeCatalog.BuiltInProvider.Id || shipped.Contains(from.Id)));

        foreach (var def in ours)
            ModuleCategories.All.ShouldContain(
                def.Category,
                $"'{def.Name}' is filed under '{def.Category}', which is not a section");
    }
}
