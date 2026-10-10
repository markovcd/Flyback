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
    public async Task Each_complaint_is_the_text_one_yes_no_is_asked_about()
    {
        var model = new Blaming("volume");

        var likely = await Triage(model).Likelihoods(["the Output's volume is 0", "a Sine is not wired"], TestContext.Current.CancellationToken);

        likely.ShouldBe([1.0, 0.0]);

        model.Asked.Select(r => r.State).Order().ShouldBe(["a Sine is not wired", "the Output's volume is 0"]);
        foreach (var request in model.Asked)
            request.Questions.Values.Single().ShouldBeOfType<Question.YesNo>().Instructions.ShouldBe(IssueTriage.Question);
    }

    [Fact]
    public async Task One_complaint_needs_no_ordering_and_asks_nothing()
    {
        var model = new Blaming("volume");

        (await Triage(model).Likelihoods(["only this"], TestContext.Current.CancellationToken)).ShouldBeNull();
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

    /// <summary>Says yes to a complaint that mentions one word, and no to any other.</summary>
    /// <remarks>Records under a lock: the triage asks about every complaint at once.</remarks>
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
            lock (Asked) Asked.Add(request);

            return Task.FromResult(new Decision("blaming", request.Questions.ToDictionary(q => q.Key, q =>
            {
                return (Answer)new Answer.YesNo(request.State.Contains(word, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            }), DecisionUsage.None));
        }
    }
}
