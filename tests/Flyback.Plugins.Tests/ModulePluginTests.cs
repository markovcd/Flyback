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
}
