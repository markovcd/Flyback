using System.Runtime.Loader;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>Decision models loaded off disk, the way a real one is.</summary>
public class DecisionPluginTests
{
    private static PluginCatalog Loaded => ShippedPlugins.Loaded;

    [Fact]
    public void Both_models_in_the_box_and_the_scripted_one_reach_the_catalog()
    {
        Loaded.DecisionModels.Select(m => m.Id).ShouldBe(["scripted", "systemone"], ignoreOrder: true);
    }

    [Fact]
    public void The_contract_keeps_one_identity_across_the_boundary()
    {
        var model = Loaded.DecisionModel("scripted")!;

        AssemblyLoadContext.GetLoadContext(model.GetType().Assembly).ShouldNotBe(AssemblyLoadContext.Default);
        typeof(IDecisionModel).IsInstanceOfType(model).ShouldBeTrue();
    }

    [Fact]
    public void The_one_offered_is_the_highest_priority()
    {
        Loaded.PreferredDecisionModel!.Id.ShouldBe("systemone");
    }

    [Fact]
    public void A_model_is_answerable_about_itself_without_being_asked_anything()
    {
        foreach (var model in Loaded.DecisionModels)
        {
            model.Name.ShouldNotBeNullOrWhiteSpace();
            Should.NotThrow(() => model.Form(SettingValues.None));
            Should.NotThrow(() => model.Unavailable(DecisionConfig.Unset));
        }
    }

    [Fact]
    public async Task An_answer_crosses_the_boundary_as_the_host_declared_it()
    {
        var model = Loaded.DecisionModel("scripted")!;

        var decision = await model.DecideAsync(
            DecisionRequest.One("we were billed twice", "money", new Question.YesNo("Is this about money?")),
            DecisionConfig.Unset,
            TestContext.Current.CancellationToken);

        decision.Answers["money"].ShouldBeOfType<Answer.YesNo>().Probability.ShouldBe(1);
    }

    [Fact]
    public void Two_plugins_offering_one_model_both_lose_it()
    {
        var catalog = PluginHost.LoadTypes(typeof(FirstDecider), typeof(SecondDecider));

        catalog.DecisionModels.ShouldBeEmpty();
        catalog.Problems.Select(p => p.ToString()).ShouldBe(
        [
            "test.first-decider: decision model 'twin' is registered by Second decider as well, so neither is used.",
            "test.second-decider: decision model 'twin' is registered by First decider as well, so neither is used.",
        ]);
    }

    [Fact]
    public void A_plugin_that_offers_a_model_says_so_before_it_is_installed()
    {
        using var dll = File.OpenRead(Path.Combine(PluginHost.DefaultDirectory, "SystemOne", "Flyback.Plugins.SystemOne.dll"));

        AssemblyFacts.Of(dll)!.Adds.ShouldContain("a decision model");
    }

    public sealed class FirstDecider : IFlybackPlugin
    {
        public PluginInfo Info { get; } = new("test.first-decider", "First decider");

        public void Register(IPluginRegistry registry) => registry.AddDecisionModel(new Twin());
    }

    public sealed class SecondDecider : IFlybackPlugin
    {
        public PluginInfo Info { get; } = new("test.second-decider", "Second decider");

        public void Register(IPluginRegistry registry) => registry.AddDecisionModel(new Twin());
    }

    private sealed class Twin : IDecisionModel
    {
        public string Id => "twin";

        public string Name => "Twin";

        public int Priority => 0;

        public Assist.AssistantCredential? Credential => null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel) =>
            throw new NotSupportedException();
    }
}
