using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>A patch's complaints, judged by how likely each is why it is silent or dark.</summary>
public class IssueTriageTests
{
    private static IssueTriage Triage(IDecisionModel model) =>
        new(new Decisions([model], new DecisionSettings(), new Credentials(null), new ModelStore(null)));

    [Fact]
    public async Task Each_complaint_is_one_score_about_the_patch()
    {
        var model = new Blaming("volume");

        var likely = await Triage(model).Likelihoods(["the Output's volume is 0", "a Sine is not wired"], "A patch of 2 modules", TestContext.Current.CancellationToken);

        likely.ShouldBe([1.0, 0.0]);

        var asked = model.Asked.ShouldHaveSingleItem();
        asked.State.ShouldBe("A patch of 2 modules");
        asked.Questions.Values.ShouldAllBe(q => q is Question.Score);
    }

    [Fact]
    public async Task One_complaint_needs_no_ordering_and_asks_nothing()
    {
        var model = new Blaming("volume");

        (await Triage(model).Likelihoods(["only this"], "p", TestContext.Current.CancellationToken)).ShouldBeNull();
        model.Asked.ShouldBeEmpty();
    }

    [Fact]
    public void The_likeliest_comes_first_and_ties_keep_their_order()
    {
        IssueTriage.Ordered(["a", "b", "c", "d"], [0.2, 0.9, 0.2, 0.5]).ShouldBe(["b", "d", "a", "c"]);
    }

    [Fact]
    public void A_summary_names_the_modules_and_counts_the_wires()
    {
        var patch = new Patch();
        patch.Nodes.Add(new NodeInstance { Id = Guid.NewGuid(), TypeId = "osc.sine" });

        IssueTriage.Summary(patch, NodeCatalog.BuiltIn).ShouldBe("A patch of 1 module and 0 wires: Sine.");
    }

    /// <summary>Scores a complaint top that mentions one word, and bottom otherwise.</summary>
    internal sealed class Blaming(string word) : IDecisionModel
    {
        public List<DecisionRequest> Asked { get; } = [];

        public string Id => "blaming";

        public string Name => "Blaming";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel)
        {
            Asked.Add(request);

            return Task.FromResult(new Decision("blaming", request.Questions.ToDictionary(q => q.Key, q =>
            {
                var score = (Question.Score)q.Value;
                var top = score.Instructions.Contains(word, StringComparison.OrdinalIgnoreCase) ? score.Levels.Count - 1 : 0;

                return (Answer)new Answer.Scored(top, score.Levels, [.. score.Levels.Select((_, i) => i == top ? 1.0 : 0)], 1);
            }), DecisionUsage.None));
        }
    }
}
