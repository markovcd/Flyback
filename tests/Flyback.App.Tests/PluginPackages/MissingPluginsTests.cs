using Flyback.App.PluginPackages;
using Flyback.Core.Graph;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.PluginPackages;

/// <summary>Looking on the plugin site for what a patch could not be opened without.</summary>
public sealed class MissingPluginsTests
{
    private static readonly ModuleProvider Ripples = new("ann.ripples", "Ripples");
    private static readonly ModuleProvider Grain = new("bob.grain", "Grain");

    private static PatchLoad Short(IReadOnlyList<ModuleProvider> missing, params string[] modules) =>
        new(new Patch(), missing, modules);

    [Fact]
    public async Task A_patch_finds_its_plugin_by_the_module_it_names()
    {
        using var fake = new FakePluginSite(
            new Shared("a1", "Ripples", Author: "Ann", Modules: ["ann.ripples.ring"]),
            new Shared("b1", "Grain", Modules: ["bob.grain.noise"]));

        var found = await MissingPlugins.FoundAsync(fake.Site(), Short([Ripples], "ann.ripples.ring"), CancellationToken.None);

        found.ShouldHaveSingleItem().Plugin.Name.ShouldBe("Ripples");
        fake.Asked.ShouldHaveSingleItem().Query.ShouldContain("module=ann.ripples.ring");
    }

    [Fact]
    public async Task A_plugin_that_declares_nothing_the_patch_names_is_not_offered()
    {
        using var fake = new FakePluginSite(new Shared("b1", "Grain", Modules: ["bob.grain.noise"]));

        var found = await MissingPlugins.FoundAsync(fake.Site(), Short([Ripples], "ann.ripples.ring"), CancellationToken.None);

        found.ShouldBeEmpty();
    }

    /// <summary>
    /// A stale stamp naming a plugin whose modules are all gone from the patch. There is
    /// nothing to look up by, and a search by name could offer anything.
    /// </summary>
    [Fact]
    public async Task A_provider_with_no_module_left_in_the_patch_is_not_asked_about()
    {
        using var fake = new FakePluginSite(new Shared("a1", "Ripples", Modules: ["ann.ripples.ring"]));

        var found = await MissingPlugins.FoundAsync(fake.Site(), Short([Ripples]), CancellationToken.None);

        found.ShouldBeEmpty();
        fake.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Two_plugins_short_are_both_offered_in_the_order_the_patch_names_them()
    {
        using var fake = new FakePluginSite(
            new Shared("a1", "Ripples", Modules: ["ann.ripples.ring"]),
            new Shared("b1", "Grain", Modules: ["bob.grain.noise"]));

        var found = await MissingPlugins.FoundAsync(
            fake.Site(), Short([Grain, Ripples], "ann.ripples.ring", "bob.grain.noise"), CancellationToken.None);

        found.Select(f => f.Plugin.Name).ShouldBe(["Grain", "Ripples"]);
    }

    /// <summary>The site having one of the two is still worth offering; the refusal said both.</summary>
    [Fact]
    public async Task The_one_of_them_the_site_has_is_offered_by_itself()
    {
        using var fake = new FakePluginSite(new Shared("a1", "Ripples", Modules: ["ann.ripples.ring"]));

        var found = await MissingPlugins.FoundAsync(
            fake.Site(), Short([Ripples, Grain], "ann.ripples.ring", "bob.grain.noise"), CancellationToken.None);

        found.ShouldHaveSingleItem().Plugin.Name.ShouldBe("Ripples");
    }

    [Fact]
    public async Task One_plugin_short_twice_over_is_offered_once()
    {
        using var fake = new FakePluginSite(new Shared("a1", "Ripples", Modules: ["ann.ripples.ring", "bob.grain.noise"]));

        var found = await MissingPlugins.FoundAsync(
            fake.Site(), Short([Ripples, Grain], "ann.ripples.ring", "bob.grain.noise"), CancellationToken.None);

        found.ShouldHaveSingleItem().Id.ShouldBe("a1");
        fake.Asked.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_module_of_another_plugin_that_starts_the_same_is_not_taken_for_this_one()
    {
        using var fake = new FakePluginSite(new Shared("a1", "Ripples", Modules: ["ann.ripples.ring"]));

        // "ann.ripplesong" begins with the provider's id and is not one of its modules.
        MissingPlugins.ModuleOf(Short([Ripples], "ann.ripplesong.hum"), Ripples).ShouldBeNull();

        (await MissingPlugins.FoundAsync(fake.Site(), Short([Ripples], "ann.ripplesong.hum"), CancellationToken.None)).ShouldBeEmpty();
    }
}
