using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests.Decide;

/// <summary>A phrase finds the modules it means, and finds nothing without a model.</summary>
public class ModuleFinderTests
{
    private static IReadOnlyList<NodeDef> Candidates => [.. NodeCatalog.BuiltIn.All.Where(d => !NodeCatalog.IsSink(d.TypeId))];

    private static ModuleFinder Finder(IDecisionModel? model, string? chosen = null) => new(new Decisions(
        model is null ? [] : [model],
        new DecisionSettings { Model = chosen },
        new Credentials(null),
        new ModelStore(null)));

    private static string Label(string typeId) => ModuleFinder.Labels(Candidates)[Candidates.Single(d => d.TypeId == typeId)];

    [Fact]
    public async Task A_phrase_finds_the_module_it_describes_first()
    {
        var model = new RankingDecider((Label("space.kaleidoscope"), 0.8), ("none", 0.6));

        var found = await Finder(model).Find("a mirror maze of shards", Candidates, TestContext.Current.CancellationToken);

        found.ShouldHaveSingleItem().Module.TypeId.ShouldBe("space.kaleidoscope");
    }

    [Fact]
    public async Task It_asks_about_every_module_by_name_and_words_ten_to_a_question_in_order_and_in_reverse_in_one_request()
    {
        var model = new RankingDecider();

        await Finder(model).Find("a mirror maze of shards", Candidates, TestContext.Current.CancellationToken);

        var questions = model.Asked[0].Questions.Values.Cast<Question.Choice>().ToList();
        var asked = questions.SelectMany(q => q.Options).Where(o => o.Label != "none").ToList();
        var labels = ModuleFinder.Labels(Candidates);

        asked.Select(o => o.Label).ShouldBe([.. Candidates.Select(d => labels[d]), .. Candidates.Reverse().Select(d => labels[d])]);
        asked.ShouldAllBe(o => o.Description.Length == 0, "the words are in the label, where the model reads them");
        Label("audio.reverb").ShouldBe("Reverb (room, hall, space)");

        foreach (var question in questions)
        {
            question.Options.Count.ShouldBeLessThanOrEqualTo(ModuleFinder.PerQuestion + 1);
            question.Options[^1].Label.ShouldBe("none");
        }
    }

    [Fact]
    public async Task A_module_s_margin_over_none_is_averaged_across_both_orders()
    {
        var first = Label(Candidates[0].TypeId);
        var model = new RankingDecider
        {
            Script = (question, label) => label switch
            {
                "none" => 0.3,
                _ when label == first => question == "modules0" ? 0.5 : 0.0,
                _ when label == Label("space.kaleidoscope") => 0.5,
                _ => 0.0,
            },
        };

        var found = await Finder(model).Find("a mirror maze of shards", Candidates, TestContext.Current.CancellationToken);

        found.Select(f => f.Module.TypeId).ShouldBe(["space.kaleidoscope"], "the first module beat none once and lost to it by more the other time");
        found[0].Probability.ShouldBe(0.5);
    }

    [Fact]
    public async Task The_ten_that_did_best_are_asked_about_once_more_and_the_answer_orders_them()
    {
        var kaleidoscope = Label("space.kaleidoscope");
        var mirror = Label("space.mirror");
        var model = new RankingDecider
        {
            Script = (question, label) => (question, label) switch
            {
                (_, "none") => 0.2,
                ("final", _) when label == kaleidoscope => 0.9,
                ("final", _) when label == mirror => 0.1,
                (_, _) when label == mirror => 0.6,
                (_, _) when label == kaleidoscope => 0.5,
                _ => 0.0,
            },
        };

        var found = await Finder(model).Find("a mirror maze of shards", Candidates, TestContext.Current.CancellationToken);

        model.Asked.Count.ShouldBe(2);
        var final = model.Asked[1].Questions["final"].ShouldBeOfType<Question.Choice>();
        final.Options.Select(o => o.Label).ShouldBe([mirror, kaleidoscope, "none"], "the finalists in the first round's order, then none");
        found.Select(f => f.Module.TypeId).ShouldBe(["space.kaleidoscope", "space.mirror"], "the final round's answer leads");
        found[0].Probability.ShouldBe(0.9);
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

    [Fact]
    public void Two_modules_of_one_name_are_told_apart_by_category_and_then_by_type_id()
    {
        NodeDef Named(string typeId, string category) => new(typeId, "Scale", category, [], [], (_, _) => []);

        var labels = ModuleFinder.Labels([Named("space.scale", "Geometry"), Named("x.scale", "Pitch"), Named("y.scale", "Pitch")]);

        labels.Values.ShouldBe(["Scale", "Scale (Pitch)", "Scale (y.scale)"]);
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
