using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>A phrase finds the modules it means, through a category first, and finds nothing without a model.</summary>
public class ModuleFinderTests
{
    private static IReadOnlyList<NodeDef> Candidates => [.. NodeCatalog.BuiltIn.All.Where(d => !NodeCatalog.IsSink(d.TypeId))];

    private static ModuleFinder Finder(IDecisionModel? model, string? chosen = null) => new(new Decisions(
        model is null ? [] : [model],
        new DecisionSettings { Model = chosen },
        new Credentials(null),
        new ModelStore(null)));

    [Fact]
    public async Task A_phrase_finds_the_module_it_describes_first()
    {
        var model = new RankingDecider(("space.kaleidoscope", 0.8), ("none", 0.6));

        var found = await Finder(model).Find("a mirror maze of shards", Candidates, TestContext.Current.CancellationToken);

        found.ShouldHaveSingleItem().Module.TypeId.ShouldBe("space.kaleidoscope");
    }

    [Fact]
    public async Task It_asks_about_every_module_by_name_ten_to_a_question_in_one_request()
    {
        var model = new RankingDecider();

        await Finder(model).Find("a mirror maze of shards", Candidates, TestContext.Current.CancellationToken);

        var questions = model.Asked.ShouldHaveSingleItem().Questions.Values.Cast<Question.Choice>().ToList();

        questions.SelectMany(q => q.Options).Where(o => o.Label != "none").Select(o => o.Label).ShouldBe(Candidates.Select(d => d.TypeId));
        questions.SelectMany(q => q.Options).Where(o => o.Label != "none").Select(o => o.Description).ShouldBe(Candidates.Select(d => d.Name));

        foreach (var question in questions)
        {
            question.Options.Count.ShouldBeLessThanOrEqualTo(ModuleFinder.PerQuestion + 1);
            question.Options[^1].Label.ShouldBe("none");
        }
    }

    [Fact]
    public async Task A_module_that_loses_to_none_of_these_is_not_found()
    {
        var model = new RankingDecider(("none", 0.9));

        (await Finder(model).Find("a mirror maze of shards", Candidates, TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_model_nothing_is_found_and_nothing_asked()
    {
        var model = new RankingDecider();

        (await Finder(model, DecisionSettings.Off).Find("a mirror maze", Candidates, TestContext.Current.CancellationToken)).ShouldBeEmpty();
        model.Asked.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("sine", 3, false)]
    [InlineData("sine", 0, true)]
    [InlineData("a slow wobble", 4, true)]
    [InlineData("ab", 0, false)]
    public void A_phrase_is_asked_about_only_where_spelling_finds_little(string phrase, int spelled, bool asked)
    {
        ModuleFinder.Wanted(phrase, spelled).ShouldBe(asked);
    }
}
