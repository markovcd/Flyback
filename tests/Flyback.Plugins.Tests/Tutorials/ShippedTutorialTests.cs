using Flyback.Engine.Language;
using Flyback.Engine.Tutorials;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Tutorials;

/// <summary>The tutorials as shipped, checked against the in-box plugins, so a step may use a plugin's modules or open on its preset.</summary>
public class ShippedTutorialTests
{
    private static readonly PluginCatalog Loaded = ShippedPlugins.Loaded;

    public static TheoryData<string> Ids => [.. ShippedTutorials.Order];

    [Fact]
    public void Every_tutorial_file_is_in_the_order_and_every_one_in_the_order_is_a_file() =>
        ShippedTutorials.Carried.Order().ShouldBe(ShippedTutorials.Order.Order());

    [Theory]
    [MemberData(nameof(Ids))]
    public void A_tutorial_opens_on_a_shipped_preset(string id) =>
        Loaded.Presets.Select(p => p.Name).ShouldContain(ShippedTutorials.Find(id)!.From);

    /// <summary>A step's goal is a patch somebody could have built, so it meets itself.</summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void Every_goal_builds_and_meets_itself(string id)
    {
        foreach (var step in ShippedTutorials.Find(id)!.Steps.Where(s => s.Goal is not null))
        {
            var load = PatchLanguage.Build(step.Goal!, Loaded.Modules);
            load.Issues.ShouldBeEmpty($"{id}, '{step.Title}': {load.Report}");

            StepCheck.Against(load.Patch, load.Patch, Loaded.Modules).ShouldBe(StepVerdict.Met, $"{id}, '{step.Title}'");
        }
    }

    /// <summary>Nothing is checked off before it is done: each goal is unmet by the patch the step before left.</summary>
    [Theory]
    [MemberData(nameof(Ids))]
    public void No_step_is_met_by_the_one_before_it(string id)
    {
        var tutorial = ShippedTutorials.Find(id)!;
        var before = Loaded.Presets.Single(p => p.Name == tutorial.From).Build(Loaded.Modules);

        foreach (var step in tutorial.Steps.Where(s => s.Goal is not null))
        {
            StepCheck.Against(before, step.Goal!, Loaded.Modules).Done.ShouldBeFalse($"{id}, '{step.Title}'");
            before = PatchLanguage.Build(step.Goal!, Loaded.Modules).Patch;
        }
    }

    [Fact]
    public void A_tutorial_is_found_by_its_title_whatever_the_case() =>
        ShippedTutorials.Find("your FIRST patch")!.Id.ShouldBe("first-patch");
}
